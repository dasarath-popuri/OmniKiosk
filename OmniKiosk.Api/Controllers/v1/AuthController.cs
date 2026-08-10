using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using OmniKiosk.Config.Api.Models.v1;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace OmniKiosk.Config.Api.Controllers.v1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly string _connectionString;

        public AuthController(IConfiguration config)
        {
            _config = config;
            _connectionString = _config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
        }
        // Staff AND kiosk logins both go through UserProfile/UserRoles now -
        // no separate table, no separate endpoint. A kiosk terminal is
        // represented as a UserProfile row with RoleName = 'KIOSK'; LoginId
        // encodes which terminal it is (e.g. "KIOSK-K101"). One JWT either
        // way, with a MachineType claim ("Kiosk" or "Staff") that
        // MoneyExchange.Api's [Authorize(Policy="KioskOnly")] checks.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            using var con = new SqlConnection(_connectionString);

            var user = await con.QuerySingleOrDefaultAsync<UserProfileRow>(@"
                SELECT u.UserID, u.LoginId, u.Password, u.FirstName, u.LastName,
                       u.UserCode,
                       u.NoOfAttempts, u.PasswordLock, u.UserStatus, u.Status,
                       u.ExpiryDate, u.CompanyId, u.RoleID, r.RoleName
                FROM UserProfile u
                JOIN UserRoles r ON r.RoleID = u.RoleID
                WHERE u.LoginId = @LoginId",
                new { request.LoginId });

            if (user == null)
            {
                await WriteAuditLog(con, "Auth", $"Failed login - unknown LoginId '{request.LoginId}'", "SYSTEM", ip);
                return Unauthorized(new { error = "Invalid login ID or password" });
            }

            if (user.PasswordLock == true)
            {
                await WriteAuditLog(con, "Auth", $"Login blocked - account locked ({request.LoginId})", request.LoginId, ip);
                return StatusCode(423, new { error = "Account is locked. Contact a supervisor." });
            }

            if (user.Status != 1)
            {
                await WriteAuditLog(con, "Auth", $"Login blocked - account inactive ({request.LoginId})", request.LoginId, ip);
                return Unauthorized(new { error = "Account is not active" });
            }

            if (user.ExpiryDate.HasValue && user.ExpiryDate.Value < DateTime.Now)
            {
                await WriteAuditLog(con, "Auth", $"Login blocked - account expired ({request.LoginId})", request.LoginId, ip);
                return Unauthorized(new { error = "Account has expired" });
            }

            // Salted per GetSHAHash's real algorithm (plaintext + salt, SHA-512,
            // uppercase hex). The salt value itself (Security:PasswordSalt)
            // is still a placeholder pending the real value from GetTableInfo() -
            // see the comment on ComputeHash below.
            bool passwordOk = ComputeHash(request.Password) == user.Password;

            if (!passwordOk)
            {
                int attempts = user.NoOfAttempts + 1;
                bool lockNow = attempts >= 5;

                await con.ExecuteAsync(@"
                    UPDATE UserProfile SET NoOfAttempts = @attempts, PasswordLock = @lockNow
                    WHERE UserID = @UserID",
                    new { attempts, lockNow, user.UserID });

                await WriteAuditLog(con, "Auth", $"Failed login - wrong password ({request.LoginId}), attempt {attempts}", request.LoginId, ip);
                return Unauthorized(new { error = "Invalid login ID or password" });
            }

            await con.ExecuteAsync(@"
                UPDATE UserProfile SET NoOfAttempts = 0, LastLogDate = GETDATE()
                WHERE UserID = @UserID",
                new { user.UserID });

            await WriteAuditLog(con, "Auth", $"Successful login ({request.LoginId})", request.LoginId, ip);

            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            var (token, expiresAt) = IssueToken(user, fullName);
            return Ok(new LoginResponse
            {
                Token = token,
                ExpiresAtUtc = expiresAt,
                FullName = fullName,
                Role = user.RoleName,
                UserId = user.UserID,
                UserCode = user.UserCode ?? ""
            });
        }

        // Matches the existing GetSHAHash(inputString) exactly: SHA-512 of
        // (inputString + salt), salt appended after the input, not before.
        // The salt itself comes from configuration - Security:PasswordSalt -
        // not compiled into source, same reasoning as Jwt:SecretKey and the
        // DB connection string. One place to update once the real value
        // (whatever GetTableInfo() actually returns) is confirmed.
        private string ComputeHash(string plaintext)
        {
            var salt = _config["Security:PasswordSalt"]
                ?? throw new InvalidOperationException("Security:PasswordSalt is not configured.");
            var bytes = SHA512.HashData(Encoding.UTF8.GetBytes(plaintext + salt));
            return Convert.ToHexString(bytes);
        }

        private (string token, DateTime expiresAtUtc) IssueToken(UserProfileRow user, string fullName)
        {
            bool isKiosk = string.Equals(user.RoleName?.Trim(), "KIOSK", StringComparison.OrdinalIgnoreCase);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.LoginId),
                new("UserId", user.UserID.ToString()),
                new("FullName", fullName),
                new(ClaimTypes.Role, user.RoleName ?? ""),
                new("CompanyId", user.CompanyId?.ToString() ?? ""),
                new("MachineType", isKiosk ? "Kiosk" : "Staff"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var secretKey = _config["Jwt:SecretKey"]
                ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Kiosks stay logged in longer than staff - an unattended
            // terminal shouldn't need someone to re-auth it overnight.
            var expires = DateTime.UtcNow.AddHours(isKiosk ? 24 : 8);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"] ?? "OmniKiosk.Api",
                audience: _config["Jwt:Audience"] ?? "OmniKiosk.Wpf",
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }

        private static async Task WriteAuditLog(SqlConnection con, string module, string detail, string userAccount, string ip)
        {
            try
            {
                await con.ExecuteAsync(@"
                    INSERT INTO Ksk_AuditLogs (Timestamp, Module, ActionDetail, UserAccount, IPAddress, MachineFingerprint)
                    VALUES (GETUTCDATE(), @module, @detail, @userAccount, @ip, NULL)",
                    new { module, detail, userAccount, ip });
            }
            catch
            {
                // Audit logging must never be the reason a login request fails.
            }
        }

        private class UserProfileRow
        {
            public int UserID { get; set; }
            public string LoginId { get; set; } = "";
            public string Password { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string? LastName { get; set; }
            public string? UserCode { get; set; }
            public int NoOfAttempts { get; set; }
            public bool? PasswordLock { get; set; }
            public int UserStatus { get; set; }
            public int Status { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public int? CompanyId { get; set; }
            public int RoleID { get; set; }
            public string RoleName { get; set; } = "";
        }
    }
}