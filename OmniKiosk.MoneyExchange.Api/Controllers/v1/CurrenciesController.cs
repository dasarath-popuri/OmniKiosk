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
    public class CurrenciesController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<CurrenciesController> _logger;

        public CurrenciesController(IConfiguration config, ILogger<CurrenciesController> logger)
        {
            _connectionString = config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
            _logger = logger;
        }

        // GET /api/v1/Currencies - calls KSK_GetCurrencies. Any future change
        // to which currencies show or how rates are picked is a proc update,
        // not an API redeploy.
        [HttpGet]
        public async Task<ActionResult<List<CurrencyDto>>> GetCurrencies()
        {
            using var con = new SqlConnection(_connectionString);

            var currencies = (await con.QueryAsync<CurrencyDto>(
                "KSK_GetCurrencies", commandType: CommandType.StoredProcedure)).ToList();

            _logger.LogInformation("Returned {Count} enabled currencies", currencies.Count);

            var missingRates = currencies.Where(c => c.BuyRate == 0).Select(c => c.CurrencyCode).ToList();
            if (missingRates.Count > 0)
                _logger.LogWarning("Currencies enabled with no active rate: {Currencies}", string.Join(", ", missingRates));

            return Ok(currencies);
        }

        // GET /api/v1/Currencies/denominations/MYR - calls KSK_GetDenominations.
        [HttpGet("denominations/{currencyCode}")]
        public async Task<ActionResult<List<DenominationDto>>> GetDenominations(string currencyCode)
        {
            using var con = new SqlConnection(_connectionString);

            var denoms = (await con.QueryAsync<DenominationDto>(
                "KSK_GetDenominations",
                new { CurrencyCode = currencyCode },
                commandType: CommandType.StoredProcedure)).ToList();

            _logger.LogInformation("Returned {Count} denominations for {CurrencyCode}", denoms.Count, currencyCode);

            return Ok(denoms);
        }

        // GET /api/v1/Currencies/denomination-breakdown - calls
        // KSK_GetDenominationBreakdown, the pre-dispense availability
        // check. Returns two result sets: the breakdown rows, then a
        // single UnfulfilledAmount value (>0 means the kiosk's current
        // cassette mix cannot fully cover targetAmount).
        [HttpGet("denomination-breakdown")]
        public async Task<ActionResult<DenominationBreakdownResponseDto>> GetDenominationBreakdown(
            [FromQuery] string kioskId, [FromQuery] decimal targetAmount)
        {
            using var con = new SqlConnection(_connectionString);

            using var multi = await con.QueryMultipleAsync(
                "KSK_GetDenominationBreakdown",
                new { KioskId = kioskId, TargetAmount = targetAmount },
                commandType: CommandType.StoredProcedure);

            var breakdown = (await multi.ReadAsync<DenominationBreakdownLineDto>()).ToList();
            var unfulfilled = await multi.ReadSingleAsync<decimal>();

            _logger.LogInformation(
                "Denomination breakdown for KioskId={KioskId}, target RM{Target}: {LineCount} lines, unfulfilled RM{Unfulfilled}",
                kioskId, targetAmount, breakdown.Count, unfulfilled);

            return Ok(new DenominationBreakdownResponseDto { Breakdown = breakdown, UnfulfilledAmount = unfulfilled });
        }

        // GET /api/v1/Currencies/dial-codes - KSK_GetCountryDialCodes.
        // See that proc's own header comment for the flagged assumption
        // about the Country table's actual dial-code column name.
        [HttpGet("dial-codes")]
        public async Task<ActionResult<List<CountryDialCodeDto>>> GetDialCodes()
        {
            using var con = new SqlConnection(_connectionString);

            try
            {
                var codes = (await con.QueryAsync<CountryDialCodeDto>(
                    "KSK_GetCountryDialCodes", commandType: CommandType.StoredProcedure)).ToList();

                return Ok(codes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load country dial codes");
                return StatusCode(500, new { error = "Could not load dial codes." });
            }
        }
    }
}