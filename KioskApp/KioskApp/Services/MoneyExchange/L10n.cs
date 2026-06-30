using System.Globalization;
using System.Threading;
using OmniKiosk.Wpf.Resources;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    public static class L10n
    {
        public static string T(string key, string fallback)
        {
            return Strings.ResourceManager.GetString(key, Thread.CurrentThread.CurrentUICulture) ?? fallback;
        }
    }
}
