using KioskApp.Services.Ekyc;
using OmniKiosk.Wpf.Sdk.Face;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.MoneyExchange;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class FaceVerificationStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;
        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        private EcFaceCamSdkHelper.CallbackDelegate? _cb;
        private bool _opened;
        private bool _handledThisSession;

        private readonly EkycFaceMatchClient _ekyc = new();
        private Task<(bool ok, string? journeyId, string? error)>? _journeyTask;

        // Dedicated, message-pumping STA thread for the camera SDK.
        // CameraConfig/xmlSamples_IR_ON.txt has useCameraThread=0 / openByThread=1,
        // meaning the SDK leans on the CALLING thread to keep pumping messages
        // for smooth frame delivery. A ThreadPool thread (Task.Run) has no
        // message loop at all - that mismatch is the likely cause of the choppy
        // preview. Every SDK call now goes through this one dedicated thread,
        // kept alive for as long as this screen has the camera open.
        private Thread? _sdkThread;
        private Dispatcher? _sdkDispatcher;

        private const int CALLBACK_EVENT_SUCC = 100;
        private const int CALLBACK_EVENT_FAIL = -100;
        private const int CALLBACK_EVENT_TIMEOUT = -101;
        private const int IMAGE_TYPE_CROP_VIS = 4;

        public FaceVerificationStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            WelcomeName.Text = _ctl.State.Customer?.FullName ?? "";
            await StartCameraAndDetectAsync();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            WelcomePopup.IsOpen = false; FailPopup.IsOpen = false;

            bool wasOpened = _opened;
            _opened = false;

            var sdkDispatcher = _sdkDispatcher;
            _sdkDispatcher = null;
            _sdkThread = null;

            if (sdkDispatcher != null)
            {
                // Fire-and-forget: leaving this screen should never wait on
                // hardware teardown. The dispatcher thread always gets shut
                // down here, whether or not the camera ever finished opening.
                sdkDispatcher.InvokeAsync(() =>
                {
                    if (wasOpened)
                    {
                        try
                        {
                            EcFaceCamSdkHelper.ECF_Stop();
                            EcFaceCamSdkHelper.ECF_Close();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Camera Stop Error: " + ex.Message);
                        }
                    }
                    sdkDispatcher.InvokeShutdown();
                });
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            _ctl.State.FaceVerified = true;
            NextRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void Retry_Click(object sender, RoutedEventArgs e) => await StartDetectAsync();

        private void ShowSkipOption(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = msg;
                HintText.Text = "Please retry, or skip to continue.";
                BtnSkip.Visibility = Visibility.Visible;
            });
        }

        // Spins up one dedicated STA thread with its own running message loop
        // and hands back its Dispatcher. Reused across Retry attempts within
        // the same screen visit; torn down in UserControl_Unloaded.
        private Task<Dispatcher> EnsureSdkThreadAsync()
        {
            if (_sdkDispatcher != null) return Task.FromResult(_sdkDispatcher);

            var tcs = new TaskCompletionSource<Dispatcher>();
            _sdkThread = new Thread(() =>
            {
                _sdkDispatcher = Dispatcher.CurrentDispatcher;
                tcs.SetResult(_sdkDispatcher);
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "EcFaceCamSdkThread",
                Priority = ThreadPriority.AboveNormal
            };
            _sdkThread.SetApartmentState(ApartmentState.STA);
            _sdkThread.Start();
            return tcs.Task;
        }

        private async Task StartCameraAndDetectAsync()
        {
            StatusText.Text = "Initializing Camera…";
            HintText.Text = "Align your face in the frame";

            _journeyTask = _ekyc.CreateJourneyIdAsync(_ctl.State.Customer?.IdNo);

            if (VisHost.HostHandle == IntPtr.Zero || NirHost.HostHandle == IntPtr.Zero)
            {
                ShowSkipOption("❌ Camera hardware render handles not found.");
                return;
            }

            IntPtr visHandle = VisHost.HostHandle;
            IntPtr nirHandle = NirHost.HostHandle;

            try
            {
                _cb ??= new EcFaceCamSdkHelper.CallbackDelegate(OnSdkEvent);
                var sdkDispatcher = await EnsureSdkThreadAsync();

                int ret = await sdkDispatcher.InvokeAsync(() =>
                {
                    EcFaceCamSdkHelper.ECF_SetCallBack(_cb, IntPtr.Zero);
                    EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(0, visHandle, 0, 0, 0, 0);
                    EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(1, nirHandle, 0, 0, 0, 0);

                    var paramsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraConfig", "xmlSamples_IR_ON.txt");
                    var openParams = File.Exists(paramsPath) ? File.ReadAllText(paramsPath) : "";
                    return EcFaceCamSdkHelper.ECF_Open(openParams);
                });

                if (ret == 0) { _opened = true; await StartDetectAsync(); }
                else { ShowSkipOption($"❌ Camera Init failed (Code {ret})."); }
            }
            catch (Exception ex)
            {
                ShowSkipOption("❌ Camera Exception: " + ex.Message);
            }
        }

        private async Task StartDetectAsync()
        {
            if (!_opened || _sdkDispatcher == null) return;
            WelcomePopup.IsOpen = false; FailPopup.IsOpen = false; _handledThisSession = false;
            BtnSkip.Visibility = Visibility.Collapsed;

            try
            {
                await _sdkDispatcher.InvokeAsync(() => EcFaceCamSdkHelper.ECF_StartDetectAsyn());
                StatusText.Text = "Detecting…"; HintText.Text = "Please look straight";
            }
            catch (Exception ex)
            {
                ShowSkipOption("❌ Start Detect Error: " + ex.Message);
            }
        }

        private void OnSdkEvent(int eventId, IntPtr context)
        {
            if (_handledThisSession && eventId == CALLBACK_EVENT_SUCC) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (eventId == CALLBACK_EVENT_SUCC)
                {
                    _handledThisSession = true;
                    StatusText.Text = "Capture success ✅";
                    var faceJpg = TryGetCapturedFaceJpeg();

                    if (faceJpg == null || faceJpg.Length == 0)
                    {
                        _handledThisSession = false;
                        ShowSkipOption("❌ Could not read camera frame.");
                        return;
                    }

                    _ctl.State.LiveFaceImageBase64 = Convert.ToBase64String(faceJpg);
                    _ = HandleCaptureAsync(faceJpg);
                }
                else if (eventId == CALLBACK_EVENT_FAIL)
                {
                    _handledThisSession = false;
                    ShowSkipOption("Liveness check failed ❌ Please try again.");
                }
                else if (eventId == CALLBACK_EVENT_TIMEOUT)
                {
                    _handledThisSession = false;
                    ShowSkipOption("Timeout ⏳ No face detected.");
                }
            }));
        }

        private async Task HandleCaptureAsync(byte[] faceJpg)
        {
            try
            {
                var cust = _ctl.State.Customer;
                if (cust == null || string.IsNullOrWhiteSpace(cust.FaceImageBase64))
                {
                    ShowSkipOption("❌ No document photo to compare against.");
                    return;
                }

                string? journeyId = null;
                if (_journeyTask != null)
                {
                    var (ok, id, err) = await _journeyTask;
                    if (ok) journeyId = id;
                    else Console.WriteLine("eKYC journey creation failed: " + err);
                }

                if (string.IsNullOrWhiteSpace(journeyId))
                {
                    var (ok, id, err) = await _ekyc.CreateJourneyIdAsync(cust.IdNo);
                    if (!ok || string.IsNullOrWhiteSpace(id))
                    {
                        ShowSkipOption("❌ eKYC service unavailable: " + (err ?? "could not create journey"));
                        return;
                    }
                    journeyId = id;
                }

                StatusText.Text = "Verifying with eKYC service…";
                HintText.Text = "This can take a few seconds";

                string liveBase64 = Convert.ToBase64String(faceJpg);
                var outcome = await _ekyc.MatchFaceAsync(journeyId!, cust.FaceImageBase64, liveBase64);

                if (!outcome.CallSucceeded)
                {
                    ShowSkipOption("❌ eKYC service error: " + outcome.ErrorMessage);
                    return;
                }

                string scoreLabel = outcome.ScorePercent.HasValue ? $"{outcome.ScorePercent.Value:0.#}%" : "n/a";

                if (outcome.Matched)
                {
                    StatusText.Text = $"Matched ✅ (score {scoreLabel})";
                    _ctl.State.FaceVerified = true;
                    await ShowWelcomeAndNext();
                }
                else
                {
                    StatusText.Text = outcome.FriendlyMessage != null
                        ? $"Mismatch ❌ — {outcome.FriendlyMessage}"
                        : $"Mismatch ❌ (score {scoreLabel})";
                    _ctl.State.FaceVerified = false;
                    FailPopup.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                _handledThisSession = false;
                ShowSkipOption("❌ Verification error: " + ex.Message);
            }
        }

        private async Task ShowWelcomeAndNext() { WelcomePopup.IsOpen = true; await Task.Delay(2500); NextRequested?.Invoke(this, EventArgs.Empty); }
        private void FailAcknowledge_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        private byte[]? TryGetCapturedFaceJpeg()
        {
            if (_sdkDispatcher == null) return null;
            try
            {
                return _sdkDispatcher.Invoke(() =>
                {
                    int len = 0;
                    if (EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, null, ref len) != 0 || len <= 0) return null;
                    var buf = new byte[len];
                    if (EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, buf, ref len) != 0 || len <= 0) return null;
                    if (buf.Length != len) Array.Resize(ref buf, len);
                    return buf;
                });
            }
            catch (Exception ex)
            {
                ShowSkipOption("❌ Frame Extraction Error: " + ex.Message);
                return null;
            }
        }
    }
}