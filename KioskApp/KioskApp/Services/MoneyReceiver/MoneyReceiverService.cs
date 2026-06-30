using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services.MoneyReceiver
{
    public sealed class EscrowInfo
    {
        public double Value { get; init; }
        public string CurrencyCode { get; init; } = "UNKNOWN";
        public string Orientation { get; init; } = "";
    }

    public sealed class MoneyReceiverService : IDisposable
    {
        private SerialPort _port;
        private CancellationTokenSource _cts;
        private bool _isAccepting = false;
        private byte _ackBit = 0;
        private string _targetPort = "COM1";

        private bool _stackRequested = false;
        private bool _returnRequested = false;
        private bool _isFirstPoll = true;
        private bool _hardwareSynced = false;

        public string TargetCurrency { get; set; } = "UNKNOWN";
        public string MachinePrimaryCurrency { get; set; } = "UNKNOWN";

        public event Action<string> OnLog;
        public event Action<string> OnStatus;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<EscrowInfo> OnEscrow;
        public event Action<EscrowInfo> OnStacked;
        public event Action<EscrowInfo> OnReturned;
        public event Action<EscrowInfo> OnPupEscrow;
        public event Action<string> OnError;
        public event Action<string> OnRejected;

        public bool IsOpened { get; private set; }

        public void Open(string comPort)
        {
            _targetPort = comPort;
            _cts = new CancellationTokenSource();
            Task.Run(() => ConnectionLoop(_cts.Token));
        }

        private void ConnectionLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsOpened)
                {
                    try
                    {
                        _port = new SerialPort(_targetPort, 9600, Parity.Even, 7, StopBits.One);
                        _port.DtrEnable = true;
                        _port.RtsEnable = true;
                        _port.ReadTimeout = 400;
                        _port.WriteTimeout = 400;
                        _port.Open();

                        IsOpened = true;
                        _isFirstPoll = true;
                        _hardwareSynced = false;

                        OnLog?.Invoke($"[PORT OPENED] {_targetPort} is open. Waiting for MEI to reply...");
                        OnStatus?.Invoke("Connecting...");
                    }
                    catch
                    {
                        IsOpened = false;
                        Thread.Sleep(2000);
                        continue;
                    }
                }

                try
                {
                    if (_port != null && _port.IsOpen)
                    {
                        SendPollCommand();
                        try { ReadReply(); }
                        catch (TimeoutException)
                        {
                            if (_hardwareSynced)
                            {
                                OnLog?.Invoke("⚠️ Hardware stopped responding (Cable unplugged or jammed?).");
                                _hardwareSynced = false;
                                OnStatus?.Invoke("Hardware Offline");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"⚠️ Serial Port Crashed: {ex.Message}. Reconnecting...");
                    IsOpened = false;
                    _hardwareSynced = false;
                    OnDisconnected?.Invoke();
                    try { _port?.Close(); } catch { }
                }

                Thread.Sleep(200);
            }
        }

        public void EnableAcceptance(bool enable)
        {
            _isAccepting = enable;
            if (_hardwareSynced)
                OnLog?.Invoke(enable ? "Hardware: Acceptance ENABLED" : "Hardware: Acceptance DISABLED");
        }

        public void EscrowStack() => _stackRequested = true;
        public void EscrowReturn() => _returnRequested = true;
        public void ForceResetState() => Close();

        private void SendPollCommand()
        {
            byte ctl = (byte)(0x10 | _ackBit);

            // 0x7F enables the device to accept notes
            byte d0 = _isAccepting ? (byte)0x7F : (byte)0x00;

            // 0x1C explicitly FORCES Escrow Mode and 4-Way orientation
            byte d1 = 0x1C;
            if (_stackRequested) { d1 |= 0x20; _stackRequested = false; }
            else if (_returnRequested) { d1 |= 0x40; _returnRequested = false; }

            // 0x10 tells the hardware to use Extended Note Reporting (Reads the ROM!)
            byte d2 = 0x10;

            byte[] pkt = new byte[8];
            pkt[0] = 0x02; // STX
            pkt[1] = 0x08; // Length
            pkt[2] = ctl;  // Control
            pkt[3] = d0;   // Data 0
            pkt[4] = d1;   // Data 1
            pkt[5] = d2;   // Data 2
            pkt[6] = 0x03; // ETX

            // XOR Checksum strictly from Length through Data 2
            pkt[7] = (byte)(pkt[1] ^ pkt[2] ^ pkt[3] ^ pkt[4] ^ pkt[5]);

            _port.DiscardInBuffer();
            _port.Write(pkt, 0, pkt.Length);
        }

        private int _lastStateByte = 0;

        private void ReadReply()
        {
            int stx = _port.ReadByte();
            if (stx != 0x02) return;

            int len = _port.ReadByte();
            if (len < 5 || len > 127) return;

            byte[] pkt = new byte[len];
            pkt[0] = 0x02;
            pkt[1] = (byte)len;
            for (int i = 2; i < len; i++) pkt[i] = (byte)_port.ReadByte();

            // XOR Checksum Verification
            byte chk = 0;
            for (int i = 1; i <= len - 3; i++) chk ^= pkt[i];
            if (chk != pkt[len - 1]) return; // Silently drop corrupted packet

            byte ctrl = pkt[2];
            int msgType = (ctrl >> 4) & 0x07;

            _ackBit = (byte)((_ackBit == 0) ? 1 : 0);

            if (!_hardwareSynced)
            {
                _hardwareSynced = true;
                OnLog?.Invoke($"✅ NATIVE 64-BIT SYNC ESTABLISHED! Reading hardware ROM directly.");
                OnStatus?.Invoke("Hardware Synced");
                OnConnected?.Invoke();
            }

            // MsgType 2 = Standard Reply
            if (msgType == 2 && len >= 0x0B)
            {
                byte data0 = pkt[4];
                if (data0 != _lastStateByte || _isFirstPoll)
                {
                    _lastStateByte = data0;
                    if ((data0 & 0x04) != 0) OnEscrow?.Invoke(new EscrowInfo { Value = 0, CurrencyCode = "UNKNOWN" });
                    else if ((data0 & 0x10) != 0) OnStacked?.Invoke(new EscrowInfo { Value = 0, CurrencyCode = "UNKNOWN" });
                    else if ((data0 & 0x40) != 0) OnReturned?.Invoke(new EscrowInfo { Value = 0, CurrencyCode = "UNKNOWN" });
                }
            }
            else if (msgType == 6 && len >= 6)
            {
                byte rejectCode = pkt[3]; // Data Byte 0 contains the exact reason
                string reason = rejectCode switch
                {
                    0x01 => "Note inserted incorrectly or skewed.",
                    0x02 => "Magnetic check failed (Suspect fake or worn).",
                    0x03 => "Optical check failed (Suspect fake or dirty).",
                    0x04 => "Transport error (Note slipped inside machine).",
                    0x05 => "Unrecognized note.",
                    0x06 => "Note is too short or too long.",
                    0x07 => "Note type is currently disabled.",
                    0x0A => "Double note detected (Two bills stuck together).",
                    0x0B => "Note is folded or severely torn.",
                    _ => $"Invalid Note (Hardware Code: {rejectCode:X2})"
                };

                OnLog?.Invoke($"❌ [HARDWARE] Note Rejected: {reason}");
                OnRejected?.Invoke(reason);
                _isFirstPoll = false;
                return;
            }
            // MsgType 7 SubType 2 = Extended Note Reply (Contains ROM Currency String)
            else if (msgType == 7 && len >= 0x1E && pkt[3] == 0x02)
            {
                byte data0 = pkt[4];

                if (data0 != _lastStateByte || _isFirstPoll)
                {
                    _lastStateByte = data0;

                    if (pkt[11] == 0x00)
                    {
                        var unk = new EscrowInfo { Value = 0, CurrencyCode = "UNKNOWN" };
                        if ((data0 & 0x04) != 0) OnEscrow?.Invoke(unk);
                        else if ((data0 & 0x10) != 0) OnStacked?.Invoke(unk);
                        else if ((data0 & 0x40) != 0) OnReturned?.Invoke(unk);
                        return;
                    }

                    // Extract exact Currency String and Mathematical Exponent directly from hardware!
                    string iso = System.Text.Encoding.ASCII.GetString(pkt, 11, 3);
                    string baseValStr = System.Text.Encoding.ASCII.GetString(pkt, 14, 3);
                    char sign = (char)pkt[17];
                    string expStr = System.Text.Encoding.ASCII.GetString(pkt, 18, 2);

                    if (int.TryParse(baseValStr, out int baseVal) && int.TryParse(expStr, out int exp))
                    {
                        double multiplier = Math.Pow(10, sign == '+' ? exp : -exp);
                        double actualValue = baseVal * multiplier;

                        var info = new EscrowInfo { Value = actualValue, CurrencyCode = iso, Orientation = "Native" };

                        if ((data0 & 0x04) != 0)
                        {
                            if (_isFirstPoll) OnPupEscrow?.Invoke(info);
                            else
                            {
                                OnLog?.Invoke($"💵 [HARDWARE] Bill in Escrow! (ROM Detected: {actualValue} {iso})");
                                OnEscrow?.Invoke(info); // HALTS AND WAITS FOR UI
                            }
                        }
                        else if ((data0 & 0x10) != 0)
                        {
                            OnLog?.Invoke($"✅ [HARDWARE] Bill securely Stacked.");
                            OnStacked?.Invoke(info);
                        }
                        else if ((data0 & 0x40) != 0)
                        {
                            OnLog?.Invoke($"⚠️ [HARDWARE] Bill Returned to customer.");
                            OnReturned?.Invoke(info);
                        }
                    }
                }
            }

            _isFirstPoll = false;
        }

        public void Close()
        {
            IsOpened = false;
            _hardwareSynced = false;
            _cts?.Cancel();
            OnDisconnected?.Invoke();
            if (_port != null && _port.IsOpen) _port.Close();
        }

        public void Dispose() => Close();
    }
}