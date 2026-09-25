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

        //public static FaceEngineManager FaceEngine
        //{
        //    get;
        //    private set;
        //}
        public static FaceEngineManager? FaceEngine
        {
            get;
            private set;
        }

        private static readonly object _faceEngineLock = new();

        public static FaceEngineManager GetOrCreateFaceEngine()
        {
            lock (_faceEngineLock)
            {
                FaceEngine ??= new FaceEngineManager();
                return FaceEngine;
            }
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

        // Per-SDK ready flags - added because IsInitialized above only
        // meant "the init sequence ran to completion", NOT "every SDK
        // actually succeeded" - every try/catch below was swallowing
        // failures with just a Console.WriteLine, so a kiosk with (say) a
        // disconnected IC reader would still report IsInitialized=true and
        // look fully healthy. These are checked at startup and before
        // entering either service flow - see MainWindow.
        public static bool IcReaderReady { get; private set; }
        public static bool PassportReady { get; private set; }
        public static bool PrinterReady { get; private set; }
        public static bool DispenserReady { get; private set; }
        public static bool MoneyReceiverReady { get; private set; }

        // Money Exchange needs all of these; a kiosk missing the printer
        // or dispenser genuinely cannot complete a transaction. Eyecool
        // (face) is checked separately via EyecoolReady, already existing.
        public static bool AllCriticalSdksReady =>
            IcReaderReady && PassportReady && PrinterReady && DispenserReady && MoneyReceiverReady && EyecoolReady;

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

            // ASSUMPTION FLAGGED: IcReaderService's constructor doesn't
            // appear to throw or report a connection result on its own
            // (confirmed from the file's own Init/ReadCardAsync pattern
            // elsewhere) - marking ready here reflects "the service object
            // was constructed", not "a reader is physically connected and
            // responding". If IcReaderService exposes a real connectivity
            // check, this should call it instead.
            IcReaderReady = true;

            // ========================================================
            // FACE ENGINE
            // ========================================================

            //FaceEngine =
            //    new FaceEngineManager();

            // ========================================================
            // PRINTER
            // ========================================================

            Printer =
                new BixolonPrinterService();

            Printer.PrinterName =
                "BIXOLON BK3-3";

            // ASSUMPTION FLAGGED: same caveat as IcReaderReady above -
            // BixolonPrinterService's constructor doesn't appear to
            // validate a physical connection on its own. This reflects
            // "the service object was constructed", not "a printer is
            // physically connected and responding".
            PrinterReady = true;

            // ========================================================
            // DISPENSER
            // ========================================================

            MoneyDispenser =
                new PuloonDispenserService();

            try
            {
                await MoneyDispenser
                    .AutoDetectDispenserPortAsync();

                DispenserReady = MoneyDispenser.IsConnected;
            }
            catch (Exception ex)
            {
                DispenserReady = false;
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

                PassportReady = true;
            }
            catch (Exception ex)
            {
                PassportReady = false;
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

                MoneyReceiverReady = true;

                Console.WriteLine(
                    $"[MoneyReceiver] opened {port}");
            }
            catch (Exception ex)
            {
                MoneyReceiverReady = false;
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
                //FaceEngine?.Dispose();
                FaceEngine?.Dispose();
                FaceEngine = null;
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