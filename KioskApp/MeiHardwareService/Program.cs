using MPOST;
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;

namespace MeiHardwareService
{
    class Program
    {
        private static Acceptor _acc = new Acceptor();
        private static NamedPipeServerStream _pipeServer;
        private static StreamWriter _writer;
        private static readonly object _writerLock = new object();

        // Native Windows Message Pump to keep the COM thread alive safely
        [StructLayout(LayoutKind.Sequential)]
        public struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeMessage
        {
            public IntPtr hWnd; public uint msg; public IntPtr wParam;
            public IntPtr lParam; public uint time; public NativePoint pt;
        }

        [DllImport("user32.dll")] public static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref NativeMessage lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref NativeMessage lpMsg);

        [STAThread]
        public static void Main(string[] args)
        {
            Console.WriteLine("MEI Microservice Started (x86).");

            Thread pipeThread = new Thread(StartPipeServer) { IsBackground = true };
            pipeThread.Start();

            // Safe Heartbeat that DOES NOT touch the hardware
            Thread heartbeatThread = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(3000);
                    SendMessage("EVT|LOG|[BACKEND] Heartbeat - Microservice is awake and listening.");
                }
            })
            { IsBackground = true };
            heartbeatThread.Start();

            // Hardware Events
            _acc.OnConnected += (s, e) => SendMessage("EVT|CONNECTED|Connected to MEI");
            _acc.OnDisconnected += (s, e) => SendMessage("EVT|DISCONNECTED|Disconnected from MEI");

            _acc.OnPowerUpComplete += (s, e) =>
            {
                SendMessage("EVT|LOG|[BACKEND] Power Up Complete. Enabling...");
                try { _acc.AutoStack = false; } catch { }
                try { _acc.EnableAcceptance = true; } catch { }
            };

            _acc.OnEscrow += (s, e) =>
            {
                SendMessage("EVT|LOG|[BACKEND] *** NATIVE ESCROW EVENT FIRED! ***");
                SendEscrowEvent("ESCROW");
            };

            _acc.OnStacked += (s, e) =>
            {
                SendMessage("EVT|LOG|[BACKEND] *** NATIVE STACKED EVENT FIRED! ***");
                SendEscrowEvent("STACKED");
            };

            _acc.OnReturned += (s, e) =>
            {
                SendMessage("EVT|LOG|[BACKEND] *** NATIVE RETURNED EVENT FIRED! ***");
                SendEscrowEvent("RETURNED");
            };

            // Keep the main thread alive and process COM events
            NativeMessage msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static void StartPipeServer()
        {
            while (true)
            {
                try
                {
                    using (_pipeServer = new NamedPipeServerStream("MeiKioskPipe", PipeDirection.InOut))
                    {
                        _pipeServer.WaitForConnection();
                        SendMessage("EVT|LOG|[BACKEND] IPC Pipe Connected!");

                        using (var reader = new StreamReader(_pipeServer))
                        {
                            _writer = new StreamWriter(_pipeServer) { AutoFlush = true };
                            string line;
                            while ((line = reader.ReadLine()) != null) ProcessCommand(line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Pipe Error: " + ex.Message);
                    try { _acc.EnableAcceptance = false; _acc.Close(); } catch { }
                }
            }
        }

        private static void ProcessCommand(string command)
        {
            SendMessage($"EVT|LOG|[BACKEND] Received Command: {command}");
            try
            {
                var parts = command.Split('|');
                switch (parts[0])
                {
                    case "OPEN": _acc.Open(parts[1].Trim()); break;
                    case "CLOSE": _acc.EnableAcceptance = false; _acc.Close(); break;
                    case "ENABLE":
                        try { _acc.AutoStack = false; } catch { }
                        _acc.EnableAcceptance = true;
                        SendMessage("EVT|LOG|[BACKEND] Acceptance Enabled.");
                        break;
                    case "DISABLE": _acc.EnableAcceptance = false; break;
                    case "STACK": _acc.EscrowStack(); break;
                    case "RETURN": _acc.EscrowReturn(); break;
                }
            }
            catch (Exception ex) { SendMessage($"EVT|ERROR|Command Failed: {ex.Message}"); }
        }

        private static void SendEscrowEvent(string eventName)
        {
            SendMessage($"EVT|LOG|[BACKEND] Reading bill data for {eventName}...");

            try
            {
                double val = 0;
                string cur = "UNKNOWN";

                if (_acc.DocType == DocumentType.Bill && _acc.Bill != null)
                {
                    val = _acc.Bill.Value;
                    cur = string.IsNullOrWhiteSpace(_acc.Bill.Country) ? "UNKNOWN" : _acc.Bill.Country;
                }
                else
                {
                    SendMessage("EVT|LOG|[BACKEND-WARN] Note not recognized as a valid bill yet.");
                }

                SendMessage($"EVT|{eventName}|{val}|{cur}");
            }
            catch (Exception ex) { SendMessage($"EVT|ERROR|SendEscrowEvent Error: {ex.Message}"); }
        }

        private static void SendMessage(string msg)
        {
            try
            {
                lock (_writerLock)
                {
                    if (_writer != null && _pipeServer != null && _pipeServer.IsConnected)
                    {
                        _writer.WriteLine(msg);
                    }
                }
            }
            catch { }
        }
    }
}