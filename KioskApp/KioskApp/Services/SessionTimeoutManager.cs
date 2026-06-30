using System;
using System.Windows;
using System.Windows.Threading;
using OmniKiosk.Wpf.Controls;

namespace OmniKiosk.Wpf.Services
{
    public class SessionTimeoutManager
    {
        private DispatcherTimer _inactivityTimer;
        private DispatcherTimer _countdownTimer;
        private int _countdownSeconds;
        private Window _parentWindow;
        private Action _onTimeout;
        private bool _isCountdownActive = false;
        private CountdownDialog _countdownDialog;
        private bool _isHandlingUserAction = false;

        public SessionTimeoutManager(Window parentWindow, Action onTimeout, int timeoutMinutes = 2)
        {
            _parentWindow = parentWindow;
            _onTimeout = onTimeout;

            _inactivityTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(timeoutMinutes)
            };
            _inactivityTimer.Tick += InactivityTimer_Tick;

            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += CountdownTimer_Tick;

            _parentWindow.PreviewMouseMove += ResetTimer;
            _parentWindow.PreviewMouseDown += ResetTimer;
            _parentWindow.PreviewKeyDown += ResetTimer;
            _parentWindow.PreviewTouchDown += ResetTimer;
        }

        public void Start()
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Timer started");
        }

        public void Stop()
        {
            _inactivityTimer.Stop();
            _countdownTimer.Stop();
            _isCountdownActive = false;
            _isHandlingUserAction = false;

            CloseCountdownDialogImmediately();
            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Timer stopped");
        }

        public void Reset()
        {
            if (!_isCountdownActive)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Timer reset");
            }
        }

        private void ResetTimer(object sender, EventArgs e)
        {
            if (!_isCountdownActive)
            {
                Reset();
            }
        }

        private void InactivityTimer_Tick(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Inactivity detected");
            _inactivityTimer.Stop();
            _isCountdownActive = true;
            _countdownSeconds = 20;
            ShowCountdownDialog();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _countdownSeconds--;
            System.Diagnostics.Debug.WriteLine($"SessionTimeoutManager: {_countdownSeconds}s remaining");

            if (_countdownDialog != null && _countdownDialog.IsVisible)
            {
                _countdownDialog.UpdateCountdown(_countdownSeconds);
            }

            if (_countdownSeconds <= 0)
            {
                HandleTimeout();
            }
        }

        private void ShowCountdownDialog()
        {
            if (_countdownDialog != null)
            {
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Dialog already exists, skipping");
                return;
            }

            _parentWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_parentWindow == null || !_parentWindow.IsLoaded)
                    {
                        System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Parent window invalid");
                        return;
                    }

                    _countdownDialog = new CountdownDialog(_countdownSeconds);
                    _countdownDialog.Owner = _parentWindow;
                    _countdownDialog.StayHereClicked += OnStayHereClicked;
                    _countdownDialog.GoBackClicked += OnGoBackClicked;
                    _countdownDialog.Closed += OnDialogClosed;

                    _countdownTimer.Start();
                    _countdownDialog.Show();

                    System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Countdown dialog shown");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SessionTimeoutManager: Error showing dialog - {ex.Message}");
                    CleanupDialog();
                }
            });
        }

        private void OnStayHereClicked(object sender, EventArgs e)
        {
            if (_isHandlingUserAction) return;
            _isHandlingUserAction = true;

            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: User clicked 'Stay Here'");

            _countdownTimer.Stop();
            _isCountdownActive = false;

            CloseCountdownDialogImmediately();

            _inactivityTimer.Stop();
            _inactivityTimer.Start();

            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: User stays, timer restarted");

            _isHandlingUserAction = false;
        }

        private void OnGoBackClicked(object sender, EventArgs e)
        {
            if (_isHandlingUserAction) return;
            _isHandlingUserAction = true;

            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: User clicked 'Go Back'");

            _countdownTimer.Stop();
            _isCountdownActive = false;

            CloseCountdownDialogImmediately();

            // Navigate first, THEN show warning, THEN restart timer
            NavigateAndRestart();

            _isHandlingUserAction = false;
        }

        private void OnDialogClosed(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Dialog closed");
            _countdownTimer.Stop();
            _isCountdownActive = false;
            CleanupDialog();
        }

        private void HandleTimeout()
        {
            System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Timeout reached");

            _countdownTimer.Stop();
            _isCountdownActive = false;

            CloseCountdownDialogImmediately();

            // Navigate first, THEN show warning, THEN restart timer
            NavigateAndRestart();
        }

        private void NavigateAndRestart()
        {
            try
            {
                // Step 1: Navigate FIRST
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Step 1 - Navigating to CDD");
                _onTimeout?.Invoke();

                // Step 2: THEN show warning (after navigation completes)
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Step 2 - Showing warning");
                CustomDialog.ShowWarning(
                    "⏱️ Session Expired",
                    "Your session has expired due to inactivity.\n\n" +
                    "Please begin your transaction again.",
                    "OK, Got it");

                // Step 3: THEN restart timer (after warning is dismissed)
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Step 3 - Restarting timer");
                _inactivityTimer.Stop();
                _inactivityTimer.Start();

                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Navigation complete, timer restarted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionTimeoutManager: Error in NavigateAndRestart - {ex.Message}");
            }
        }

        private void CloseCountdownDialogImmediately()
        {
            if (_countdownDialog == null) return;

            try
            {
                _countdownDialog.StayHereClicked -= OnStayHereClicked;
                _countdownDialog.GoBackClicked -= OnGoBackClicked;
                _countdownDialog.Closed -= OnDialogClosed;

                if (_countdownDialog.IsVisible)
                {
                    _countdownDialog.Close();
                }

                _countdownDialog = null;
                System.Diagnostics.Debug.WriteLine("SessionTimeoutManager: Dialog closed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SessionTimeoutManager: Error closing dialog - {ex.Message}");
                _countdownDialog = null;
            }
        }

        private void CleanupDialog()
        {
            if (_countdownDialog != null)
            {
                _countdownDialog.StayHereClicked -= OnStayHereClicked;
                _countdownDialog.GoBackClicked -= OnGoBackClicked;
                _countdownDialog.Closed -= OnDialogClosed;
                _countdownDialog = null;
            }
        }
    }
}