using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Ekyc;
using OmniKiosk.Wpf.Services.MoneyExchange;
using System;
using System.IO;
using System.Threading;
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

        // Remote eKYC face-match client - used only for first-time customers.
        private readonly EkycFaceMatchClient _ekyc = new();
        private Task<(bool ok, string? journeyId, string? error)>? _journeyTask;

        // Dedicated, message-pumping STA thread for the camera SDK. See the
        // notes from the lag investigation: CameraConfig/xmlSamples_IR_ON.txt
        // runs with useCameraThread=1 now, but every SDK call still goes
        // through this one dedicated thread for consistency - never the
        // WPF UI thread, never a bare ThreadPool task.
        private Thread? _sdkThread;
        private Dispatcher? _sdkDispatcher;

        private const int CALLBACK_EVENT_SUCC = 100;
        private const int CALLBACK_EVENT_FAIL = -100;
        private const int CALLBACK_EVENT_TIMEOUT = -101;
        private const int IMAGE_TYPE_CROP_VIS = 4;
        private const int LocalMatchThreshold = 75;

        public FaceVerificationStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Hdr.Text = L10n.T("Mx_FaceVerify", "Face Verification");
            SubtitleText.Text = L10n.T("Mx_FaceVerifySubtitle", "Please look at the camera to confirm your identity.");
            WelcomeTitle.Text = L10n.T("Mx_IdentityVerified", "Identity Verified");
            WelcomeName.Text = _ctl.State.Customer?.FullName ?? "";
            FailTitle.Text = L10n.T("Mx_VerificationFailed", "Verification Failed");
            FailBody.Text = L10n.T("Mx_VerificationFailedBody", "We couldn't confirm your identity. Please proceed to the counter for manual assistance.");
            FailAcknowledgeButton.Content = L10n.T("Mx_ExitTransaction", "Exit Transaction");
            BtnSkip.Content = L10n.T("Mx_SkipContinue", "Skip & Continue ➔");
            BtnSkipBack.Content = L10n.T("Mx_Back", "Back");

            ShowBranchInstructions();
            await StartCameraAndDetectAsync();
        }

        // The one piece of UI that genuinely differs between the two paths:
        // a returning customer sees a quick "we recognize you" framing, a
        // first-time customer sees what eKYC actually involves.
        private void ShowBranchInstructions()
        {
            if (_ctl.State.IsExistingCustomer)
            {
                InstructionsIcon.Text = "👋";
                InstructionsTitle.Text = L10n.T("Mx_WelcomeBackTitle", "Welcome back!");
                InstructionsBody.Text = L10n.T("Mx_WelcomeBackBody", "We already have your details on file. Just look at the camera to confirm it's you - this only takes a moment.");
            }
            else
            {
                InstructionsIcon.Text = "🔒";
                InstructionsTitle.Text = L10n.T("Mx_FirstTimeTitle", "First time here?");
                InstructionsBody.Text = L10n.T("Mx_FirstTimeBody", "Since this is your first visit, we'll verify your identity with our verification partner. Please look directly at the camera and hold still - this takes a few seconds longer.");
            }
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
                HintText.Text = L10n.T("Mx_RetryOrSkip", "Please retry, or skip to continue.");
                BtnSkip.Visibility = Visibility.Visible;
            });
        }

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
            StatusText.Text = L10n.T("Mx_InitCamera", "Initializing Camera…");
            HintText.Text = L10n.T("Mx_AlignFace", "Align your face in the frame");

            // Only new customers need a journey - kick it off in parallel with
            // camera bring-up so neither one waits on the other.
            if (!_ctl.State.IsExistingCustomer)
                _journeyTask = _ekyc.CreateJourneyIdAsync(_ctl.State.Customer?.IdNo);

            if (VisHost.HostHandle == IntPtr.Zero || NirHost.HostHandle == IntPtr.Zero)
            {
                ShowSkipOption(L10n.T("Mx_CameraHandlesMissing", "❌ Camera hardware render handles not found."));
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
                StatusText.Text = L10n.T("Mx_Detecting", "Detecting…");
                HintText.Text = L10n.T("Mx_LookStraight", "Please look straight");
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
                    StatusText.Text = L10n.T("Mx_CaptureSuccess", "Capture success ✅");
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
                    ShowSkipOption(L10n.T("Mx_LivenessFailed", "Liveness check failed ❌ Please try again."));
                }
                else if (eventId == CALLBACK_EVENT_TIMEOUT)
                {
                    _handledThisSession = false;
                    ShowSkipOption(L10n.T("Mx_DetectTimeout", "Timeout ⏳ No face detected."));
                }
            }));
        }

        // Single entry point after a good capture - routes to whichever
        // verification method matches this customer's IsExistingCustomer flag.
        private async Task HandleCaptureAsync(byte[] faceJpg)
        {
            try
            {
                var cust = _ctl.State.Customer;
                if (cust == null || string.IsNullOrWhiteSpace(cust.FaceImageBase64))
                {
                    ShowSkipOption(L10n.T("Mx_NoDocPhoto", "❌ No document photo to compare against."));
                    return;
                }

                if (_ctl.State.IsExistingCustomer)
                    await HandleExistingCustomerMatchAsync(cust, faceJpg);
                else
                    await HandleNewCustomerEkycAsync(cust, faceJpg);
            }
            catch (Exception ex)
            {
                _handledThisSession = false;
                ShowSkipOption("❌ Verification error: " + ex.Message);
            }
        }

        // Fast path: compare the live capture against this customer's cached
        // biometric feature (or, failing that, their stored ID photo) using
        // the local TaiSDK engine. No network call at all.
        private async Task HandleExistingCustomerMatchAsync(Models.MoneyExchange.CustomerProfile cust, byte[] faceJpg)
        {
            var engine = GlobalHardwareManager.FaceEngine?.Current;
            if (engine == null || !engine.Info.IsAvailable)
            {
                ShowSkipOption(L10n.T("Mx_LocalEngineUnavailable", "❌ Local face engine unavailable: ") + (engine?.Info.Message ?? "not loaded"));
                return;
            }

            StatusText.Text = L10n.T("Mx_ComparingLocal", "Comparing with your saved profile…");

            byte[]? storedFeature = null;
            if (!string.IsNullOrWhiteSpace(cust.FaceFeatureBase64))
            {
                storedFeature = Convert.FromBase64String(cust.FaceFeatureBase64);
            }
            else if (!string.IsNullOrWhiteSpace(cust.FaceImageBase64))
            {
                var storedImage = Convert.FromBase64String(cust.FaceImageBase64);
                if (!engine.TryExtractFeature(storedImage, out storedFeature, out var extractErr) || storedFeature == null)
                {
                    ShowSkipOption("❌ " + L10n.T("Mx_StoredProfileError", "Could not read stored profile: ") + extractErr);
                    return;
                }
            }

            if (storedFeature == null)
            {
                ShowSkipOption("❌ " + L10n.T("Mx_NoReferencePhoto", "No reference photo on file."));
                return;
            }

            if (!engine.TryExtractFeature(faceJpg, out var liveFeature, out var liveErr) || liveFeature == null)
            {
                ShowSkipOption("❌ " + L10n.T("Mx_LiveExtractError", "Could not process live photo: ") + liveErr);
                return;
            }

            if (!engine.TryCompare(liveFeature, storedFeature, out var score, out var cmpErr))
            {
                ShowSkipOption("❌ " + L10n.T("Mx_CompareError", "Comparison failed: ") + cmpErr);
                return;
            }

            bool matched = score >= LocalMatchThreshold;

            if (matched)
            {
                // Refresh the cached feature with today's capture so it stays current.
                _ctl.SaveFace(Convert.ToBase64String(liveFeature), Convert.ToBase64String(faceJpg));

                StatusText.Text = $"{L10n.T("Mx_Matched", "Matched ✅")} (score {score})";
                _ctl.State.FaceVerified = true;
                await ShowWelcomeAndNext();
            }
            else
            {
                StatusText.Text = $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} (score {score})";
                _ctl.State.FaceVerified = false;
                FailPopup.IsOpen = true;
            }
        }

        // First-time path: remote Innov8tif eKYC, exactly as built and tested
        // earlier. On success, also extracts a local feature from the same
        // live photo so this customer gets the fast path next visit.
        private async Task HandleNewCustomerEkycAsync(Models.MoneyExchange.CustomerProfile cust, byte[] faceJpg)
        {
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
                    ShowSkipOption("❌ " + L10n.T("Mx_EkycUnavailable", "eKYC service unavailable: ") + (err ?? "could not create journey"));
                    return;
                }
                journeyId = id;
            }

            StatusText.Text = L10n.T("Mx_VerifyingEkyc", "Verifying with eKYC service…");
            HintText.Text = L10n.T("Mx_TakesFewSeconds", "This can take a few seconds");

            string liveBase64 = Convert.ToBase64String(faceJpg);
            var outcome = await _ekyc.MatchFaceAsync(journeyId!, cust.FaceImageBase64, liveBase64);

            if (!outcome.CallSucceeded)
            {
                ShowSkipOption("❌ " + L10n.T("Mx_EkycServiceError", "eKYC service error: ") + outcome.ErrorMessage);
                return;
            }

            string scoreLabel = outcome.ScorePercent.HasValue ? $"{outcome.ScorePercent.Value:0.#}%" : "n/a";

            if (outcome.Matched)
            {
                // Seed the local cache from today's live photo, purely so next
                // visit can use the fast local path instead of eKYC again.
                var engine = GlobalHardwareManager.FaceEngine?.Current;
                if (engine != null && engine.Info.IsAvailable &&
                    engine.TryExtractFeature(faceJpg, out var localFeature, out _) && localFeature != null)
                {
                    _ctl.SaveFace(Convert.ToBase64String(localFeature), liveBase64);
                }

                StatusText.Text = $"{L10n.T("Mx_Matched", "Matched ✅")} (score {scoreLabel})";
                _ctl.State.FaceVerified = true;
                await ShowWelcomeAndNext();
            }
            else
            {
                StatusText.Text = outcome.FriendlyMessage != null
                    ? $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} — {outcome.FriendlyMessage}"
                    : $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} (score {scoreLabel})";
                _ctl.State.FaceVerified = false;
                FailPopup.IsOpen = true;
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
