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
    }
}
