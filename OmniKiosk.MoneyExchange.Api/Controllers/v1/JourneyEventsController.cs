using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using OmniKiosk.MoneyExchange.Api.Models.v1;
using System.Data;

namespace OmniKiosk.MoneyExchange.Api.Controllers.v1
{
    // Full-journey audit trail (Ksk_KioskJourneyEvents) - captures every
    // meaningful step from the main menu onward, not just completed
    // transactions. Built for Money Exchange first; ServiceType on the
    // request/table lets Remittance log into this exact same table later
    // with no schema or endpoint change.
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Policy = "KioskOnly")]
    public class JourneyEventsController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<JourneyEventsController> _logger;

        public JourneyEventsController(IConfiguration config, ILogger<JourneyEventsController> logger)
        {
            _connectionString = config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
            _logger = logger;
        }

        // POST /api/v1/JourneyEvents - calls KSK_LogJourneyEvent. This
        // proc never throws (see its own header comment) - this endpoint
        // mirrors that posture and always returns 200, even on a logging
        // failure, since journey logging must never be able to interrupt
        // a real customer transaction over an HTTP-level error either.
        [HttpPost]
        public async Task<ActionResult> LogEvent([FromBody] LogJourneyEventRequest request)
        {
            try
            {
                using var con = new SqlConnection(_connectionString);
                var p = new DynamicParameters();
                p.Add("@SessionId", request.SessionId);
                p.Add("@ServiceType", request.ServiceType);
                p.Add("@BranchId", request.BranchId);
                p.Add("@KioskLoginId", request.KioskLoginId);
                p.Add("@EventType", request.EventType);
                p.Add("@StepName", request.StepName);
                p.Add("@Outcome", request.Outcome);
                p.Add("@Details", request.Details);
                p.Add("@TransactionId", request.TransactionId);
                p.Add("@LoggedOk", dbType: DbType.Boolean, direction: ParameterDirection.Output);

                await con.ExecuteAsync("KSK_LogJourneyEvent", p, commandType: CommandType.StoredProcedure);

                bool loggedOk = p.Get<bool>("@LoggedOk");
                if (!loggedOk)
                    _logger.LogWarning("KSK_LogJourneyEvent reported LoggedOk=false for session {SessionId}, event {EventType}/{StepName}",
                        request.SessionId, request.EventType, request.StepName);

                return Ok();
            }
            catch (Exception ex)
            {
                // Deliberately still 200 - see header comment. Logged
                // server-side so it's findable, but never surfaced as a
                // failure to the kiosk caller.
                _logger.LogError(ex, "Failed to log journey event for session {SessionId}", request.SessionId);
                return Ok();
            }
        }
    }
}