using System;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OmniKiosk.Wpf.Services
{
    public class PaymentTerminalService
    {
        private SerialPort _serialPort;
        private const int TIMEOUT_MS = 120000; // 2 minutes for terminal response
        private const int ACK_TIMEOUT_MS = 2000; // 2 seconds for ACK

        // Control Characters
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte ACK = 0x06;
        private const byte NACK = 0x15;

        public PaymentTerminalService(string portName = "COM5")
        {
            InitializeSerialPort(portName);
        }

        private void InitializeSerialPort(string portName)
        {
            _serialPort = new SerialPort
            {
                PortName = portName,
                BaudRate = 9600,
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = ACK_TIMEOUT_MS,
                WriteTimeout = 1000
            };
        }

        public bool OpenConnection()
        {
            try
            {
                string[] allPorts = SerialPort.GetPortNames();
                Debug.WriteLine($"[DEBUG] All available ports: {string.Join(", ", allPorts)}");
                Debug.WriteLine($"[DEBUG] Trying to open: {_serialPort.PortName}");
                if (!_serialPort.IsOpen)
                {
                    _serialPort.Open();
                    Debug.WriteLine($"Serial port {_serialPort.PortName} opened successfully");
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening serial port: {ex.Message}");
                return false;
            }
        }

        public void CloseConnection()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    Debug.WriteLine("Serial port closed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing serial port: {ex.Message}");
            }
        }

        // Calculate LRC (Longitudinal Redundancy Check)
        private byte CalculateLRC(byte[] data, int startIndex, int length)
        {
            byte lrc = 0x00;
            for (int i = startIndex; i < startIndex + length; i++)
            {
                lrc ^= data[i];
            }
            return lrc;
        }

        // Build complete message with STX, Length, Data, ETX, LRC
        // Build complete message with STX, Length, Data, ETX, LRC
        private byte[] BuildMessage(string messageData)
        {
            byte[] dataBytes = Encoding.ASCII.GetBytes(messageData);
            int messageLength = dataBytes.Length;

            Debug.WriteLine($"Building message - Data length: {messageLength} bytes");
            Debug.WriteLine($"Message data: {messageData}");

            // Convert length to 4-character ASCII hex (not BCD!)
            // For example: 22 bytes = "0016" in hex
            string lengthHex = messageLength.ToString("X4");
            byte[] lengthBytes = Encoding.ASCII.GetBytes(lengthHex);

            Debug.WriteLine($"Length in hex: {lengthHex}");

            // Calculate total message size
            int totalSize = 1 + lengthBytes.Length + dataBytes.Length + 1 + 1; // STX + Len + Data + ETX + LRC
            byte[] message = new byte[totalSize];

            int index = 0;
            message[index++] = STX;

            // Add length (4 ASCII characters)
            Array.Copy(lengthBytes, 0, message, index, lengthBytes.Length);
            index += lengthBytes.Length;

            // Add data
            Array.Copy(dataBytes, 0, message, index, dataBytes.Length);
            index += dataBytes.Length;

            message[index++] = ETX;

            // Calculate and add LRC (XOR from position 1 to ETX position, inclusive)
            byte lrc = 0x00;
            for (int i = 1; i < index; i++) // Start from 1 (skip STX), go to ETX (inclusive)
            {
                lrc ^= message[i];
            }
            message[index] = lrc;

            Debug.WriteLine($"Message built ({message.Length} bytes): {BitConverter.ToString(message)}");
            Debug.WriteLine($"LRC calculated: 0x{lrc:X2}");

            return message;
        }
        // Parse received message
        private (bool success, string data) ParseMessage(byte[] message)
        {
            try
            {
                if (message[0] != STX)
                {
                    Debug.WriteLine("Invalid message: Missing STX");
                    return (false, null);
                }

                // Find ETX position
                int etxIndex = -1;
                for (int i = 1; i < message.Length; i++)
                {
                    if (message[i] == ETX)
                    {
                        etxIndex = i;
                        break;
                    }
                }

                if (etxIndex == -1)
                {
                    Debug.WriteLine("Invalid message: Missing ETX");
                    return (false, null);
                }

                // Verify LRC
                byte receivedLRC = message[etxIndex + 1];
                byte calculatedLRC = CalculateLRC(message, 1, etxIndex);

                if (receivedLRC != calculatedLRC)
                {
                    Debug.WriteLine($"LRC mismatch: Received {receivedLRC:X2}, Calculated {calculatedLRC:X2}");
                    return (false, null);
                }

                // Extract data (skip STX and length bytes, up to ETX)
                int dataStart = 1 + 4; // After STX and 4-byte length
                int dataLength = etxIndex - dataStart;
                string data = Encoding.ASCII.GetString(message, dataStart, dataLength);

                return (true, data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing message: {ex.Message}");
                return (false, null);
            }
        }

        // Send message and wait for ACK
        // Send message and wait for ACK
        private async Task<bool> SendMessageWithRetry(byte[] message, int maxRetries = 2)
        {
            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        Debug.WriteLine($"Retry attempt {attempt}");
                        await Task.Delay(500); // Wait before retry
                    }

                    // Clear any existing data in buffers
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    // Send message
                    _serialPort.Write(message, 0, message.Length);
                    Debug.WriteLine($"Message sent: {BitConverter.ToString(message)}");

                    // Wait for ACK with better timeout handling
                    var startTime = DateTime.Now;
                    int waitTimeMs = 3000; // 3 seconds to wait for ACK

                    while ((DateTime.Now - startTime).TotalMilliseconds < waitTimeMs)
                    {
                        if (_serialPort.BytesToRead > 0)
                        {
                            byte response = (byte)_serialPort.ReadByte();
                            Debug.WriteLine($"Received byte: 0x{response:X2} ({response})");

                            if (response == ACK)
                            {
                                Debug.WriteLine("✓ Received ACK");
                                return true;
                            }
                            else if (response == NACK)
                            {
                                Debug.WriteLine("✗ Received NACK - will retry");
                                break; // Break to retry
                            }
                            else
                            {
                                Debug.WriteLine($"⚠ Unexpected byte: 0x{response:X2}, continuing to wait...");
                                // Continue waiting, might be noise
                            }
                        }

                        await Task.Delay(50); // Small delay before checking again
                    }

                    Debug.WriteLine("⚠ No ACK received - timeout");
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("⏱ Timeout waiting for ACK");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error sending message: {ex.Message}");
                }
            }

            Debug.WriteLine("❌ Failed after all retry attempts");
            return false;
        }
        // Receive response message
        // Receive response message
        private async Task<byte[]> ReceiveResponse(int timeoutMs = TIMEOUT_MS)
        {
            try
            {
                var startTime = DateTime.Now;
                var buffer = new System.Collections.Generic.List<byte>();

                Debug.WriteLine($"Waiting for response (timeout: {timeoutMs}ms)...");

                // Read until we get a complete message
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        byte b = (byte)_serialPort.ReadByte();
                        buffer.Add(b);

                        // Log first byte
                        if (buffer.Count == 1)
                        {
                            Debug.WriteLine($"First byte received: 0x{b:X2}");
                        }

                        // Check if we have a complete message (STX...ETX LRC)
                        if (buffer.Count >= 7) // Minimum message size
                        {
                            // Find ETX
                            for (int i = buffer.Count - 2; i >= 0; i--)
                            {
                                if (buffer[i] == ETX && buffer[0] == STX)
                                {
                                    // We have ETX and next byte should be LRC
                                    if (i == buffer.Count - 2)
                                    {
                                        byte[] completeMessage = buffer.ToArray();
                                        Debug.WriteLine($"Complete message received ({completeMessage.Length} bytes): {BitConverter.ToString(completeMessage)}");
                                        return completeMessage;
                                    }
                                }
                            }
                        }
                    }
                    await Task.Delay(50);
                }

                if (buffer.Count > 0)
                {
                    Debug.WriteLine($"⚠ Incomplete message received ({buffer.Count} bytes): {BitConverter.ToString(buffer.ToArray())}");
                }
                else
                {
                    Debug.WriteLine("⚠ No data received");
                }

                return null;
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("⏱ Timeout receiving response");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error receiving response: {ex.Message}");
                return null;
            }
        }
        // Build presentation header
        // Build presentation header
        private string BuildPresentationHeader(string transactionCode, string responseCode = "00", string requestResponseIndicator = "0")
        {
            // Transport Header: Type(60) + Destination(0000) + Source(0000)
            string transportHeader = "6000000000000000";  // 60 + 0000 + 0000 + 0000

            // Presentation Header: Format(1) + RRI + TransCode + RespCode + MoreToFollow(0) + Separator(1C)
            char separator = (char)0x1C;
            string presentationHeader = $"1{requestResponseIndicator}{transactionCode}{responseCode}0{separator}";

            string fullHeader = transportHeader + presentationHeader;

            Debug.WriteLine($"Transport Header: {transportHeader}");
            Debug.WriteLine($"Presentation Header: {presentationHeader.Replace(separator.ToString(), "<1C>")}");
            Debug.WriteLine($"Full Header: {fullHeader.Replace(separator.ToString(), "<1C>")}");

            return fullHeader;
        }
        // Build field data
        private string BuildFieldData(string fieldCode, string data)
        {
            // Field Code (2 bytes) + Length (2 bytes BCD) + Data + Separator (0x1C)
            int dataLength = data.Length;
            string lengthHex = dataLength.ToString("X4");
            return fieldCode + lengthHex + data + "\x1C";
        }

        // Parse response data
        public PaymentResponse ParsePaymentResponse(string responseData)
        {
            try
            {
                var response = new PaymentResponse();

                // Skip transport header (12 bytes) and parse presentation header
                int index = 12;

                response.FormatVersion = responseData.Substring(index, 1);
                index += 1;

                response.RequestResponseIndicator = responseData.Substring(index, 1);
                index += 1;

                response.TransactionCode = responseData.Substring(index, 2);
                index += 2;

                response.ResponseCode = responseData.Substring(index, 2);
                index += 2;

                response.MoreToFollow = responseData.Substring(index, 1);
                index += 1;

                // Skip separator
                index += 1;

                // Parse field data
                while (index < responseData.Length)
                {
                    if (index + 6 > responseData.Length) break;

                    string fieldCode = responseData.Substring(index, 2);
                    index += 2;

                    string lengthHex = responseData.Substring(index, 4);
                    int fieldLength = Convert.ToInt32(lengthHex, 16);
                    index += 4;

                    if (index + fieldLength > responseData.Length) break;

                    string fieldData = responseData.Substring(index, fieldLength);
                    index += fieldLength;

                    // Parse specific fields
                    switch (fieldCode)
                    {
                        case "02": response.ResponseText = fieldData; break;
                        case "01": response.ApprovalCode = fieldData; break;
                        case "65": response.InvoiceNumber = fieldData; break;
                        case "64": response.TraceNumber = fieldData; break;
                        case "30": response.CardNumber = fieldData; break;
                        case "D4": response.CardLabel = fieldData; break;
                        case "D5": response.CardholderName = fieldData; break;
                        case "31": response.ExpiryDate = fieldData; break;
                        case "16": response.TerminalID = fieldData; break;
                        case "D1": response.MerchantNumber = fieldData; break;
                        case "40": response.Amount = fieldData; break;
                        case "42": response.CashbackAmount = fieldData; break;
                        case "50": response.BatchNumber = fieldData; break;
                        case "06": response.RetrievalReferenceNumber = fieldData; break;
                        case "E6": response.CardEntryMode = fieldData; break;
                        case "03": response.TransactionDate = fieldData; break;
                        case "04": response.TransactionTime = fieldData; break;
                        case "66": response.TransactionID = fieldData; break;
                        case "17": response.ReceiptFooterMerchant = fieldData; break;
                        case "18": response.ReceiptFooterCustomer = fieldData; break;
                    }

                    // Skip separator
                    if (index < responseData.Length && responseData[index] == '\x1C')
                    {
                        index++;
                    }
                }

                response.IsApproved = response.ResponseCode == "00";
                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing response: {ex.Message}");
                return new PaymentResponse { IsApproved = false, ResponseText = "Error parsing response" };
            }
        }

        // Purchase Transaction (Transaction Code '20')
        public async Task<PaymentResponse> ProcessPurchase(string transactionId, decimal amount, int merchantIndex = 0)
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    if (!OpenConnection())
                    {
                        return new PaymentResponse
                        {
                            IsApproved = false,
                            ResponseText = "Failed to connect to terminal"
                        };
                    }
                }

                // Build message
                string header = BuildPresentationHeader("20");

                // Add fields
                string fields = "";
                fields += BuildFieldData("66", transactionId.PadLeft(20, '0')); // Transaction ID

                // Amount with implied decimal (multiply by 100)
                string amountStr = ((long)(amount * 100)).ToString().PadLeft(12, '0');
                fields += BuildFieldData("40", amountStr);

                fields += BuildFieldData("M1", merchantIndex.ToString("D2")); // Merchant Index

                string messageData = header + fields;
                byte[] message = BuildMessage(messageData);

                // Send request
                Debug.WriteLine("Sending purchase request...");
                if (!await SendMessageWithRetry(message))
                {
                    return new PaymentResponse
                    {
                        IsApproved = false,
                        ResponseText = "Failed to send request to terminal"
                    };
                }

                // Wait for response
                Debug.WriteLine("Waiting for terminal response...");
                byte[] responseBytes = await ReceiveResponse();

                if (responseBytes == null)
                {
                    return new PaymentResponse
                    {
                        IsApproved = false,
                        ResponseText = "Timeout waiting for terminal response"
                    };
                }

                // Send ACK for response
                _serialPort.Write(new byte[] { ACK }, 0, 1);
                Debug.WriteLine("Sent ACK for response");

                // Parse response
                var (success, responseData) = ParseMessage(responseBytes);

                if (!success)
                {
                    return new PaymentResponse
                    {
                        IsApproved = false,
                        ResponseText = "Invalid response from terminal"
                    };
                }

                return ParsePaymentResponse(responseData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing purchase: {ex.Message}");
                return new PaymentResponse
                {
                    IsApproved = false,
                    ResponseText = $"Error: {ex.Message}"
                };
            }
        }

        // Ping Terminal (Transaction Code 'FF')
        public async Task<bool> PingTerminal()
        {
            try
            {
                if (!_serialPort.IsOpen)
                {
                    if (!OpenConnection())
                        return false;
                }

                string header = BuildPresentationHeader("FF");
                byte[] message = BuildMessage(header);

                if (!await SendMessageWithRetry(message))
                    return false;

                byte[] responseBytes = await ReceiveResponse(5000);

                if (responseBytes == null)
                    return false;

                _serialPort.Write(new byte[] { ACK }, 0, 1);

                var (success, responseData) = ParseMessage(responseBytes);

                if (success && responseData.Contains("FFCP"))
                {
                    Debug.WriteLine("Terminal is online");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error pinging terminal: {ex.Message}");
                return false;
            }
        }

        // Add this test method to PaymentTerminalService
        public async Task<bool> PingTerminalRaw()
        {
            try
            {
                Debug.WriteLine("=== RAW PING TEST ===");

                if (!_serialPort.IsOpen)
                {
                    if (!OpenConnection())
                        return false;
                }

                // Clear buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // This is the EXACT ping message from the documentation (page 87)
                // 02 00 18 36 30 30 30 30 30 30 30 30 30 31 30 46 46 30 30 30 1C 03 30
                byte[] pingMessage = new byte[]
                {
            0x02,                                           // STX
            0x30, 0x30, 0x31, 0x38,                        // Length = "0018" (24 bytes)
            0x36, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, // Transport header "60000000"
            0x30, 0x30, 0x30, 0x30,                        // "0000"
            0x31, 0x30, 0x46, 0x46, 0x30, 0x30, 0x30,      // Presentation: "1" + "0" + "FF" + "00" + "0"
            0x1C,                                           // Separator
            0x03,                                           // ETX
            0x30                                            // LRC
                };

                Debug.WriteLine($"Sending RAW ping: {BitConverter.ToString(pingMessage)}");

                // Send the message
                _serialPort.Write(pingMessage, 0, pingMessage.Length);

                // Wait for any response
                await Task.Delay(2000);

                if (_serialPort.BytesToRead > 0)
                {
                    List<byte> response = new List<byte>();
                    while (_serialPort.BytesToRead > 0)
                    {
                        response.Add((byte)_serialPort.ReadByte());
                        await Task.Delay(10);
                    }

                    Debug.WriteLine($"✓ Received {response.Count} bytes: {BitConverter.ToString(response.ToArray())}");
                    return response.Count > 0;
                }
                else
                {
                    Debug.WriteLine("✗ No response received");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}");
                return false;
            }
        }
        // Add to PaymentTerminalService.cs
        public async Task<string> TestDifferentBaudRates()
        {
            int[] baudRates = { 9600, 19200, 38400, 57600, 115200 };
            StringBuilder result = new StringBuilder();

            result.AppendLine("=== TESTING DIFFERENT BAUD RATES ===");

            foreach (int baudRate in baudRates)
            {
                try
                {
                    if (_serialPort.IsOpen)
                        _serialPort.Close();

                    _serialPort.BaudRate = baudRate;
                    _serialPort.Open();

                    Debug.WriteLine($"\nTesting Baud Rate: {baudRate}");
                    result.AppendLine($"\nBaud Rate: {baudRate}");

                    // Clear buffers
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    // Send raw ping
                    byte[] pingMessage = new byte[]
                    {
                0x02, 0x30, 0x30, 0x31, 0x38, 0x36, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
                0x30, 0x30, 0x30, 0x31, 0x30, 0x46, 0x46, 0x30, 0x30, 0x30, 0x1C, 0x03, 0x30
                    };

                    _serialPort.Write(pingMessage, 0, pingMessage.Length);
                    Debug.WriteLine("Ping sent, waiting for response...");

                    await Task.Delay(2000);

                    if (_serialPort.BytesToRead > 0)
                    {
                        List<byte> response = new List<byte>();
                        while (_serialPort.BytesToRead > 0)
                        {
                            response.Add((byte)_serialPort.ReadByte());
                            await Task.Delay(10);
                        }

                        string responseStr = BitConverter.ToString(response.ToArray());
                        Debug.WriteLine($"✓✓✓ RESPONSE RECEIVED at {baudRate}: {responseStr}");
                        result.AppendLine($"✓ SUCCESS! Response: {responseStr}");
                        result.AppendLine($"\n*** USE BAUD RATE: {baudRate} ***");

                        _serialPort.Close();
                        return result.ToString();
                    }
                    else
                    {
                        Debug.WriteLine("✗ No response");
                        result.AppendLine("✗ No response");
                    }

                    _serialPort.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error at {baudRate}: {ex.Message}");
                    result.AppendLine($"✗ Error: {ex.Message}");
                }
            }

            result.AppendLine("\n❌ No baud rate worked");
            return result.ToString();
        }
        // Add to PaymentTerminalService.cs
        public async Task<string> SendSimpleTestBytes()
        {
            StringBuilder result = new StringBuilder();
            result.AppendLine("=== SIMPLE BYTES TEST ===");

            try
            {
                if (!_serialPort.IsOpen)
                    _serialPort.Open();

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // Test 1: Send ENQ (Enquiry)
                Debug.WriteLine("Test 1: Sending ENQ (0x05)");
                _serialPort.Write(new byte[] { 0x05 }, 0, 1);
                await Task.Delay(1000);

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] resp = new byte[_serialPort.BytesToRead];
                    _serialPort.Read(resp, 0, resp.Length);
                    result.AppendLine($"ENQ Response: {BitConverter.ToString(resp)}");
                    Debug.WriteLine($"✓ ENQ Response: {BitConverter.ToString(resp)}");
                }
                else
                {
                    result.AppendLine("ENQ: No response");
                    Debug.WriteLine("✗ ENQ: No response");
                }

                await Task.Delay(500);
                _serialPort.DiscardInBuffer();

                // Test 2: Send ACK
                Debug.WriteLine("Test 2: Sending ACK (0x06)");
                _serialPort.Write(new byte[] { 0x06 }, 0, 1);
                await Task.Delay(1000);

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] resp = new byte[_serialPort.BytesToRead];
                    _serialPort.Read(resp, 0, resp.Length);
                    result.AppendLine($"ACK Response: {BitConverter.ToString(resp)}");
                    Debug.WriteLine($"✓ ACK Response: {BitConverter.ToString(resp)}");
                }
                else
                {
                    result.AppendLine("ACK: No response");
                    Debug.WriteLine("✗ ACK: No response");
                }

                await Task.Delay(500);
                _serialPort.DiscardInBuffer();

                // Test 3: Send STX + ETX
                Debug.WriteLine("Test 3: Sending STX-ETX");
                _serialPort.Write(new byte[] { 0x02, 0x03 }, 0, 2);
                await Task.Delay(1000);

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] resp = new byte[_serialPort.BytesToRead];
                    _serialPort.Read(resp, 0, resp.Length);
                    result.AppendLine($"STX-ETX Response: {BitConverter.ToString(resp)}");
                    Debug.WriteLine($"✓ STX-ETX Response: {BitConverter.ToString(resp)}");
                }
                else
                {
                    result.AppendLine("STX-ETX: No response");
                    Debug.WriteLine("✗ STX-ETX: No response");
                }

                // Test 4: Check if terminal is sending anything on its own
                await Task.Delay(500);
                _serialPort.DiscardInBuffer();

                Debug.WriteLine("Test 4: Listening for 3 seconds...");
                result.AppendLine("\nListening for unsolicited data...");

                await Task.Delay(3000);

                if (_serialPort.BytesToRead > 0)
                {
                    byte[] resp = new byte[_serialPort.BytesToRead];
                    _serialPort.Read(resp, 0, resp.Length);
                    result.AppendLine($"✓ Unsolicited data received: {BitConverter.ToString(resp)}");
                    Debug.WriteLine($"✓ Unsolicited data: {BitConverter.ToString(resp)}");
                }
                else
                {
                    result.AppendLine("No unsolicited data");
                    Debug.WriteLine("✗ No unsolicited data");
                }

            }
            catch (Exception ex)
            {
                result.AppendLine($"Error: {ex.Message}");
                Debug.WriteLine($"Error: {ex.Message}");
            }

            return result.ToString();
        }
        // Cancel Transaction (Transaction Code 'XX')
        public async Task<bool> CancelTransaction(string transactionId)
        {
            try
            {
                if (!_serialPort.IsOpen)
                    return false;

                string header = BuildPresentationHeader("XX");
                string fields = "";
                fields += BuildFieldData("00", "00000000000000000000"); // Pay Account ID
                fields += BuildFieldData("66", transactionId.PadLeft(20, '0'));

                string messageData = header + fields;
                byte[] message = BuildMessage(messageData);

                if (!await SendMessageWithRetry(message))
                    return false;

                byte[] responseBytes = await ReceiveResponse(10000);

                if (responseBytes == null)
                    return false;

                _serialPort.Write(new byte[] { ACK }, 0, 1);

                var (success, responseData) = ParseMessage(responseBytes);
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error canceling transaction: {ex.Message}");
                return false;
            }
        }
    }

    // Payment Response Model
    public class PaymentResponse
    {
        public bool IsApproved { get; set; }
        public string ResponseCode { get; set; }
        public string ResponseText { get; set; }
        public string TransactionID { get; set; }
        public string Amount { get; set; }
        public string CashbackAmount { get; set; }
        public string ApprovalCode { get; set; }
        public string InvoiceNumber { get; set; }
        public string TraceNumber { get; set; }
        public string CardNumber { get; set; }
        public string CardLabel { get; set; }
        public string CardholderName { get; set; }
        public string ExpiryDate { get; set; }
        public string TerminalID { get; set; }
        public string MerchantNumber { get; set; }
        public string BatchNumber { get; set; }
        public string RetrievalReferenceNumber { get; set; }
        public string CardEntryMode { get; set; }
        public string TransactionDate { get; set; }
        public string TransactionTime { get; set; }
        public string ReceiptFooterMerchant { get; set; }
        public string ReceiptFooterCustomer { get; set; }
        public string FormatVersion { get; set; }
        public string RequestResponseIndicator { get; set; }
        public string TransactionCode { get; set; }
        public string MoreToFollow { get; set; }
    }
}