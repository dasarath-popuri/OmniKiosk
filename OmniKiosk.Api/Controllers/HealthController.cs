using Microsoft.AspNetCore.Mvc;

namespace OmniKiosk.Config.Api.Controllers
{
    // This tells the API that the URL for this controller is "api/health"
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        // This listens for standard HTTP GET requests
        [HttpGet]
        public IActionResult CheckHealth()
        {
            // This returns an HTTP 200 OK with a friendly message
            return Ok(new { status = "Online", message = "OmniKiosk API is running perfectly!" });
        }
    }
}