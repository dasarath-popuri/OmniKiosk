using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OmniKiosk.Config.Api.Models.v1;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace OmniKiosk.Config.Api.Controllers.v1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class AuthController : ControllerBase
    {
        // In production, this key MUST be securely stored in appsettings.json or Azure Key Vault!
        // It must be at least 32 characters long for high security.
        private const string SECRET_KEY = "OmniKioskSuperSecretEnterpriseKey2026!!!";

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // 1. Verify credentials (later, you will check this against your database)
            if (request.BranchCode == "00000" && request.Password == "KioskAdmin123!")
            {
                // 2. Create the claims (the data hidden inside the token)
                var claims = new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, request.BranchCode),
                    new Claim("BranchLevel", "Standard"),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                };

                // 3. Cryptographically sign the token
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SECRET_KEY));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: "OmniKiosk.Api",
                    audience: "OmniKiosk.Wpf",
                    claims: claims,
                    expires: DateTime.Now.AddHours(8), // Token lasts for 8 hours
                    signingCredentials: creds
                );

                // 4. Send it back to the WPF app
                return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
            }

            return Unauthorized(new { error = "Invalid Branch Code or Password" });
        }
    }
}