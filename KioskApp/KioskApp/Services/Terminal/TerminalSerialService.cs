using System;
using System.IO.Ports;
using System.Threading;

namespace OmniKiosk.Wpf.Services.Terminal
{
    /// <summary>
    /// Simple serial port service that raises DataReceived(byte[]) events.
    /// Use Open/Close/Send. Thread-safe send with a lock.
    /// </summary>
    public class TerminalSerialService : IDisposable
    {
        private SerialPort? _port;
        private readonly object _sendLock = new object();

        /// <summary>Raised when raw bytes are received from serial port.</summary>
        public event Action<byte[]>? DataReceived;

        public bool IsOpen => _port != null && _port.IsOpen;

        public void Open(string portName, int baudRate = 9600, Parity parity = Parity.None,
                         int dataBits = 8, StopBits stopBits = StopBits.One,
                         bool enableRts = true, bool enableDtr = true)
        {
            Close();

            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 3000,
                WriteTimeout = 5000,
                Handshake = Handshake.None,
                RtsEnable = enableRts,
                DtrEnable = enableDtr,
                NewLine = "\n"
            };

            _port.DataReceived += Port_DataReceived;
            _port.Open();
        }

        public void Close()
        {
            try
            {
                if (_port != null)
                {
                    _port.DataReceived -= Port_DataReceived;
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                }
            }
            catch { /* ignore */ }
            finally
            {
                _port = null;
            }
        }

        public void Send(byte[] data)
        {
            if (_port == null || !_port.IsOpen) throw new InvalidOperationException("Port not open");

            lock (_sendLock)
            {
                _port.Write(data, 0, data.Length);

                // small pause for device processing (some terminals need slight delay)
                Thread.Sleep(10);
            }
        }

        private void Port_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = (SerialPort?)sender;
                if (sp == null) return;

                int available = sp.BytesToRead;
                if (available <= 0) return;

                var buffer = new byte[available];
                int read = sp.Read(buffer, 0, available);
                if (read > 0)
                {
                    DataReceived?.Invoke(buffer);
                }
            }
            catch
            {
                // swallow any read exceptions in the test harness
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
