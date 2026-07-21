using System;
using System.Windows;

namespace OmniKiosk.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🚀 The old 32-bit MeiHardwareService launcher has been completely removed!
            // We are now running 100% native 64-bit serial communication.
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // (If you have any other global shutdown logic, it goes here)
            base.OnExit(e);
        }
    }
}

//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Windows;

//namespace OmniKiosk.Wpf
//{
//    public partial class App : Application
//    {
//        private Process? _meiServiceProcess;

//        protected override void OnStartup(StartupEventArgs e)
//        {
//            base.OnStartup(e);

//            try
//            {
//                // --- 1. GHOST BUSTER ---
//                // Find and kill any old hidden microservices left over from the last debug session
//                var existingProcesses = Process.GetProcessesByName("MeiHardwareService");
//                foreach (var p in existingProcesses)
//                {
//                    try { p.Kill(); p.WaitForExit(); } catch { }
//                }

//                // --- 2. FIND THE MICROSERVICE ---
//                string servicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HardwareServices", "MeiHardwareService.exe");

//                if (File.Exists(servicePath))
//                {
//                    _meiServiceProcess = new Process
//                    {
//                        StartInfo = new ProcessStartInfo
//                        {
//                            FileName = servicePath,

//                            // ⚠️ THE CRITICAL FIX: Tells the service to look for MPOST.dll in its own folder!
//                            WorkingDirectory = Path.GetDirectoryName(servicePath),

//                            UseShellExecute = true,
//                            //CreateNoWindow = true, // Keeps it completely invisible to the user
//                            WindowStyle = ProcessWindowStyle.Hidden
//                        }
//                    };
//                    _meiServiceProcess.Start();

//                }
//                else
//                {
//                    // If the file is missing, loudly warn the developer instead of failing silently!
//                    MessageBox.Show(
//                        $"Could not find the 32-bit Hardware Microservice!\n\nPlease ensure it is located here:\n{servicePath}",
//                        "Hardware Service Missing",
//                        MessageBoxButton.OK,
//                        MessageBoxImage.Error);
//                }
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show("Failed to launch MEI Hardware Service: " + ex.Message);
//            }
//        }

//        protected override void OnExit(ExitEventArgs e)
//        {
//            // 3. Clean up the microservice when the kiosk app closes
//            if (_meiServiceProcess != null && !_meiServiceProcess.HasExited)
//            {
//                try { _meiServiceProcess.Kill(); } catch { }
//            }
//            base.OnExit(e);
//        }
//    }
//}