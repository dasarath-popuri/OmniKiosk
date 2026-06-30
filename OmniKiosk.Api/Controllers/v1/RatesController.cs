using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OmniKiosk.Config.Api.Models.v1;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmniKiosk.Config.Api.Controllers.v1
{
    [Authorize] // 🔒 Protected by our JWT!
    [Route("api/v1/[controller]")]
    [ApiController]
    public class RatesController : ControllerBase
    {
        private readonly IMemoryCache _cache;
        private const string RATES_CACHE_KEY = "LiveExchangeRates";

        // Inject the cache engine into the controller
        public RatesController(IMemoryCache cache)
        {
            _cache = cache;
        }

        [HttpGet]
        public async Task<IActionResult> GetLiveRates()
        {
            // 1. ATTEMPT TO GET FROM CACHE
            // If the rates are in RAM, return them instantly!
            if (_cache.TryGetValue(RATES_CACHE_KEY, out List<ExchangeRate> cachedRates))
            {
                Console.WriteLine("⚡ SERVED FROM CACHE: Did not hit the Database.");
                return Ok(cachedRates);
            }

            // 2. IF CACHE IS EMPTY, DO THE HEAVY LIFTING
            Console.WriteLine("🐢 CACHE MISS: Querying the Database...");

            // (Imagine this is where you run your slow SQL Database query)
            // await _dbContext.Rates.ToListAsync();
            await Task.Delay(1000); // Simulating a slow database call

            var freshRates = new List<ExchangeRate>
            {
                new ExchangeRate { CurrencyCode = "USD", RateToMyr = 4.75, LastUpdated = DateTime.Now },
                new ExchangeRate { CurrencyCode = "SGD", RateToMyr = 3.52, LastUpdated = DateTime.Now },
                new ExchangeRate { CurrencyCode = "EUR", RateToMyr = 5.10, LastUpdated = DateTime.Now }
            };

            // 3. SAVE THE FRESH DATA TO THE CACHE
            var cacheOptions = new MemoryCacheEntryOptions()
                // Set the data to expire exactly 5 minutes from now
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            _cache.Set(RATES_CACHE_KEY, freshRates, cacheOptions);

            return Ok(freshRates);
        }
    }
}