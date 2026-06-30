using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Sdk.Face;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class FaceSdkTestView : UserControl
    {
        public event EventHandler? BackRequested;

        private EcFaceCamSdkHelper.CallbackDelegate? _cb;
        private bool _opened;

        private readonly CustomerStore _store;
        private List<CustomerRecord> _customers = new();

        private byte[]? _currentFeature;
        private byte[]? _currentFaceJpeg;
        private CustomerRecord? _matchedCustomer;

        // TaiSDK returns similarity 0..100
        private const int MatchThreshold = 75;

        // Eyecool callback events
        private const int CALLBACK_EVENT_SUCC = 100;
        private const int CALLBACK_EVENT_FAIL = -100;
        private const int CALLBACK_EVENT_TIMEOUT = -101;

        private const int IMAGE_TYPE_CROP_VIS = 4;

        private bool _handledThisSession = false;
        private readonly FaceEngineManager _faceEngine = new FaceEngineManager();

        public FaceSdkTestView()
        {
            InitializeComponent();

            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KioskApp",
                "face_customers.json");

            _store = new CustomerStore(dbPath);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _customers = _store.Load();
            StatusText.Text = $"Ready. Loaded {_customers.Count} customers.";
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                HideOverlays();
                _faceEngine.Dispose();

                if (_opened)
                {
                    EcFaceCamSdkHelper.ECF_Stop();
                    EcFaceCamSdkHelper.ECF_Close();
                    _opened = false;
                }
            }
            catch { }
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cb ??= new EcFaceCamSdkHelper.CallbackDelegate(OnSdkEvent);
                EcFaceCamSdkHelper.ECF_SetCallBack(_cb, IntPtr.Zero);

                if (VisHost.HostHandle == IntPtr.Zero || NirHost.HostHandle == IntPtr.Zero)
                {
                    StatusText.Text = "Video host not ready. Try again.";
                    return;
                }

                EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(0, VisHost.HostHandle, 0, 0, 0, 0);
                EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(1, NirHost.HostHandle, 0, 0, 0, 0);

                string paramsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraConfig", "xmlSamples_IR_ON.txt");
                string strParams = File.Exists(paramsPath) ? File.ReadAllText(paramsPath) : "";

                int ret = EcFaceCamSdkHelper.ECF_Open(strParams);
                if (ret != 0)
                {
                    StatusText.Text = $"ECF_Open failed: {ret}";
                    _opened = false;
                    return;
                }

                _opened = true;
                StatusText.Text = "Opened. Press Start Detect.";
                LogEvent("Camera opened.");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Open error: " + ex.Message;
                LogEvent("Open error: " + ex.Message);
            }
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_opened)
                {
                    StatusText.Text = "Open camera first.";
                    return;
                }

                HideOverlays();
                _currentFeature = null;
                _currentFaceJpeg = null;
                _matchedCustomer = null;
                _handledThisSession = false;

                int ret = EcFaceCamSdkHelper.ECF_StartDetectAsyn();
                StatusText.Text = $"Detect started (ret {ret}). Align face...";
                LogEvent("Detect started.");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Start error: " + ex.Message;
                LogEvent("Start error: " + ex.Message);
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideOverlays();
                int ret = EcFaceCamSdkHelper.ECF_Stop();
                StatusText.Text = $"Stopped (ret {ret}).";
                LogEvent("Stopped.");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Stop error: " + ex.Message;
                LogEvent("Stop error: " + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideOverlays();
                EcFaceCamSdkHelper.ECF_Stop();
                int ret = EcFaceCamSdkHelper.ECF_Close();
                _opened = false;
                StatusText.Text = $"Closed (ret {ret}).";
                LogEvent("Closed.");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Close error: " + ex.Message;
                LogEvent("Close error: " + ex.Message);
            }
        }

        // ================== CALLBACK ==================
        private void OnSdkEvent(int eventId, IntPtr context)
        {
            // prevent multiple success events spamming UI
            if (_handledThisSession && eventId == CALLBACK_EVENT_SUCC)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogEvent($"EventId={eventId}");

                if (eventId == CALLBACK_EVENT_SUCC)
                {
                    _handledThisSession = true;
                    StatusText.Text = "Liveness passed ✅ Capturing face...";

                    // stop detection while we show UI (prevents repeated callbacks)
                    try { EcFaceCamSdkHelper.ECF_Stop(); } catch { }

                    var faceJpg = TryGetCapturedFaceJpeg();
                    if (faceJpg == null || faceJpg.Length == 0)
                    {
                        StatusText.Text = "Face image empty. Try again.";
                        _handledThisSession = false;
                        return;
                    }

                    _currentFaceJpeg = faceJpg;

                    var engine = _faceEngine.Current;
                    if (!engine.Info.IsAvailable)
                    {
                        StatusText.Text = $"Face engine unavailable: {engine.Info.Message}";
                        ShowRegister(); // still allow registration (but feature will be missing)
                        return;
                    }

                    if (!engine.TryExtractFeature(faceJpg, out var feature, out var err))
                    {
                        StatusText.Text = "Feature extraction failed: " + err;
                        _handledThisSession = false;
                        return;
                    }

                    _currentFeature = feature;
                    HandleFaceFeature(feature!, faceJpg);
                    return;
                }

                if (eventId == CALLBACK_EVENT_TIMEOUT)
                {
                    StatusText.Text = "Timeout ⏳ Please try again.";
                    _handledThisSession = false;
                    return;
                }

                if (eventId == CALLBACK_EVENT_FAIL)
                {
                    StatusText.Text = "Liveness failed ❌ Please try again.";
                    _handledThisSession = false;
                    return;
                }
            }));
        }

        private byte[]? TryGetCapturedFaceJpeg()
        {
            try
            {
                int len = 0;
                int ret1 = EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, null, ref len);
                if (ret1 != 0 || len <= 0)
                {
                    LogEvent($"ECF_GetImageData len failed ret={ret1}, len={len}");
                    return null;
                }

                var buf = new byte[len];
                int ret2 = EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, buf, ref len);
                if (ret2 != 0 || len <= 0)
                {
                    LogEvent($"ECF_GetImageData data failed ret={ret2}, len={len}");
                    return null;
                }

                if (buf.Length != len)
                    Array.Resize(ref buf, len);

                return buf;
            }
            catch (Exception ex)
            {
                LogEvent("TryGetCapturedFaceJpeg error: " + ex.Message);
                return null;
            }
        }

        private void HandleFaceFeature(byte[] feature, byte[]? faceJpeg = null)
        {
            if (feature == null || feature.Length == 0)
            {
                StatusText.Text = "Feature empty. Cannot match/register.";
                ShowRegister();
                return;
            }

            _currentFeature = feature;
            _currentFaceJpeg = faceJpeg;

            var engine = _faceEngine.Current;
            int bestScore = -1;
            CustomerRecord? best = null;

            foreach (var c in _customers)
            {
                if (string.IsNullOrWhiteSpace(c.FeatureBase64)) continue;

                var stored = Convert.FromBase64String(c.FeatureBase64);
                if (!engine.TryCompare(feature, stored, out var score, out var err))
                {
                    LogEvent("Compare failed: " + err);
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            if (best != null && bestScore >= MatchThreshold)
            {
                _matchedCustomer = best;
                StatusText.Text = $"Matched: {best.Name} (score {bestScore}). Confirming...";
                ShowConfirm(best);
            }
            else
            {
                _matchedCustomer = null;
                StatusText.Text = $"No match (best {bestScore}). Registering...";
                ShowRegister();
            }
        }

        // ================== CONFIRM ==================
        private void ShowConfirm(CustomerRecord customer)
        {
            RegisterPopup.IsOpen = false;

            // show stored face (if saved)
            ConfirmFaceImage.Source = LoadBase64Image(customer.FaceImageBase64);

            ConfirmPopup.IsOpen = true;
            ConfirmTitle.Text = $"Welcome, {customer.Name}!";
            ConfirmText.Text = "Is this you?";
        }

        private void ConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            if (_matchedCustomer == null)
            {
                HideOverlays();
                return;
            }

            _matchedCustomer.LastSeenUtc = DateTime.UtcNow;
            _store.Upsert(_customers, _matchedCustomer);

            HideOverlays();

            // ✅ welcome animation 2s
            ShowWelcomeToast($"Welcome, {_matchedCustomer.Name} 👋", "Confirmed successfully");

            // restart detection after toast
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.1) };
            t.Tick += (_, __) =>
            {
                t.Stop();
                TryRestartDetect();
            };
            t.Start();
        }

        private void ConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            _matchedCustomer = null;
            ShowRegister();
        }

        // ================== REGISTER ==================
        private void ShowRegister()
        {
            ConfirmPopup.IsOpen = false;

            // show captured face preview
            RegisterFaceImage.Source = LoadBytesImage(_currentFaceJpeg);

            RegisterPopup.IsOpen = true;

            TxtName.Text = "";
            TxtIdNo.Text = "";
            TxtPhone.Text = "";
            CmbIdType.SelectedIndex = 0;
            TxtName.Focus();
        }

        private void RegisterCancel_Click(object sender, RoutedEventArgs e)
        {
            HideOverlays();
            StatusText.Text = "Registration canceled.";
            TryRestartDetect();
        }

        private void RegisterSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentFaceJpeg == null || _currentFaceJpeg.Length == 0)
            {
                StatusText.Text = "No captured face image. Try again.";
                return;
            }

            if (_currentFeature == null || _currentFeature.Length == 0)
            {
                StatusText.Text = "No face feature extracted. Cannot register. Please try again.";
                return;
            }

            var name = (TxtName.Text ?? "").Trim();
            var idNo = (TxtIdNo.Text ?? "").Trim();
            var phone = (TxtPhone.Text ?? "").Trim();
            var idType = (CmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(idNo))
            {
                StatusText.Text = "Name and ID No are required.";
                return;
            }

            var record = new CustomerRecord
            {
                Name = name,
                IdNo = idNo,
                Phone = phone,
                IdType = idType,
                FeatureBase64 = Convert.ToBase64String(_currentFeature),
                FaceImageBase64 = Convert.ToBase64String(_currentFaceJpeg),
                LastSeenUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            _store.Upsert(_customers, record);
            _customers = _store.Load();

            HideOverlays();

            ShowWelcomeToast($"Saved, {record.Name} ✅", "Registration complete");

            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.1) };
            t.Tick += (_, __) =>
            {
                t.Stop();
                TryRestartDetect();
            };
            t.Start();
        }

        private void HideOverlays()
        {
            ConfirmPopup.IsOpen = false;
            RegisterPopup.IsOpen = false;
        }

        private void TryRestartDetect()
        {
            try
            {
                if (!_opened) return;
                _handledThisSession = false;
                EcFaceCamSdkHelper.ECF_StartDetectAsyn();
                StatusText.Text = "Detecting... align face.";
                LogEvent("Detect restarted.");
            }
            catch (Exception ex)
            {
                LogEvent("Restart detect failed: " + ex.Message);
            }
        }

        // ================== WELCOME TOAST ==================
        private void ShowWelcomeToast(string title, string subtitle)
        {
            WelcomeTitle.Text = title;
            WelcomeSub.Text = subtitle;

            WelcomeToast.Visibility = Visibility.Visible;

            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, __) =>
            {
                t.Stop();
                WelcomeToast.Visibility = Visibility.Collapsed;
            };
            t.Start();
        }

        // ================== UTIL ==================
        private void LogEvent(string msg)
        {
            EventLog.AppendText($"{DateTime.Now:HH:mm:ss} {msg}\n");
            EventLog.ScrollToEnd();
        }

        private static BitmapImage? LoadBytesImage(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static BitmapImage? LoadBase64Image(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                return LoadBytesImage(Convert.FromBase64String(base64));
            }
            catch { return null; }
        }
    }
}