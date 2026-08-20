using OmniKiosk.Wpf.Config;
using OmniKiosk.Wpf.Sdk.Face;
using OmniKiosk.Wpf.Sdk.IC;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Services.MoneyReceiver;
using OmniKiosk.Wpf.Sdk.Printer;
using OmniKiosk.Wpf.Sdk.Dispenser;
using System;
using System.IO;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services
{
    public static class GlobalHardwareManager
    {
        public static MoneyReceiverService MoneyReceiver
        {
            get;
            private set;
        }

        public static PassportReaderService PassportScanner
        {
            get;
            private set;
        }

        public static IcReaderService IcReader
        {
            get;
            private set;
        }

        public static FaceEngineManager FaceEngine
        {
            get;
            private set;
        }

        public static BixolonPrinterService Printer
        {
            get;
            private set;
        }

        public static PuloonDispenserService MoneyDispenser
        {
            get;
            private set;
        }

        public static bool IsInitialized
        {
            get;
            private set;
        }

        public static int EyecoolInitCode
        {
            get;
            private set;
        } = int.MinValue;

        public static bool EyecoolReady
        {
            get;
            private set;
        }

        // ============================================================
        // INITIALIZATION
        // ============================================================

        public static async Task InitializeAllAsync()
        {
            if (IsInitialized)
                return;

            // ========================================================
            // IC READER
            // ========================================================

            IcReader =
                new IcReaderService();

            // ========================================================
            // FACE ENGINE
            // ========================================================

            FaceEngine =
                new FaceEngineManager();

            // ========================================================
            // PRINTER
            // ========================================================

            Printer =
                new BixolonPrinterService();

            Printer.PrinterName =
                "BIXOLON BK3-3";

            // ========================================================
            // DISPENSER
            // ========================================================

            MoneyDispenser =
                new PuloonDispenserService();

            try
            {
                await MoneyDispenser
                    .AutoDetectDispenserPortAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Dispenser] " +
                    ex.Message);
            }

            // ========================================================
            // PASSPORT
            // ========================================================

            try
            {
                string baseDir =
                    AppDomain.CurrentDomain.BaseDirectory;

                string libPath =
                    Path.GetFullPath(
                        Path.Combine(
                            baseDir,
                            KioskSettings.PassportLibFolder));

                PassportScanner =
                    new PassportReaderService(
                        KioskSettings.PassportReaderUserId,
                        libPath);

                PassportScanner.Init();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Passport] " +
                    ex.Message);
            }

            // ========================================================
            // EYECOOL
            //
            // Only initialize the SDK globally.
            //
            // We do NOT:
            //   ECF_Open
            //   ECF_StartDetectAsyn
            //
            // Those belong to FaceVerificationStep.
            // ========================================================

            try
            {
                EyecoolInitCode =
                    EcFaceCamSdkHelper
                        .EnsureInitialized();

                EyecoolReady =
                    EyecoolInitCode == 0;

                Console.WriteLine(
                    $"[Eyecool] ECF_Init = {EyecoolInitCode}");
            }
            catch (Exception ex)
            {
                EyecoolReady = false;

                Console.WriteLine(
                    "[Eyecool] initialization exception:");
                Console.WriteLine(ex);
            }

            // ========================================================
            // MONEY RECEIVER
            // ========================================================

            MoneyReceiver =
                new MoneyReceiverService();

            await Task.Delay(2500);

            try
            {
                const string port = "COM2";

                MoneyReceiver.Open(port);

                Console.WriteLine(
                    $"[MoneyReceiver] opened {port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[MoneyReceiver] " +
                    ex.Message);
            }

            IsInitialized = true;
        }

        // ============================================================
        // SHUTDOWN
        // ============================================================

        public static void ShutdownAll()
        {
            try
            {
                MoneyReceiver?.Dispose();
            }
            catch
            {
            }

            try
            {
                PassportScanner?.Dispose();
            }
            catch
            {
            }

            try
            {
                FaceEngine?.Dispose();
            }
            catch
            {
            }

            try
            {
                MoneyDispenser?.Disconnect();
            }
            catch
            {
            }

            // ========================================================
            // EYECOOL
            //
            // ECF_Exit ONLY at kiosk application shutdown.
            // ========================================================

            try
            {
                EcFaceCamSdkHelper
                    .ShutdownSdk();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] shutdown exception:");
                Console.WriteLine(ex);
            }

            EyecoolReady = false;
            IsInitialized = false;
        }
    }
}