using OmniKiosk.Wpf.Config;
using OmniKiosk.Wpf.Sdk.Face;
using OmniKiosk.Wpf.Sdk.IC;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Services.MoneyReceiver;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Sdk.Dispenser;
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services
{
    public static class GlobalHardwareManager
    {
        public static MoneyReceiverService MoneyReceiver { get; private set; }
        public static PassportReaderService PassportScanner { get; private set; }
        public static IcReaderService IcReader { get; private set; }
        public static FaceEngineManager FaceEngine { get; private set; }
        public static BixolonPrinterService Printer { get; private set; }

        // 🚀 Real Dispenser Service
        public static PuloonDispenserService MoneyDispenser { get; private set; }

        public static bool IsInitialized { get; private set; }

        public static async Task InitializeAllAsync()
        {
            if (IsInitialized) return;

            IcReader = new IcReaderService();
            FaceEngine = new FaceEngineManager();
            Printer = new BixolonPrinterService();
            Printer.PrinterName = "BIXOLON BK3-3";

            // Initialize Dispenser
            MoneyDispenser = new PuloonDispenserService();

            // 🚀 NEW: Auto-detect and connect to the dispenser during app bootup!
            try
            {
                await MoneyDispenser.AutoDetectDispenserPortAsync();
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var libPath = Path.GetFullPath(Path.Combine(baseDir, KioskSettings.PassportLibFolder));
                PassportScanner = new PassportReaderService(KioskSettings.PassportReaderUserId, libPath);
                PassportScanner.Init();
            }
            catch { }

            MoneyReceiver = new MoneyReceiverService();
            await Task.Delay(2500);

            try
            {
                // Note: If you ever write an Auto-Detect for the Acceptor too, you would put it here!
                //var port = SerialPort.GetPortNames().FirstOrDefault(p => p.Contains("COM")) ?? "COM1";
                var port = "COM2";
                MoneyReceiver.Open(port);
            }
            catch { }

            IsInitialized = true;
        }

        public static void ShutdownAll()
        {
            try { MoneyReceiver?.Dispose(); } catch { }
            try { PassportScanner?.Dispose(); } catch { }
            try { FaceEngine?.Dispose(); } catch { }
            try { MoneyDispenser?.Disconnect(); } catch { }
        }
    }
}