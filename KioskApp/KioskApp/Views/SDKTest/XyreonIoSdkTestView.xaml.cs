using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Xyreon;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class XyreonIoSdkTestView :
        UserControl
    {
        public event EventHandler?
            BackRequested;

        private readonly DispatcherTimer
            _timer;

        private bool
            _refreshing;

        private TextBlock[]
            _inputs = null!;

        private Button[]
            _outputs = null!;

        private readonly string[]
            _outputNames =
            {
                "Printer LED",
                "Passport LED",
                "Dispenser LED",
                "Acceptor LED",
                "MyKad LED",
                "SIREN",
                "ELECTRONIC LOCK",
                "Camera White LED",
                "Unknown",
                "Unknown",
                "Unknown",
                "Unknown"
            };

        public XyreonIoSdkTestView()
        {
            InitializeComponent();

            _inputs =
                new[]
                {
                    DI0,
                    DI1,
                    DI2,
                    DI3,
                    DI4,
                    DI5,
                    DI6,
                    DI7
                };

            _outputs =
                new[]
                {
                    DO0,
                    DO1,
                    DO2,
                    DO3,
                    DO4,
                    DO5,
                    DO6,
                    DO7,
                    DO8,
                    DO9,
                    DO10,
                    DO11
                };

            _timer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan
                            .FromMilliseconds(
                                250)
                };

            _timer.Tick +=
                Timer_Tick;
        }

        private async void UserControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAsync();

            _timer.Start();
        }

        //private void UserControl_Unloaded(
        //    object sender,
        //    RoutedEventArgs e)
        //{
        //    _timer.Stop();
        //}
        private void UserControl_Unloaded(
    object sender,
    RoutedEventArgs e)
        {
            _timer.Stop();

            var io =
                GlobalHardwareManager.XyreonIo;

            if (io == null)
                return;

            // SDK test may temporarily stop automation
            // so DO5 can be manually tested.
            //
            // When leaving the test page, restore global
            // security unless an authorized PIN override
            // is currently active.
            if (!io.DoorAlarmSuppressed &&
                !io.DoorAlarmAutomationEnabled)
            {
                io.StartDoorAlarmAutomation(
                    250);

                KioskLocalLogger.LogInfo(
                    "DoorSecurity",
                    "Door alarm automatically re-armed after leaving XYREON SDK test.");
            }
        }

        private async void Timer_Tick(
            object? sender,
            EventArgs e)
        {
            if (_refreshing)
                return;

            _refreshing =
                true;

            try
            {
                await RefreshAsync();
            }
            finally
            {
                _refreshing =
                    false;
            }
        }

        private async System.Threading.Tasks.Task
            RefreshAsync()
        {
            var io =
                GlobalHardwareManager
                    .XyreonIo;

            if (io == null)
            {
                TxtConnection.Text =
                    "XYREON service not initialized";

                return;
            }

            if (!io.IsConnected)
            {
                bool connected =
                    await io.AutoConnectAsync();

                if (!connected)
                {
                    TxtConnection.Text =
                        "Connection failed";

                    TxtConnection.Foreground =
                        Brushes.OrangeRed;

                    return;
                }
            }

            var state =
                await io.RefreshStateAsync();

            if (state == null)
            {
                TxtConnection.Text =
                    "No response";

                return;
            }

            TxtConnection.Text =
                $"Connected: {io.PortName}";

            TxtConnection.Foreground =
                Brushes.LightGreen;

            for (int i = 0;
                 i < 8;
                 i++)
            {
                bool on =
                    state.Inputs[i];

                _inputs[i].Text =
                    $"DI{i} {(on ? "ON / 1" : "OFF / 0")}";

                _inputs[i].Background =
                    on
                        ? Brushes.DarkGreen
                        : Brushes.DarkSlateGray;
            }

            for (int i = 0;
                 i < 12;
                 i++)
            {
                bool on =
                    state.Outputs[i];

                _outputs[i].Content =
                    $"DO{i} {_outputNames[i]} [{(on ? "ON" : "OFF")}]";
            }

            TxtRaw.Text =
                $"DI mask: 0x{state.InputMask:X2}   " +
                $"DO mask: 0x{state.OutputMask:X3}   " +
                $"{state.RawFrame}";

            // Confirmed:
            // DI0 ON  = closed
            // DI0 OFF = open
            if (state.IsLowerDoorOpen)
            {
                TxtDoor.Text =
                    "LOWER DOOR: OPEN  (DI0 OFF)";

                TxtDoor.Foreground =
                    Brushes.OrangeRed;
            }
            else
            {
                TxtDoor.Text =
                    "LOWER DOOR: CLOSED  (DI0 ON)";

                TxtDoor.Foreground =
                    Brushes.LightGreen;
            }

            BtnAlarmAuto.Content =
                io.DoorAlarmAutomationEnabled
                    ? "STOP DOOR ALARM AUTO"
                    : "START DOOR ALARM AUTO";
        }

        private async void Connect_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void Output_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            if (!int.TryParse(
                    button.Tag?.ToString(),
                    out int index))
            {
                return;
            }

            var io =
                GlobalHardwareManager
                    .XyreonIo;

            if (io == null)
                return;

            var state =
                await io.RefreshStateAsync();

            if (state == null)
                return;

            bool newState =
                !state.Outputs[index];

            // DO5 auto alarm owns the siren while enabled.
            if (index == 5 &&
                io.DoorAlarmAutomationEnabled)
            {
                MessageBox.Show(
                    "Stop Door Alarm Auto before manually testing DO5.",
                    "XYREON",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            // Additional warning for outputs which can
            // cause physical movement/noise.
            if (newState &&
                (index == 5 ||
                 index == 6 ||
                 index >= 8))
            {
                string description =
                    index switch
                    {
                        5 =>
                            "This will turn the SIREN ON.",

                        6 =>
                            "This will energize the ELECTRONIC LOCK output.",

                        _ =>
                            $"DO{index} is not yet physically identified."
                    };

                var answer =
                    MessageBox.Show(
                        description +
                        "\n\nContinue?",
                        "Confirm Output Test",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (answer !=
                    MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await io.SetRawOutputAsync(
                index,
                newState);

            await RefreshAsync();
        }

        private async void AlarmAuto_Click(
            object sender,
            RoutedEventArgs e)
        {
            var io =
                GlobalHardwareManager
                    .XyreonIo;

            if (io == null)
                return;

            if (io
                .DoorAlarmAutomationEnabled)
            {
                await io
                    .StopDoorAlarmAutomationAsync(
                        turnSirenOff:
                            true);
            }
            else
            {
                var answer =
                    MessageBox.Show(
                        "Start lower-door alarm test?\n\n" +
                        "Door CLOSED: DI0 ON\n" +
                        "Door OPEN: DI0 OFF\n\n" +
                        "Opening the door will turn DO5 siren ON.\n" +
                        "Closing the door will turn DO5 siren OFF.",
                        "Door Alarm Test",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (answer !=
                    MessageBoxResult.Yes)
                {
                    return;
                }

                io.StartDoorAlarmAutomation(
                    250);
            }

            await RefreshAsync();
        }

        private void Back_Click(
            object sender,
            RoutedEventArgs e)
        {
            BackRequested?.Invoke(
                this,
                EventArgs.Empty);
        }
    }
}