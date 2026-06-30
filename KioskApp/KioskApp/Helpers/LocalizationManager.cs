//using System;
//using System.ComponentModel;
//using System.Globalization;
//using System.Resources;
//using System.Threading;


//namespace OmniKiosk.Wpf.Helpers
//{
//    public class LocalizationManager : INotifyPropertyChanged
//    {
//        private static readonly LocalizationManager _instance = new LocalizationManager();
//        public static LocalizationManager Instance => _instance;


//        public event PropertyChangedEventHandler? PropertyChanged;


//        public ResourceManager ResourceManager { get; private set; }


//        private LocalizationManager()
//        {
//            ResourceManager = new ResourceManager("OmniKiosk.Wpf.Resources.Strings", typeof(LocalizationManager).Assembly);
//        }


//        public string this[string key]
//        {
//            get
//            {
//                try
//                {
//                    var value = ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
//                    return value ?? key;
//                }
//                catch
//                {
//                    return key;
//                }
//            }
//        }


//        public void SetCulture(string cultureName)
//        {
//            var ci = new CultureInfo(cultureName);
//            Thread.CurrentThread.CurrentUICulture = ci;
//            Thread.CurrentThread.CurrentCulture = ci;
//            // notify XAML bindings
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
//        }
//    }
//}

using System.Globalization;
using System.Resources;
using OmniKiosk.Wpf.Resources;

namespace OmniKiosk.Wpf.Helpers
{
    public static class LocalizationManager
    {
        private static ResourceManager _resourceManager = new ResourceManager(typeof(Strings));

        public static string GetString(string key)
        {
            return _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }

        public static void SetLanguage(string cultureCode)
        {
            CultureInfo culture = new CultureInfo(cultureCode);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
    }
}
