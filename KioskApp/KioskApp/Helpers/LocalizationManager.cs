using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;
using OmniKiosk.Wpf.Resources;

namespace OmniKiosk.Wpf.Helpers
{
    // Singleton + indexer + INotifyPropertyChanged is the standard WPF pattern for
    // live-reactive resx localization: every {loc:Loc Key} binding points at
    // this[key], so firing PropertyChanged(null) after SetLanguage refreshes every
    // bound string on screen in one shot - no per-control manual reassignment,
    // and no separate UpdateLocalizedText() method to keep in sync by hand.
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        public static LocalizationManager Instance { get; } = new LocalizationManager();

        private readonly ResourceManager _resourceManager = new ResourceManager(typeof(Strings));

        public event PropertyChangedEventHandler? PropertyChanged;

        private LocalizationManager() { }

        public string this[string key] => _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        public static string GetString(string key) => Instance[key];

        public void SetLanguage(string cultureCode)
        {
            var culture = new CultureInfo(cultureCode);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // null property name = "everything changed" in WPF binding convention -
            // every {loc:Loc ...} indexer binding across every open screen re-reads.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
