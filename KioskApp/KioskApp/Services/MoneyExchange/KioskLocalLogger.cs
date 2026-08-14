using System.IO;

namespace OmniKiosk.Wpf.Services
{
    // Debug.WriteLine only shows up with a debugger attached, which will
    // never be the case on a production kiosk during a real transaction.
    // This writes to a plain local file instead, so a fire-and-forget
    // failure (auth, network, anything before the HTTP request even leaves
    // the machine) is actually checkable after the fact, not silently lost.
    public static class KioskLocalLogger
    {
        private static readonly string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ClientLogs");
        private static readonly object _lock = new();

        public static void LogError(string context, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogFolder);
                    var path = Path.Combine(LogFolder, $"kiosk-client-{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] [ERROR] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // A logging failure must never be the reason a customer's
                // transaction gets interrupted - if even this fails
                // (disk full, permissions), there's nothing further to do
                // but let the calling code continue as it already does.
            }
        }

        public static void LogInfo(string context, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogFolder);
                    var path = Path.Combine(LogFolder, $"kiosk-client-{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{context}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
