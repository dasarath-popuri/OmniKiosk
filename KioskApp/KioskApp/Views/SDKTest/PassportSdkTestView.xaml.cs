using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OmniKiosk.Wpf.Sdk.Passport;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class PassportSdkTestView : UserControl
    {
        public event EventHandler? BackRequested;

        private PassportReaderService? _svc;
        private CancellationTokenSource? _cts;
        private bool _running;

        // Robustness
        private string? _lastPassportNo;
        private bool _lockedUntilRemoved;
        private int _noReadStreak;
        private DateTime _lastSuccessAt = DateTime.MinValue;

        public PassportSdkTestView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ClearUi();
            StatusText.Text = "Ready. Click Init.";
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopLoop();
            _svc?.Dispose();
            _svc = null;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Init_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var userId = KioskSettings.PassportReaderUserId;
                var libFolder = KioskSettings.PassportLibFolder;

                if (string.IsNullOrWhiteSpace(userId))
                {
                    AppendLog("ERROR: PassportReaderUserId is empty in KioskSettings.");
                    StatusText.Text = "Missing User ID.";
                    return;
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var libPath = Path.GetFullPath(Path.Combine(baseDir, libFolder));

                _svc?.Dispose();
                _svc = new PassportReaderService(userId, libPath);
                _svc.Init();

                AppendLog("Init OK.");
                StatusText.Text = "Initialized. Place passport on reader.";

                // ✅ AUTO START LOOP (like vendor demo)
                StartLoop();
            }
            catch (Exception ex)
            {
                AppendLog("Init FAIL: " + ex.Message);
                StatusText.Text = "Init failed.";
            }
        }

        private void Free_Click(object sender, RoutedEventArgs e)
        {
            StopLoop();
            _svc?.Dispose();
            _svc = null;

            HideRemoveOverlay();
            ClearUi();

            StatusText.Text = "Freed.";
            AppendLog("Free done.");
        }

        private void StartLoop()
        {
            if (_svc == null)
            {
                AppendLog("Please Init first.");
                return;
            }

            if (_running) return;

            _running = true;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoop(_cts.Token));

            AppendLog("Started auto read loop.");
        }

        private void StopLoop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _running = false;
        }

        // ✅ Production-ish loop: auto read, lock until removal, clear on removal
        private void ReadLoop(CancellationToken ct)
        {
            const int removalNoReadThreshold = 6;   // higher = stricter removal detection
            const int loopDelayMs = 200;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var online = _svc!.CheckOnlineEx();
                    if (online != 1)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = online == 2 ? "Device not connected." : "Re-init required.";
                        });

                        Thread.Sleep(600);
                        continue;
                    }

                    // If locked, we only watch for removal
                    if (_lockedUntilRemoved)
                    {
                        var okLocked = _svc.TryReadPassport(out _, out _);

                        if (!okLocked) _noReadStreak++;
                        else _noReadStreak = 0;

                        if (_noReadStreak >= removalNoReadThreshold)
                        {
                            _lockedUntilRemoved = false;
                            _noReadStreak = 0;
                            _lastPassportNo = null;

                            Dispatcher.Invoke(() =>
                            {
                                HideRemoveOverlay();
                                ClearUi();
                                StatusText.Text = "Passport removed. Ready.";
                                AppendLog("Passport removed. Ready for next.");
                            });

                            BeepRemoved();
                        }

                        Thread.Sleep(loopDelayMs);
                        continue;
                    }

                    // Normal read mode
                    var ok = _svc.TryReadPassport(out var doc, out var portraitPath);
                    if (!ok)
                    {
                        Thread.Sleep(loopDelayMs);
                        continue;
                    }

                    // Prevent empty ghost reads
                    if (string.IsNullOrWhiteSpace(doc.PassportNumber) &&
                        string.IsNullOrWhiteSpace(doc.FullName))
                    {
                        Thread.Sleep(loopDelayMs);
                        continue;
                    }

                    // Avoid re-processing same passport repeatedly within a short interval
                    var currentNo = doc.PassportNumber?.Trim();
                    if (!string.IsNullOrWhiteSpace(currentNo) &&
                        _lastPassportNo == currentNo &&
                        (DateTime.Now - _lastSuccessAt).TotalSeconds < 5)
                    {
                        Thread.Sleep(loopDelayMs);
                        continue;
                    }

                    // Success -> update UI and lock until removed
                    _lastPassportNo = currentNo;
                    _lastSuccessAt = DateTime.Now;
                    _lockedUntilRemoved = true;
                    _noReadStreak = 0;

                    Dispatcher.Invoke(() =>
                    {
                        TxtPassportNo.Text = doc.PassportNumber ?? "-";
                        TxtName.Text = doc.FullName ?? "-";
                        TxtNationality.Text = doc.Nationality ?? "-";
                        TxtDob.Text = doc.DateOfBirth ?? "-";
                        TxtExpiry.Text = doc.DateOfExpiry ?? "-";
                        TxtSex.Text = doc.Sex ?? "-";

                        StatusText.Text = "Read success ✅";
                        AppendLog($"OK: {doc.PassportNumber} | {doc.FullName}");

                        if (!string.IsNullOrWhiteSpace(portraitPath) && File.Exists(portraitPath))
                            PortraitImage.Source = LoadImage(portraitPath);

                        ShowRemoveOverlay(doc.PassportNumber);
                    });

                    BeepOk();
                    Thread.Sleep(350);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendLog("Loop error: " + ex.Message);
                        StatusText.Text = "Error in loop (check log).";
                    });

                    Thread.Sleep(700);
                }
            }
        }

        private void AppendLog(string s)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
            LogBox.ScrollToEnd();
        }

        private static BitmapImage LoadImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void ShowRemoveOverlay(string? passportNo = null)
        {
            RemoveOverlay.Visibility = Visibility.Visible;
            RemoveOverlayText.Text = passportNo == null
                ? "Please remove the passport from the reader."
                : $"Passport {passportNo} read. Please remove it from the reader.";
        }

        private void HideRemoveOverlay()
        {
            RemoveOverlay.Visibility = Visibility.Collapsed;
        }

        private void ClearUi()
        {
            TxtPassportNo.Text = "-";
            TxtName.Text = "-";
            TxtNationality.Text = "-";
            TxtDob.Text = "-";
            TxtExpiry.Text = "-";
            TxtSex.Text = "-";
            PortraitImage.Source = null;
        }

        private static void BeepOk()
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }

        private static void BeepRemoved()
        {
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }
    }
}