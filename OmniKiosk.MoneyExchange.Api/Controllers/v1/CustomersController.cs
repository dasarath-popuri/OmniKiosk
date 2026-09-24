using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using OmniKiosk.MoneyExchange.Api.Models.v1;
using System.Data;

namespace OmniKiosk.MoneyExchange.Api.Controllers.v1
{
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "KioskOnly")]
    public class CustomersController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<CustomersController> _logger;

        // *** UNCONFIRMED - see the note in Ksk_CheckExistingCustomer.sql ***
        // The one real SenderMaster row I've seen had IdType=2 for a
        // passport-format IdNo. Guessing IC=1 by elimination, not from any
        // confirmed code table.
        private static readonly Dictionary<string, int> IdTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IC"] = 1,
            ["Passport"] = 2
        };

        public CustomersController(IConfiguration config, ILogger<CustomersController> logger)
        {
            _connectionString = config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
            _logger = logger;
        }

        [HttpGet("check")]
        public async Task<ActionResult<CustomerCheckResult>> CheckCustomer([FromQuery] string idType, [FromQuery] string idNo)
        {
            if (!IdTypeMap.TryGetValue(idType, out var idTypeCode))
            {
                _logger.LogWarning("CheckCustomer called with unrecognized IdType '{IdType}'", idType);
                return BadRequest(new { error = $"Unrecognized IdType '{idType}'. Expected IC or Passport." });
            }

            using var con = new SqlConnection(_connectionString);

            var row = await con.QuerySingleOrDefaultAsync<CustomerCheckResult>(
                "KSK_CheckExistingCustomer",
                new { IdType = idTypeCode, IdNo = idNo },
                commandType: CommandType.StoredProcedure);

            if (row == null)
            {
                _logger.LogInformation("Customer check: new customer (IdType={IdType}, IdNo=***{Last4})", idType, idNo.Length >= 4 ? idNo[^4..] : idNo);
                return Ok(new CustomerCheckResult { Found = false });
            }

            row.Found = true;

            if (row.IsBlocked == true)
                _logger.LogWarning("Customer check: SenderID {SenderId} is flagged IsBlocked=true", row.SenderId);

            _logger.LogInformation("Customer check: existing customer, SenderID {SenderId}", row.SenderId);
            return Ok(row);
        }

        // POST /api/v1/Customers - creates a new SenderMaster record.
        // *** Calls KSK_CreateNewSender, a fallback proc - see the notes at
        // the top of that .sql file. If a real sender-creation proc already
        // exists in this system, this endpoint should call that instead. ***
        [HttpPost]
        public async Task<ActionResult<CreateCustomerResponse>> CreateCustomer([FromBody] CreateCustomerRequest request)
        {
            if (!IdTypeMap.TryGetValue(request.IdType, out var idTypeCode))
            {
                _logger.LogWarning("CreateCustomer called with unrecognized IdType '{IdType}'", request.IdType);
                return BadRequest(new { error = $"Unrecognized IdType '{request.IdType}'. Expected IC or Passport." });
            }

            using var con = new SqlConnection(_connectionString);

            // Decode base64 back to raw bytes here - the wire format is
            // base64 (JSON has no binary type), but SenderMaster.Picture1
            // is varbinary(max) and needs actual bytes, not base64 text.
            // A missing or malformed photo shouldn't fail the whole
            // customer creation - log it and proceed with no photo rather
            // than block a legitimate customer over a capture issue.
            byte[]? picture1Bytes = null;
            if (!string.IsNullOrWhiteSpace(request.Picture1Base64))
            {
                try
                {
                    picture1Bytes = Convert.FromBase64String(request.Picture1Base64);
                }
                catch (FormatException)
                {
                    _logger.LogWarning("CreateCustomer: Picture1Base64 was not valid base64, proceeding without a photo");
                }
            }

            // Same treatment as Picture1Base64 above - a missing or
            // malformed document image shouldn't fail the whole customer
            // creation either. Will be null for MyKad customers currently
            // (no capture mechanism exists for that document type yet).
            byte[]? idDocumentImageBytes = null;
            if (!string.IsNullOrWhiteSpace(request.IdDocumentImageBase64))
            {
                try
                {
                    idDocumentImageBytes = Convert.FromBase64String(request.IdDocumentImageBase64);
                }
                catch (FormatException)
                {
                    _logger.LogWarning("CreateCustomer: IdDocumentImageBase64 was not valid base64, proceeding without a document image");
                }
            }

            var p = new DynamicParameters();
            p.Add("@BranchId", request.BranchId);
            p.Add("@IdType", idTypeCode);
            p.Add("@IdNo", request.IdNo);
            p.Add("@FullName", request.FullName);
            p.Add("@Nationality", request.Nationality);
            p.Add("@DateOfBirth", request.DateOfBirth);
            p.Add("@Gender", request.Gender);
            p.Add("@MobileNo", request.MobileNo);
            p.Add("@IdExpiryDate", request.IdExpiryDate);
            p.Add("@Picture1", picture1Bytes, dbType: DbType.Binary);
            p.Add("@IdDocumentImage", idDocumentImageBytes, dbType: DbType.Binary);
            p.Add("@NewSenderId", dbType: DbType.Int32, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_CreateNewSender", p, commandType: CommandType.StoredProcedure);
                var newSenderId = p.Get<int>("@NewSenderId");

                _logger.LogInformation("Created new SenderMaster record, SenderID {SenderId}, IdType {IdType}", newSenderId, request.IdType);

                return Ok(new CreateCustomerResponse { SenderId = newSenderId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create SenderMaster record for IdType {IdType}", request.IdType);
                return StatusCode(500, new { error = "Could not create customer record." });
            }
        }

        // POST /api/v1/Customers/{senderId}/screen?transGuid=... - watchlist
        // screening via KSK_TransScreening, a kiosk-owned proc with its own
        // copy of the matching logic - not a call into rtr_METranScreening.
        // transGuid is supplied by the caller (created once at the start of
        // the whole money exchange flow), not generated here.
        [HttpPost("{senderId}/screen")]
        public async Task<ActionResult<ScreeningResult>> ScreenCustomer(int senderId, [FromQuery] string transGuid)
        {
            if (string.IsNullOrWhiteSpace(transGuid))
                return BadRequest(new { error = "transGuid is required." });

            using var con = new SqlConnection(_connectionString);

            var p = new DynamicParameters();
            p.Add("@TransGuid", transGuid);
            p.Add("@SenderId", senderId);
            p.Add("@HasMatch", dbType: DbType.Boolean, direction: ParameterDirection.Output);

            try
            {
                await con.ExecuteAsync("KSK_TransScreening", p, commandType: CommandType.StoredProcedure);

                var result = new ScreeningResult { HasMatch = p.Get<bool>("@HasMatch") };

                if (result.HasMatch)
                    _logger.LogWarning("Watchlist screening: MATCH for SenderID {SenderId}, TransGuid {TransGuid}", senderId, transGuid);
                else
                    _logger.LogInformation("Watchlist screening: clear for SenderID {SenderId}, TransGuid {TransGuid}", senderId, transGuid);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchlist screening failed for SenderID {SenderId}", senderId);
                return StatusCode(500, new { error = "Could not complete screening." });
            }
        }
    }
}