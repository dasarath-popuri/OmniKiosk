using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OmniKiosk.Wpf.Controls;

namespace OmniKiosk.Wpf.Services.MoneyExchange
{
    /// <summary>
    /// Drives the "are you still there" flow once a customer moves past currency
    /// selection: 30 seconds with no touch/mouse/keyboard activity brings up
    /// CountdownDialog; no response (or an explicit Go Back) fires TimedOut,
    /// which the flow view uses to return to CurrencySelectionStep. Started and
    /// stopped by the flow view per step - not active while just browsing rates.
    /// </summary>
    public sealed class MoneyExchangeTimeoutManager
    {
        private const int InactivitySeconds = 60;
        private const int WarningCountdownSeconds = 20;

        private readonly Window _hostWindow;
        private readonly DispatcherTimer _inactivityTimer;
        private DispatcherTimer? _countdownTimer;
        private CountdownDialog? _dialog;
        private int _secondsLeft;
        private bool _monitoring;

        public event EventHandler? TimedOut;

        public MoneyExchangeTimeoutManager(Window hostWindow)
        {
            _hostWindow = hostWindow;
            _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(InactivitySeconds) };
            _inactivityTimer.Tick += (_, __) => ShowWarning();
        }

        public void Start()
        {
            if (_monitoring) return;
            _monitoring = true;
            _hostWindow.PreviewMouseDown += OnActivity;
            _hostWindow.PreviewTouchDown += OnActivity;
            _hostWindow.PreviewKeyDown += OnActivity;
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }

        public void Stop()
        {
            if (!_monitoring) return;
            _monitoring = false;
            _hostWindow.PreviewMouseDown -= OnActivity;
            _hostWindow.PreviewTouchDown -= OnActivity;
            _hostWindow.PreviewKeyDown -= OnActivity;
            _inactivityTimer.Stop();
            _countdownTimer?.Stop();
            CloseDialogIfOpen();
        }

        private void OnActivity(object sender, InputEventArgs e)
        {
            // While the warning dialog itself is open, only its own Stay Here /
            // Go Back buttons should count - ambient taps elsewhere shouldn't
            // silently dismiss the warning without an explicit choice.
            if (_dialog != null) return;
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }

        private void ShowWarning()
        {
            _inactivityTimer.Stop();
            _secondsLeft = WarningCountdownSeconds;

            _dialog = new CountdownDialog(_secondsLeft) { Owner = _hostWindow };
            _dialog.StayHereClicked += (_, __) => Resume();
            _dialog.GoBackClicked += (_, __) => Timeout();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, __) =>
            {
                _secondsLeft--;
                if (_secondsLeft <= 0)
                {
                    _countdownTimer?.Stop();
                    Timeout();
                    return;
                }
                _dialog?.UpdateCountdown(_secondsLeft);
            };
            _countdownTimer.Start();

            _dialog.ShowDialog();
        }

        private void Resume()
        {
            _countdownTimer?.Stop();
            CloseDialogIfOpen();
            if (_monitoring) _inactivityTimer.Start();
        }

        private void Timeout()
        {
            _countdownTimer?.Stop();
            CloseDialogIfOpen();
            TimedOut?.Invoke(this, EventArgs.Empty);
            if (_monitoring) _inactivityTimer.Start();
        }

        private void CloseDialogIfOpen()
        {
            if (_dialog == null) return;
            try { _dialog.Close(); } catch { /* already closing */ }
            _dialog = null;
        }
    }
}
