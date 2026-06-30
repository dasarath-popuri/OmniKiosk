using System;
using System.Linq;
using System.Text;
using System.Windows;
using OmniKiosk.Wpf.Services.Terminal;
using WebSocketSharp;

namespace OmniKiosk.Wpf.Views
{
    public partial class PaymentTerminalTestView : Window
    {
        private const byte ETX = 0x03;
        private readonly TerminalSerialService _terminal;
        private readonly ByteBuffer _recvBuffer = new ByteBuffer();

        public PaymentTerminalTestView()
        {
            InitializeComponent();
            _terminal = new TerminalSerialService();
            _terminal.DataReceived += Terminal_DataReceived;

            LoadPorts();
            Log("Ready.");
        }

        private void LoadPorts()
        {
            try
            {
                cmbPorts.ItemsSource = System.IO.Ports.SerialPort.GetPortNames();
                if (cmbPorts.Items.Count > 0) cmbPorts.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log("Error loading ports: " + ex.Message);
            }
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            LoadPorts();
            Log("🔄 Ports refreshed.");
        }

        private void OpenPort_Click(object sender, RoutedEventArgs e)
        {
            if (cmbPorts.SelectedItem == null) { MessageBox.Show("Select COM port."); return; }

            var port = cmbPorts.SelectedItem.ToString();
            if (!int.TryParse(((System.Windows.Controls.ComboBoxItem)cmbBaud.SelectedItem).Content.ToString(), out int baud))
                baud = 9600;

            try
            {
                _terminal.Open(port!, baud, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One, enableRts: true, enableDtr: true);
                btnOpen.IsEnabled = false;
                btnClose.IsEnabled = true;
                txtStatus.Text = $"Open {port}@{baud}";
                Log($"Opened {port} @ {baud}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open port: {ex.Message}");
                Log("Open error: " + ex.Message);
            }
        }

        private void ClosePort_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _terminal.Close();
                btnOpen.IsEnabled = true;
                btnClose.IsEnabled = false;
                txtStatus.Text = "Closed";
                Log("Port closed.");
            }
            catch (Exception ex) { Log("Close error: " + ex.Message); }
        }

        // ---------- Buttons wired to builder ----------
        private void BtnSendPing_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildPing(), "Ping");

        private void BtnBalanceInquiry_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildBalanceInquiry("1234567890", true), "Balance Inquiry");

        private void BtnSale_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildSale("000000001000", "INV001"), "Sale RM10.00");

        private void BtnSettlement_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildSettlement(), "Settlement");

        private void BtnPrintReceipt_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildPrintReceipt("THANK YOU FOR VISITING"), "Print Receipt");

        private void BtnDisplayScreen_Click(object sender, RoutedEventArgs e) =>
            SendFrame(EcrMessageBuilder.BuildDisplayScreen("WELCOME USER", 30), "Display Screen");

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => txtLog.Clear();

        // ---------- Send helper ----------
        private void SendFrame(byte[] frame, string title)
        {
            if (!_terminal.IsOpen)
            {
                Log("❌ Open COM port first.");
                return;
            }

            try
            {
                _terminal.Send(frame);
                LogHex($"➡ Sent {title} ({frame.Length} bytes)", frame);
            }
            catch (Exception ex)
            {
                Log($"❌ Send error: {ex.Message}");
            }
        }

        // ---------- Receive ----------
        private void Terminal_DataReceived(byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                LogHex("⬅ RECV RAW", data);
                _recvBuffer.Add(data);
                TryParseFrames();
            });
        }

        private void TryParseFrames()
        {
            while (true)
            {
                var buffer = _recvBuffer.ToArray();
                if (buffer == null || buffer.Length < 5) return;

                int stx = Array.IndexOf(buffer, (byte)0x02);
                if (stx < 0) { _recvBuffer.Clear(); return; }
                if (stx > 0) { _recvBuffer.DropFront(stx); buffer = _recvBuffer.ToArray(); }

                if (buffer.Length < 3) return;

                int msgLen = TerminalHelper.BcdToInt(buffer[1], buffer[2]);
                int total = 1 + 2 + msgLen + 1 + 1;
                if (buffer.Length < total) return;

                var frame = new byte[total];
                Array.Copy(buffer, 0, frame, 0, total);

                int etxPos = 1 + 2 + msgLen;
                if (frame[etxPos] != ETX)
                {
                    _recvBuffer.DropFront(1);
                    continue;
                }

                var lrcRegion = new byte[2 + msgLen + 1];
                Array.Copy(frame, 1, lrcRegion, 0, lrcRegion.Length);
                var computed = TerminalHelper.ComputeLrc(lrcRegion);
                var received = frame[total - 1];

                Log($"Parsed frame: msgLen={msgLen}, computedLRC=0x{computed:X2}, recvLRC=0x{received:X2}");

                if (computed == received)
                {
                    // send ACK
                    _terminal.Send(new byte[] { 0x06 });
                    Log("LRC OK -> Sent ACK (0x06).");

                    // extract message data and display ASCII-ish
                    var msgData = new byte[msgLen];
                    Array.Copy(frame, 3, msgData, 0, msgLen);
                    Log($"MessageData ASCII: {TerminalHelper.BytesToAscii(msgData)}");
                }
                else
                {
                    // send NACK
                    _terminal.Send(new byte[] { 0x15 });
                    Log("LRC MISMATCH -> Sent NACK (0x15).");
                }

                _recvBuffer.DropFront(total);
            }
        }

        // ---------- Utilities ----------
        private void Log(string s)
        {
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {s}\n");
            txtLog.ScrollToEnd();
        }

        private void LogHex(string tag, byte[] data)
        {
            Log($"{tag} ({data.Length} bytes): {TerminalHelper.BytesToHex(data)}");
        }

        // small buffer
        private class ByteBuffer
        {
            private readonly System.Collections.Generic.List<byte> buf = new();
            public void Add(byte[] chunk) => buf.AddRange(chunk);
            public byte[] ToArray() => buf.ToArray();
            public void DropFront(int n) { if (n <= 0) return; if (n >= buf.Count) buf.Clear(); else buf.RemoveRange(0, n); }
            public void Clear() => buf.Clear();
        }
    }
}
