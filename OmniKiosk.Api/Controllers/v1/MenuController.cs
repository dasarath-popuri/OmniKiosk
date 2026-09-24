using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace OmniKiosk.Config.Api.Controllers.v1
{
    // Item 10 - simplified, flag-driven main menu. Lives here rather than
    // on MoneyExchange.Api since the main menu loads before the customer
    // has chosen any specific service - this is the API the kiosk already
    // talks to at boot, before either Money Exchange or Remittance starts.
    [Authorize]
    [Route("api/v1/[controller]")]
    [ApiController]
    public class MenuController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<MenuController> _logger;

        public MenuController(IConfiguration config, ILogger<MenuController> logger)
        {
            _connectionString = config.GetConnectionString("PhantomRemit")
                ?? throw new InvalidOperationException("ConnectionStrings:PhantomRemit is not configured.");
            _logger = logger;
        }

        // GET /api/v1/Menu/enabled-services - calls KSK_GetEnabledServices.
        [HttpGet("enabled-services")]
        public async Task<ActionResult<List<MenuServiceDto>>> GetEnabledServices()
        {
            using var con = new SqlConnection(_connectionString);

            var services = (await con.QueryAsync<MenuServiceDto>(
                "KSK_GetEnabledServices", commandType: CommandType.StoredProcedure)).ToList();

            _logger.LogInformation("Returned {Count} enabled menu services", services.Count);

            return Ok(services);
        }
    }

    public class MenuServiceDto
    {
        public string ServiceCode { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int SortOrder { get; set; }
    }
}