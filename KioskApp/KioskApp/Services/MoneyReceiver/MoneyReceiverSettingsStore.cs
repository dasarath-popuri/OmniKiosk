using System;
using System.IO;
using System.Text.Json;

namespace OmniKiosk.Wpf.Services.MoneyReceiver
{
    public sealed class MoneyReceiverSettings
    {
        public string PortName { get; set; } = "COM3";
        public bool AutoOpenAndEnable { get; set; } = true;
        public bool AutoStackOnEscrow { get; set; } = false; // if true, no confirm buttons
    }

    public sealed class MoneyReceiverSettingsStore
    {
        private readonly string _path;

        public MoneyReceiverSettingsStore(string path) => _path = path;

        public MoneyReceiverSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new MoneyReceiverSettings();
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<MoneyReceiverSettings>(json) ?? new MoneyReceiverSettings();
            }
            catch
            {
                return new MoneyReceiverSettings();
            }
        }

        public void Save(MoneyReceiverSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}