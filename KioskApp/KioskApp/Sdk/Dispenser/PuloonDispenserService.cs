using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Sdk.Dispenser
{
    public class PuloonDispenserService
    {
        private const byte SOH = 0x01;
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte EOT = 0x04;
        private const byte ACK = 0x06;
        private const byte ID = 0x30;

        private SerialPort _serialPort;
        private readonly SemaphoreSlim _portLock = new SemaphoreSlim(1, 1);

        // 🚀 NEW: Lets the UI quickly check if the port is currently open
        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;

        public bool Connect(string comPort)
        {
            try
            {
                Disconnect(); // Safety clear before opening a new one
                _serialPort = new SerialPort(comPort, 9600, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 5000;
                _serialPort.WriteTimeout = 2000;
                _serialPort.Open();
                return true;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                _serialPort.Dispose();
            }
        }

        // 🚀 NEW: Automatically finds the dispenser on any USB port without hardcoding COM numbers!
        public async Task<string> AutoDetectDispenserPortAsync()
        {
            string[] availablePorts = SerialPort.GetPortNames();

            foreach (string port in availablePorts)
            {
                if (Connect(port))
                {
                    var response = await StatusAsync();
                    if (response.Success) return port; // Found the Puloon machine!

                    Disconnect(); // It opened, but it wasn't the dispenser. Close and try the next.
                }
            }
            return null;
        }

        public int GetTotalAvailableMyr()
        {
            return 50000;
        }

        private byte CalculateBcc(byte[] data, int length)
        {
            byte bcc = 0;
            for (int i = 0; i < length; i++) bcc ^= data[i];
            return bcc;
        }

        private async Task<PuloonResponse> ExecuteCommandAsync(byte cmd, byte[] args, int timeoutMs = 60000)
        {
            await _portLock.WaitAsync();
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen) return new PuloonResponse(false, "Offline", 0xFF);

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                int argsLen = args != null ? args.Length : 0;
                int packetLen = 6 + argsLen;
                byte[] packet = new byte[packetLen];

                packet[0] = EOT;
                packet[1] = ID;
                packet[2] = STX;
                packet[3] = cmd;

                if (argsLen > 0)
                {
                    Array.Copy(args, 0, packet, 4, argsLen);
                }

                packet[4 + argsLen] = ETX;
                packet[5 + argsLen] = CalculateBcc(packet, packetLen - 1);

                _serialPort.Write(packet, 0, packet.Length);

                _serialPort.ReadTimeout = 5000;
                if (_serialPort.ReadByte() != ACK) return new PuloonResponse(false, "No ACK from dispenser", 0xFF);

                _serialPort.ReadTimeout = timeoutMs;
                int soh = _serialPort.ReadByte();
                if (soh != SOH) return new PuloonResponse(false, "Invalid response header", 0xFF);

                List<byte> responseData = new List<byte> { (byte)soh };
                bool etxFound = false;

                while (!etxFound)
                {
                    int b = _serialPort.ReadByte();
                    responseData.Add((byte)b);
                    if (b == ETX)
                    {
                        responseData.Add((byte)_serialPort.ReadByte());
                        etxFound = true;
                    }
                }

                _serialPort.Write(new[] { ACK }, 0, 1);
                _serialPort.Write(new[] { EOT }, 0, 1);

                byte[] resArray = responseData.ToArray();
                byte errorByte = resArray[4];
                int errorCode = errorByte - 0x20;

                string msg = ParseErrorCode(errorCode);
                bool isSuccess = (errorCode == 0x00 ||
                                  errorCode == 0x5C ||
                                  errorCode == 0x5D ||
                                  errorCode == 0x5E ||
                                  errorCode == 0x5F);

                return new PuloonResponse(errorCode == 0, msg, errorCode, resArray);
            }
            catch (TimeoutException) { return new PuloonResponse(false, "Hardware Timeout", 0xFF); }
            catch (Exception ex) { return new PuloonResponse(false, ex.Message, 0xFF); }
            finally { _portLock.Release(); }
        }

        public async Task<PuloonResponse> ResetAsync() => await ExecuteCommandAsync(0x44, null, 10000);
        public async Task<PuloonResponse> StatusAsync() => await ExecuteCommandAsync(0x50, null);
        public async Task<PuloonResponse> PurgeAsync() => await ExecuteCommandAsync(0x51, null, 60000);
        public async Task<PuloonResponse> RomVersionAsync() => await ExecuteCommandAsync(0x71, new byte[] { 0x30 });

        public async Task<CassetteHardwareStatus> GetCassetteHardwareStatusAsync()
        {
            var res = await StatusAsync();
            if (!res.Success || res.RawData == null || res.RawData.Length < 23)
                return new CassetteHardwareStatus { IsReadable = false };

            return new CassetteHardwareStatus
            {
                IsReadable = true,
                C1_Exists = (res.RawData[7] & 0x04) != 0,
                C1_NearEnd = (res.RawData[7] & 0x08) != 0,
                C2_Exists = (res.RawData[11] & 0x04) != 0,
                C2_NearEnd = (res.RawData[11] & 0x08) != 0,
                C3_Exists = (res.RawData[15] & 0x04) != 0,
                C3_NearEnd = (res.RawData[15] & 0x08) != 0,
                C4_Exists = (res.RawData[19] & 0x04) != 0,
                C4_NearEnd = (res.RawData[19] & 0x08) != 0
            };
        }

        public async Task<PuloonResponse> DispenseAsync(int c1, int c2, int c3, int c4)
        {
            if (c1 > 100 || c2 > 100 || c3 > 100 || c4 > 100) return new PuloonResponse(false, "Max 100 notes per cassette", 0xFF);

            byte[] args = new byte[] { (byte)(c1 + 0x20), (byte)(c2 + 0x20), (byte)(c3 + 0x20), (byte)(c4 + 0x20), 0x20, 0x20, 0x20 };
            return await ExecuteCommandAsync(0x52, args, 60000);
        }

        public async Task<PuloonResponse> TestDispenseAsync(int c1, int c2, int c3, int c4)
        {
            if (c1 > 50 || c2 > 50 || c3 > 50 || c4 > 50) return new PuloonResponse(false, "Max 50 notes for test dispense", 0xFF);

            byte[] args = new byte[] { (byte)(c1 + 0x20), (byte)(c2 + 0x20), (byte)(c3 + 0x20), (byte)(c4 + 0x20), 0x20, 0x20, 0x20 };
            return await ExecuteCommandAsync(0x53, args, 60000);
        }

        private string ParseErrorCode(int code)
        {
            switch (code)
            {
                case 0x00: return "Success";
                case 0x09: return "Jamming on EJT Sensor";
                case 0x0C: return "Too Many Pick-up Events (Notes Rejected)";
                case 0x12: return "Reject Tray Not Detected";
                case 0x14: return "More Banknotes Dispensed Than Requested";
                case 0x15: return "Dispensing Not Terminated in 90s";
                case 0x16: return "Abnormal Command (Protocol Error)";
                case 0x17: return "Abnormal Parameters on the Command";

                // 🚀 UPDATED: Perfectly mapped to your RM arrangement!
                case 0x50: return "Pick-up Error: Cassette 1 (RM 1) is almost empty";
                case 0x51: return "Pick-up Error: Cassette 2 (RM 10) is almost empty";
                case 0x52: return "Pick-up Error: Cassette 3 (RM 50) is almost empty";
                case 0x53: return "Pick-up Error: Cassette 4 (RM 100) is almost empty";

                case 0x54: return "Jamming or sensor failure in Cassette 1";
                case 0x58: return "Cassette 1 Not Detected";
                case 0x5C: return "Cassette 1 Near-End";

                case 0x60: return "Pick-up Error: Cassette 1 (RM 1) rollers slipped";
                case 0x61: return "Pick-up Error: Cassette 2 (RM 10) rollers slipped";
                case 0x62: return "Pick-up Error: Cassette 3 (RM 50) rollers slipped";
                case 0x63: return "Pick-up Error: Cassette 4 (RM 100) rollers slipped";

                case 0x68: return "Cannot Find Type 1 Cassette";
                case 0x6C: return "Cassette 1 (RM 1) is EMPTY";
                case 0x6D: return "Cassette 2 (RM 10) is EMPTY";
                case 0x6E: return "Cassette 3 (RM 50) is EMPTY";
                case 0x6F: return "Cassette 4 (RM 100) is EMPTY";
                case 0xFF: return "Communication Error";
                default: return $"Hardware Error Code: 0x{code:X2}";
            }
        }
    }

    public class PuloonResponse
    {
        public bool Success { get; }
        public string Message { get; }
        public int ErrorCode { get; }
        public byte[] RawData { get; }

        public PuloonResponse(bool success, string msg, int errorCode, byte[] rawData = null)
        {
            Success = success;
            Message = msg;
            ErrorCode = errorCode;
            RawData = rawData;
        }
    }

    public class CassetteHardwareStatus
    {
        public bool IsReadable { get; set; }
        public bool C1_Exists { get; set; }
        public bool C1_NearEnd { get; set; }
        public bool C2_Exists { get; set; }
        public bool C2_NearEnd { get; set; }
        public bool C3_Exists { get; set; }
        public bool C3_NearEnd { get; set; }
        public bool C4_Exists { get; set; }
        public bool C4_NearEnd { get; set; }
    }
}