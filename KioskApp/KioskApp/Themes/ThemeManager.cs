using Serilog;
using System;
using System.Linq;
using System.Windows;

namespace OmniKiosk.Wpf.Themes
{
    public static class ThemeManager
    {
        public static void ApplyTheme(string themeName)
        {
            try
            {
                // 1. Locate the exact path to the requested theme file
                var uri = new Uri($"pack://application:,,,/OmniKiosk.Wpf;component/Themes/{themeName}Theme.xaml");
                var newTheme = new ResourceDictionary { Source = uri };

                // 2. Find any existing theme dictionaries currently loaded and remove them
                var existingTheme = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));

                if (existingTheme != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(existingTheme);
                }

                // 3. Inject the new theme into the global application resources
                Application.Current.Resources.MergedDictionaries.Add(newTheme);

                Log.Information("🎨 Successfully applied UI Theme: {ThemeName}", themeName);
            }
            catch (Exception ex)
            {
                Log.Error("❌ Failed to apply theme '{ThemeName}'. Error: {Error}", themeName, ex.Message);
            }
        }
    }
}