using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;

namespace OmniKiosk.Wpf.Logging
{
    // 1. The Manager: Keeps track of the current customer's session
    public static class JourneyManager
    {
        public static string CurrentJourneyId { get; private set; } = "SYSTEM_BOOT";

        public static void StartNewJourney()
        {
            // Generates a short, readable 8-character ID (e.g., "A8F9B2C1")
            CurrentJourneyId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            Log.Information("=================================================");
            Log.Information("🚀 NEW CUSTOMER JOURNEY STARTED");
            Log.Information("=================================================");
        }

        public static void ClearJourney()
        {
            Log.Information("⏹ JOURNEY ENDED / TIMED OUT");
            CurrentJourneyId = "IDLE";
        }
    }

    // 2. The Enricher: Serilog uses this to automatically stamp every log
    public class JourneyIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var property = propertyFactory.CreateProperty("JourneyId", JourneyManager.CurrentJourneyId);
            logEvent.AddPropertyIfAbsent(property);
        }
    }
}