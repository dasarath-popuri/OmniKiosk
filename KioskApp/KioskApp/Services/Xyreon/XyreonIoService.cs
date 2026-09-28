using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services.Xyreon
{
    public enum KioskInput
    {
        LowerDoorSensor = 0
    }

    public enum KioskOutput
    {
        ReceiptPrinterGreenLight = 0,
        PassportScannerGreenLight = 1,
        CashDispenserGreenLight = 2,
        CashAcceptorGreenLight = 3,
        MyKadScannerGreenLight = 4,

        Siren = 5,
        ElectronicLock = 6,

        CameraWhiteLight = 7,

        // Backward-compatible name already used
        // in FaceVerificationStep.xaml.cs
        CameraSideLights = CameraWhiteLight,

        Output8 = 8,
        Output9 = 9,
        Output10 = 10,
        Output11 = 11
    }
    public sealed class XyreonIoState
    {
        public int InputMask { get; init; }

        public int OutputMask { get; init; }

        public bool[] Inputs { get; init; } =
            Array.Empty<bool>();

        public bool[] Outputs { get; init; } =
            Array.Empty<bool>();

        public string RawFrame { get; init; } =
            "";

        // Confirmed on the physical kiosk:
        //
        // DI0 ON  = lower door CLOSED
        // DI0 OFF = lower door OPEN
        //
        public bool IsLowerDoorOpen =>
            Inputs.Length > 0 &&
            !Inputs[(int)KioskInput.LowerDoorSensor];
    }

    public sealed class XyreonIoService :
        IDisposable
    {
        private const string CommandInterfaceId =
            "VID_2E8A&PID_3333&MI_02";

        private const int BaudRate =
            9600;

        private const int ReadTimeoutMs =
            700;

        private const int WriteTimeoutMs =
            700;

        private static readonly Regex StateRegex =
            new(
                @"^@GS(?<di>[0-9A-Fa-f]{2}),(?<do>[0-9A-Fa-f]{2,3})\*$",
                RegexOptions.Compiled);

        private readonly SemaphoreSlim _connectionLock =
            new(1, 1);

        private readonly SemaphoreSlim _ioLock =
            new(1, 1);

        private SerialPort? _port;

        private int _outputMask;

        private bool _stateReadSuccessfully;

        private bool _disposed;

        private CancellationTokenSource?
            _doorAlarmCts;

        private Task?
            _doorAlarmTask;
        private volatile bool
    _doorAlarmSuppressed;

        public bool IsConnected =>
            _port?.IsOpen == true;

        public string? PortName =>
            _port?.PortName;

        public XyreonIoState?
            LastState
        { get; private set; }

        public bool DoorAlarmAutomationEnabled =>
            _doorAlarmCts != null &&
            !_doorAlarmCts.IsCancellationRequested;
        public bool DoorAlarmSuppressed =>
    _doorAlarmSuppressed;
        public event Action<string>?
            Log;

        public event Action<string>?
            Error;

        public event Action<XyreonIoState>?
            StateChanged;

        // ============================================================
        // CONNECT
        // ============================================================

        public async Task<bool> AutoConnectAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            await _connectionLock.WaitAsync(
                cancellationToken);

            try
            {
                if (IsConnected &&
                    _stateReadSuccessfully)
                {
                    return true;
                }

                DisconnectInternal();

                string? portName =
                    FindCommandPort();

                if (string.IsNullOrWhiteSpace(
                        portName))
                {
                    Error?.Invoke(
                        "XYREON COMMAND port was not found.");

                    return false;
                }

                return await ConnectInternalAsync(
                    portName,
                    cancellationToken);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task<bool> ConnectInternalAsync(
            string portName,
            CancellationToken cancellationToken)
        {
            try
            {
                var serialPort =
                    new SerialPort(
                        portName,
                        BaudRate,
                        Parity.None,
                        8,
                        StopBits.One)
                    {
                        Handshake =
                            Handshake.None,

                        Encoding =
                            Encoding.ASCII,

                        ReadTimeout =
                            ReadTimeoutMs,

                        WriteTimeout =
                            WriteTimeoutMs,

                        DtrEnable =
                            false,

                        RtsEnable =
                            false
                    };

                await Task.Run(
                    () =>
                    {
                        cancellationToken
                            .ThrowIfCancellationRequested();

                        serialPort.Open();

                        Thread.Sleep(
                            120);

                        serialPort
                            .DiscardInBuffer();

                        serialPort
                            .DiscardOutBuffer();

                        serialPort.Write(
                            "@DHB*");

                        string response =
                            ReadFrame(
                                serialPort,
                                ReadTimeoutMs);

                        if (!response.StartsWith(
                                "@DHB,OK",
                                StringComparison
                                    .OrdinalIgnoreCase))
                        {
                            throw new
                                InvalidOperationException(
                                    "Unexpected XYREON handshake: " +
                                    response);
                        }
                    },
                    cancellationToken);

                _port =
                    serialPort;

                _stateReadSuccessfully =
                    false;

                var state =
                    await RefreshStateAsync(
                        cancellationToken);

                if (state == null)
                {
                    DisconnectInternal();

                    return false;
                }

                Log?.Invoke(
                    $"XYREON connected on {portName}");

                return true;
            }
            catch (Exception ex)
            {
                DisconnectInternal();

                Error?.Invoke(
                    $"XYREON connection failed: {ex.Message}");

                return false;
            }
        }

        // ============================================================
        // READ DI / DO STATE
        // ============================================================

        public async Task<XyreonIoState?>
            RefreshStateAsync(
                CancellationToken cancellationToken =
                    default)
        {
            ThrowIfDisposed();

            if (!IsConnected)
            {
                bool connected =
                    await AutoConnectAsync(
                        cancellationToken);

                if (!connected)
                    return null;
            }

            await _ioLock.WaitAsync(
                cancellationToken);

            try
            {
                return RefreshStateNoLock();
            }
            catch (Exception ex)
            {
                Error?.Invoke(
                    "XYREON state read failed: " +
                    ex.Message);

                DisconnectInternal();

                return null;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private XyreonIoState?
            RefreshStateNoLock()
        {
            if (_port?.IsOpen != true)
                return null;

            _port.DiscardInBuffer();

            _port.Write(
                "@GS*");

            DateTime timeoutAt =
                DateTime.UtcNow
                    .AddMilliseconds(
                        ReadTimeoutMs);

            while (DateTime.UtcNow <
                   timeoutAt)
            {
                int remaining =
                    Math.Max(
                        100,
                        (int)
                        (timeoutAt -
                         DateTime.UtcNow)
                        .TotalMilliseconds);

                string response;

                try
                {
                    response =
                        ReadFrame(
                            _port,
                            remaining);
                }
                catch (TimeoutException)
                {
                    return null;
                }

                if (!response.StartsWith(
                        "@GS",
                        StringComparison
                            .OrdinalIgnoreCase))
                {
                    continue;
                }

                Match match =
                    StateRegex.Match(
                        response);

                if (!match.Success)
                    return null;

                int inputMask =
                    int.Parse(
                        match.Groups["di"]
                            .Value,
                        NumberStyles.HexNumber,
                        CultureInfo
                            .InvariantCulture);

                int outputMask =
                    int.Parse(
                        match.Groups["do"]
                            .Value,
                        NumberStyles.HexNumber,
                        CultureInfo
                            .InvariantCulture);

                _outputMask =
                    outputMask;

                _stateReadSuccessfully =
                    true;

                var state =
                    new XyreonIoState
                    {
                        InputMask =
                            inputMask,

                        OutputMask =
                            outputMask,

                        Inputs =
                            Enumerable
                                .Range(
                                    0,
                                    8)
                                .Select(
                                    i =>
                                        (inputMask &
                                         (1 << i))
                                        != 0)
                                .ToArray(),

                        Outputs =
                            Enumerable
                                .Range(
                                    0,
                                    12)
                                .Select(
                                    i =>
                                        (outputMask &
                                         (1 << i))
                                        != 0)
                                .ToArray(),

                        RawFrame =
                            response
                    };

                LastState =
                    state;

                StateChanged?.Invoke(
                    state);

                return state;
            }

            return null;
        }

        // ============================================================
        // OUTPUT CONTROL
        // ============================================================

        public Task<bool> SetOutputAsync(
            KioskOutput output,
            bool enabled,
            CancellationToken cancellationToken =
                default)
        {
            return SetRawOutputAsync(
                (int)output,
                enabled,
                cancellationToken);
        }

        public async Task<bool> SetRawOutputAsync(
            int outputIndex,
            bool enabled,
            CancellationToken cancellationToken =
                default)
        {
            ThrowIfDisposed();

            if (outputIndex < 0 ||
                outputIndex > 11)
            {
                throw new
                    ArgumentOutOfRangeException(
                        nameof(outputIndex));
            }

            if (!IsConnected)
            {
                bool connected =
                    await AutoConnectAsync(
                        cancellationToken);

                if (!connected)
                    return false;
            }

            await _ioLock.WaitAsync(
                cancellationToken);

            try
            {
                // IMPORTANT:
                // Read current full output mask first.
                //
                // @Ixxx* writes the ENTIRE DO mask.
                //
                var state =
                    RefreshStateNoLock();

                if (state == null)
                    return false;

                int bit =
                    1 << outputIndex;

                int newMask =
                    enabled
                        ? _outputMask | bit
                        : _outputMask & ~bit;

                if (newMask ==
                    _outputMask)
                {
                    return true;
                }

                string command =
                    $"@I{newMask:X3}*";

                _port!.Write(
                    command);

                Thread.Sleep(
                    30);

                _port
                    .DiscardInBuffer();

                var verified =
                    RefreshStateNoLock();

                if (verified == null)
                {
                    Error?.Invoke(
                        $"DO{outputIndex} command sent but verification failed.");

                    _outputMask =
                        newMask;

                    return true;
                }

                bool actualState =
                    verified
                        .Outputs[
                            outputIndex];

                if (actualState !=
                    enabled)
                {
                    Error?.Invoke(
                        $"DO{outputIndex} verification mismatch.");

                    return false;
                }

                Log?.Invoke(
                    $"DO{outputIndex} -> " +
                    (enabled
                        ? "ON"
                        : "OFF"));

                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(
                    $"DO{outputIndex} failed: " +
                    ex.Message);

                return false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<bool> TurnOffKnownLightsAsync(
    CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!IsConnected)
            {
                bool connected =
                    await AutoConnectAsync(
                        cancellationToken);

                if (!connected)
                    return false;
            }

            await _ioLock.WaitAsync(
                cancellationToken);

            try
            {
                // Read actual current hardware state first.
                var state =
                    RefreshStateNoLock();

                if (state == null)
                    return false;

                // Only the normal guidance LEDs are cleared.
                //
                // DO0 Printer LED
                // DO1 Passport LED
                // DO2 Dispenser LED
                // DO3 Acceptor LED
                // DO4 MyKad LED
                // DO7 Camera LED
                //
                // DO5 Siren             PRESERVED
                // DO6 Electronic Lock   PRESERVED
                // DO8-DO11              PRESERVED
                int knownLightMask =
                    (1 << 0) |
                    (1 << 1) |
                    (1 << 2) |
                    (1 << 3) |
                    (1 << 4) |
                    (1 << 7);

                int newMask =
                    _outputMask &
                    ~knownLightMask;

                if (newMask ==
                    _outputMask)
                {
                    return true;
                }

                _port!.Write(
                    $"@I{newMask:X3}*");

                Thread.Sleep(
                    30);

                _port.DiscardInBuffer();

                _outputMask =
                    newMask;

                RefreshStateNoLock();

                Log?.Invoke(
                    $"Known kiosk LEDs OFF. " +
                    $"DO mask = 0x{_outputMask:X3}");

                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(
                    "Failed to turn known kiosk lights off: " +
                    ex.Message);

                return false;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        // ============================================================
        // LOWER DOOR -> SIREN TEST AUTOMATION
        // ============================================================

        public void StartDoorAlarmAutomation(
            int pollIntervalMs = 250)
        {
            ThrowIfDisposed();

            if (DoorAlarmAutomationEnabled)
                return;

            pollIntervalMs =
                Math.Max(
                    100,
                    pollIntervalMs);

            _doorAlarmCts =
                new CancellationTokenSource();

            CancellationToken token =
                _doorAlarmCts.Token;

            _doorAlarmTask =
                Task.Run(
                    async () =>
                    {
                        bool?
                            previousRequiredAlarm =
                                null;

                        while (!token
                               .IsCancellationRequested)
                        {
                            try
                            {
                                var state =
                                    await RefreshStateAsync(
                                        token);

                                if (state != null)
                                {
                                    //bool
                                    //    alarmRequired =
                                    //        state
                                    //            .IsLowerDoorOpen;
                                    bool alarmRequired =state.IsLowerDoorOpen &&!_doorAlarmSuppressed;

                                    if (previousRequiredAlarm !=
                                        alarmRequired)
                                    {
                                        await SetOutputAsync(
                                            KioskOutput
                                                .Siren,
                                            alarmRequired,
                                            token);

                                        previousRequiredAlarm =
                                            alarmRequired;

                                        Log?.Invoke(
                                            alarmRequired
                                                ? "Door OPEN -> DO5 ON"
                                                : "Door CLOSED -> DO5 OFF");
                                    }
                                }
                            }
                            catch (
                                OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                Error?.Invoke(
                                    "Door alarm monitor: " +
                                    ex.Message);
                            }

                            try
                            {
                                await Task.Delay(
                                    pollIntervalMs,
                                    token);
                            }
                            catch (
                                OperationCanceledException)
                            {
                                break;
                            }
                        }
                    },
                    token);
        }
        public async Task SetDoorAlarmSuppressedAsync(
    bool suppressed)
        {
            ThrowIfDisposed();

            _doorAlarmSuppressed =
                suppressed;

            Log?.Invoke(
                suppressed
                    ? "Authorized door access enabled - alarm suppressed."
                    : "Authorized door access ended - alarm re-armed.");

            if (suppressed &&
                IsConnected)
            {
                // If someone enters the correct PIN while
                // the siren is already sounding, stop it
                // immediately.
                await SetOutputAsync(
                    KioskOutput.Siren,
                    false);
            }
        }
        public async Task
            StopDoorAlarmAutomationAsync(
                bool turnSirenOff = true)
        {
            CancellationTokenSource?
                cts =
                    _doorAlarmCts;

            if (cts == null)
                return;

            _doorAlarmCts =
                null;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            try
            {
                if (_doorAlarmTask != null)
                {
                    await _doorAlarmTask;
                }
            }
            catch
            {
            }

            _doorAlarmTask =
                null;
            _doorAlarmSuppressed =
    false;
            cts.Dispose();

            if (turnSirenOff &&
                IsConnected)
            {
                try
                {
                    await SetOutputAsync(
                        KioskOutput.Siren,
                        false);
                }
                catch
                {
                }
            }
        }

        // ============================================================
        // SERIAL HELPERS
        // ============================================================

        private static string ReadFrame(
            SerialPort port,
            int timeoutMs)
        {
            var text =
                new StringBuilder();

            DateTime timeoutAt =
                DateTime.UtcNow
                    .AddMilliseconds(
                        timeoutMs);

            while (DateTime.UtcNow <
                   timeoutAt)
            {
                int remaining =
                    Math.Max(
                        50,
                        (int)
                        (timeoutAt -
                         DateTime.UtcNow)
                        .TotalMilliseconds);

                port.ReadTimeout =
                    remaining;

                int value =
                    port.ReadChar();

                if (value < 0)
                    continue;

                char c =
                    (char)value;

                text.Append(
                    c);

                if (c == '*')
                    return text.ToString();
            }

            throw new TimeoutException(
                "XYREON response timeout.");
        }

        private static string?
            FindCommandPort()
        {
            try
            {
                using RegistryKey?
                    usbRoot =
                        Registry.LocalMachine
                            .OpenSubKey(
                                @"SYSTEM\CurrentControlSet\Enum\USB");

                if (usbRoot == null)
                    return null;

                foreach (
                    string deviceName
                    in usbRoot
                        .GetSubKeyNames())
                {
                    if (!deviceName
                        .StartsWith(
                            CommandInterfaceId,
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using RegistryKey?
                        device =
                            usbRoot
                                .OpenSubKey(
                                    deviceName);

                    if (device == null)
                        continue;

                    foreach (
                        string instanceName
                        in device
                            .GetSubKeyNames())
                    {
                        using RegistryKey?
                            parameters =
                                device.OpenSubKey(
                                    instanceName +
                                    @"\Device Parameters");

                        string? port =
                            parameters?
                                .GetValue(
                                    "PortName")
                                as string;

                        if (!string
                            .IsNullOrWhiteSpace(
                                port))
                        {
                            return port;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        // ============================================================

        private void DisconnectInternal()
        {
            _stateReadSuccessfully =
                false;

            LastState =
                null;

            try
            {
                if (_port?.IsOpen ==
                    true)
                {
                    _port.Close();
                }
            }
            catch
            {
            }

            try
            {
                _port?.Dispose();
            }
            catch
            {
            }

            _port =
                null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new
                    ObjectDisposedException(
                        nameof(
                            XyreonIoService));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                StopDoorAlarmAutomationAsync(
                        turnSirenOff:
                            true)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
            }

            DisconnectInternal();

            _connectionLock.Dispose();
            _ioLock.Dispose();

            _disposed =
                true;
        }
    }
}