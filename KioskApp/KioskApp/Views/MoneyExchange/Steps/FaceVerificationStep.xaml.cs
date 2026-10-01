using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Ekyc;
using OmniKiosk.Wpf.Services.MoneyExchange;
using OmniKiosk.Wpf.Services.Xyreon;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OmniKiosk.Wpf.Views.MoneyExchange.Steps
{
    public partial class FaceVerificationStep : UserControl, IStepNav
    {
        private readonly MoneyExchangeFlowController _ctl;

        public event EventHandler? NextRequested;
        public event EventHandler? BackRequested;
        public event EventHandler? ExitRequested;

        // ================================================================
        // EYECOOL CALLBACK
        // ================================================================

        private EcFaceCamSdkHelper.CallbackDelegate? _cb;

        // ================================================================
        // CAMERA STATE
        // ================================================================

        private bool _opened;
        private bool _stopping;
        private bool _handledThisSession;
        private const int PreviewFps = 20;
        private static readonly long PreviewIntervalTicks =
            TimeSpan.TicksPerSecond / PreviewFps;

        private long _lastPreviewTicks;
        // ================================================================
        // PREVIEW (JPEG-pull)
        //
        // Reused across frames to avoid a fresh allocation on every
        // callback - JpegPull_net7's reference implementation allocates
        // a new byte[200*1024] per frame, which is fine for a short manual
        // test but adds GC churn on a kiosk running continuously for hours.
        // _previewBusy is a cheap reentrancy guard: if the previous frame's
        // decode+dispatch hasn't finished yet, drop this one instead of
        // queueing it up behind other UI work.
        // ================================================================

        // Preview is intentionally throttled so WPF cannot starve the native
        // Eyecool detection/liveness pipeline. The provider sample proves
        // JPEG-pull works on this kiosk; we keep the same architecture but
        // cap display work to a customer-smooth rate.
        //private long _lastPreviewDispatchMs;
        private long _previewFrameNumber;

        // ================================================================
        // eKYC
        // ================================================================

        private readonly EkycFaceMatchClient _ekyc =
            new();

        private Task<(bool ok, string? journeyId, string? error)>?
            _journeyTask;

        // ================================================================
        // SDK EVENTS
        // ================================================================

        private const int CALLBACK_EVENT_GOODFACE = 0;
        private const int CALLBACK_EVENT_NOFACE = 1;
        private const int CALLBACK_EVENT_MULTIFACE = 2;
        private const int CALLBACK_EVENT_HEADPOS = 3;
        private const int CALLBACK_EVENT_BIGFACE = 4;
        private const int CALLBACK_EVENT_SMALLFACE = 5;
        private const int CALLBACK_EVENT_MOTIVE = 7;
        private const int CALLBACK_EVENT_BRIGHT = 8;
        private const int CALLBACK_EVENT_NOTCENTER = 9;
        private const int CALLBACK_EVENT_NOTINROI = 12;

        private const int CALLBACK_EVENT_PREVIEW = 50;

        private const int CALLBACK_EVENT_SUCC = 100;
        private const int CALLBACK_EVENT_FAIL = 101;
        private const int CALLBACK_EVENT_TIMEOUT = 102;
        private int _captureInProgress;

        // Face-position analysis runs independently of Eyecool monitoring.
        // Only one TaiSDK analysis is allowed at a time and stale preview
        // frames are discarded so the customer always sees the latest image.
        private readonly byte[] _previewBuffer = new byte[200 * 1024];
        private readonly int[] _previewLengthBuffer = new int[1];
        private byte[]? _latestPreviewJpeg;
        private int _previewRenderScheduled;
        private long _lastFaceAnalysisMs;
        private int _faceAnalysisRunning;
        private int _goodFaceSamples;

        private const int FaceAnalysisIntervalMs = 300;
        private const int RequiredGoodFaceSamples = 3;
        private const int CameraWidth = 640;
        private const int CameraHeight = 480;

        private enum FaceGuidanceState
        {
            NoFace,
            MultipleFaces,
            MoveCloser,
            MoveBack,
            CenterFace,
            Good
        }
        //private const int CALLBACK_EVENT_MOTIVE = 7;

        // ================================================================
        // IMAGE
        // ================================================================

        private const int IMAGE_TYPE_CROP_VIS = 4;

        private const int IMAGE_TYPE_VIS = 0;

        // ================================================================
        // LOCAL MATCH
        // ================================================================

        private const int LocalMatchThreshold = 75;

        // ================================================================
        // CONSTRUCTOR
        // ================================================================

        public FaceVerificationStep(
            MoneyExchangeFlowController ctl)
        {
            InitializeComponent();

            _ctl = ctl;
        }

        // ================================================================
        // LOADED
        // ================================================================

        private async void UserControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Hdr.Text =
                L10n.T(
                    "Mx_FaceVerify",
                    "Face Verification");

            SubtitleText.Text =
                L10n.T(
                    "Mx_FaceVerifySubtitle",
                    "Please look at the camera to confirm your identity.");

            WelcomeTitle.Text =
                L10n.T(
                    "Mx_IdentityVerified",
                    "Identity Verified");

            WelcomeName.Text =
                _ctl.State.Customer?.FullName ?? "";

            FailTitle.Text =
                L10n.T(
                    "Mx_VerificationFailed",
                    "Verification Failed");

            FailBody.Text =
                L10n.T(
                    "Mx_VerificationFailedBody",
                    "We couldn't confirm your identity. Please proceed to the counter for manual assistance.");

            FailAcknowledgeButton.Content =
                L10n.T(
                    "Mx_ExitTransaction",
                    "Exit Transaction");

            BtnSkip.Content =
                L10n.T(
                    "Mx_ExitToCounter",
                    "Exit Transaction");

            BtnSkipBack.Content =
                L10n.T(
                    "Mx_Back",
                    "Back");

            BtnRetry.Content =
                L10n.T(
                    "Mx_Retry",
                    "Retry");

            ShowBranchInstructions();

            // ------------------------------------------------------------
            // Start eKYC journey while camera starts.
            // This doesn't touch the native preview.
            // ------------------------------------------------------------

            if (!_ctl.State.IsExistingCustomer)
            {
                // Reuse the journey CustomerDetailsStep already created
                // during document verification (OkayID/OkayDoc), per
                // Innov8tif's own guidance that one JourneyId should be
                // used for the whole eKYC flow. Only create a new one here
                // if, for some reason, that step never set it (e.g. MyKad,
                // which has no document-image step to create one from yet).
                if (!string.IsNullOrWhiteSpace(_ctl.State.EkycJourneyId))
                {
                    _journeyTask =
                        Task.FromResult<(bool ok, string? journeyId, string? error)>(
                            (true, _ctl.State.EkycJourneyId, null));
                }
                else
                {
                    _journeyTask =
                        _ekyc.CreateJourneyIdAsync(
                            _ctl.State.Customer?.IdNo);
                }
            }
            SetCameraSideLights(true);

            await StartCameraAndMonitorAsync();
        }

        // ================================================================
        // BRANCH INSTRUCTIONS
        // ================================================================

        private void ShowBranchInstructions()
        {
            if (_ctl.State.IsExistingCustomer)
            {
                InstructionsIcon.Text = "👋";

                InstructionsTitle.Text =
                    L10n.T(
                        "Mx_WelcomeBackTitle",
                        "Welcome back!");

                InstructionsBody.Text =
                    L10n.T(
                        "Mx_WelcomeBackBody",
                        "We already have your details on file. Just look at the camera to confirm it's you - this only takes a moment.");
            }
            else
            {
                InstructionsIcon.Text = "🔒";

                InstructionsTitle.Text =
                    L10n.T(
                        "Mx_FirstTimeTitle",
                        "First time here?");

                InstructionsBody.Text =
                    L10n.T(
                        "Mx_FirstTimeBody",
                        "Since this is your first visit, we'll verify your identity with our verification partner. Please look directly at the camera and hold still - this takes a few seconds longer.");
            }
        }

        // ================================================================
        // UNLOADED
        // ================================================================

        private void UserControl_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            WelcomePopup.IsOpen = false;

            FailPopup.IsOpen = false;

            _stopping = true;

            StopCamera();
        }
        private void SetCameraSideLights(
    bool enabled)
        {
            var io =
                GlobalHardwareManager.XyreonIo;

            if (io == null)
                return;

            _ = io.SetOutputAsync(
                KioskOutput.CameraSideLights,
                enabled);
        }

        // ================================================================
        // CAMERA STOP
        // ================================================================

        private void StopCamera()
        {
            SetCameraSideLights(false);
            if (!_opened)
                return;

            // Clear the last displayed frame so a stale image isn't left
            // on screen between sessions (e.g. Retry, or navigating away).
            VisImage.Source = null;
            Interlocked.Exchange(ref _latestPreviewJpeg, null);
            Interlocked.Exchange(ref _goodFaceSamples, 0);

            try
            {
                // Safe even when no monitor/detection was started.
                EcFaceCamSdkHelper.ECF_Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] ECF_Stop: " +
                    ex.Message);
            }

            try
            {
                EcFaceCamSdkHelper.ECF_Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] ECF_Close: " +
                    ex.Message);
            }

            _opened = false;
            Interlocked.Exchange(ref _lastPreviewTicks, 0);
        }

        // ================================================================
        // BACK
        // ================================================================

        private void Back_Click(
            object sender,
            RoutedEventArgs e)
        {
            BackRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // ================================================================
        // ================================================================
        // EXIT (was SKIP - this used to mark FaceVerified=true and let a
        // customer continue with zero actual verification on any hardware
        // or eKYC failure, real or clicked at will. That is not something
        // a kiosk dispensing real cash can allow, mock/demo use or not -
        // an unverified person could receive cash. Now mirrors
        // FailAcknowledge_Click exactly: exits the transaction and sends
        // the customer to a staff member, the same as every other
        // "cannot proceed at this kiosk" outcome in this flow (sanctions
        // match, document authenticity failure, face mismatch).
        // ================================================================

        private void Skip_Click(
            object sender,
            RoutedEventArgs e)
        {
            ExitRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // ================================================================
        // RETRY
        // ================================================================
        private async void Retry_Click(
    object sender,
    RoutedEventArgs e)
        {
            WelcomePopup.IsOpen = false;
            FailPopup.IsOpen = false;

            _handledThisSession = false;

            Interlocked.Exchange(
                ref _captureInProgress,
                0);

            _goodFaceSamples = 0;
            Interlocked.Exchange(ref _lastFaceAnalysisMs, 0);
            Interlocked.Exchange(ref _faceAnalysisRunning, 0);

            StopCamera();

            await Task.Delay(150);

            SetCameraSideLights(true);

            await StartCameraAndMonitorAsync();
        }

        // ================================================================
        // CAMERA START
        //
        // Video is generated by WPF: CALLBACK_EVENT_PREVIEW pulls each
        // frame via ECF_CopyFrameWithAlpha and displays it in VisImage.
        // See OnSdkEvent / HandlePreviewFrame below.
        // ================================================================

        private async Task StartCameraAndMonitorAsync()
        {
            StatusText.Text =
                L10n.T(
                    "Mx_InitCamera",
                    "Initializing Camera…");

            HintText.Text =
                L10n.T(
                    "Mx_AlignFace",
                    "Align your face in the frame");

            BtnSkip.Visibility =
                Visibility.Collapsed;

            _stopping = false;
            Interlocked.Exchange(ref _lastPreviewTicks, 0);
            Interlocked.Exchange(ref _previewFrameNumber, 0);
            Interlocked.Exchange(ref _previewRenderScheduled, 0);
            Interlocked.Exchange(ref _lastFaceAnalysisMs, 0);
            Interlocked.Exchange(ref _faceAnalysisRunning, 0);
            Interlocked.Exchange(ref _goodFaceSamples, 0);
            Interlocked.Exchange(ref _latestPreviewJpeg, null);
            VisImage.Source = null;
            try
            {
                // --------------------------------------------------------
                // SDK initialization
                // --------------------------------------------------------

                int initRet =
                    EcFaceCamSdkHelper
                        .EnsureInitialized();

                if (initRet != 0)
                {
                    ShowSkipOption(
                        $"❌ Camera Init failed (Code {initRet}).");

                    return;
                }

                // --------------------------------------------------------
                // Keep callback alive
                // --------------------------------------------------------

                _cb ??=
                    new EcFaceCamSdkHelper.CallbackDelegate(
                        OnSdkEvent);

                // --------------------------------------------------------
                // Callback BEFORE ECF_Open
                // --------------------------------------------------------

                int callbackRet =
                    EcFaceCamSdkHelper.ECF_SetCallBack(
                        _cb,
                        IntPtr.Zero);

                if (callbackRet != 0)
                {
                    ShowSkipOption(
                        $"❌ Callback setup failed (Code {callbackRet}).");

                    return;
                }

                // --------------------------------------------------------
                // No display window setup here.
                //
                // Video is rendered entirely by WPF via JPEG-pull:
                // CALLBACK_EVENT_PREVIEW -> ECF_CopyFrameWithAlpha ->
                // BitmapImage -> VisImage.Source. See OnSdkEvent below.
                // This matches JpegPull_net7, the only architecture
                // confirmed smooth on the actual kiosk hardware with
                // SsDuck_model.dat present - ECF_SetDisplayWindowEx
                // (native HWND hosting) did not eliminate the lag.
                // --------------------------------------------------------

                // --------------------------------------------------------
                // XML configuration
                // --------------------------------------------------------

                string paramsPath =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "CameraConfig",
                        "xmlSamples_IR_ON.txt");

                if (!File.Exists(paramsPath))
                {
                    ShowSkipOption(
                        $"❌ Camera configuration not found:\n{paramsPath}");

                    return;
                }

                string xmlParams =
                    File.ReadAllText(
                        paramsPath);

                string sdkOverridePath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "xmlParams.txt");

                if (File.Exists(sdkOverridePath))
                {
                    KioskLocalLogger.LogError(
                        "FaceVerification",
                        "Eyecool xmlParams.txt override detected at: " + sdkOverridePath);

                    // Eyecool gives xmlParams.txt precedence over ECF_Open parameters.
                    // Keep an existing override synchronized with our approved kiosk XML.
                    File.Copy(paramsPath, sdkOverridePath, true);

                    KioskLocalLogger.LogInfo(
                        "FaceVerification",
                        "Eyecool xmlParams.txt synchronized with CameraConfig/xmlSamples_IR_ON.txt.");
                }

                // --------------------------------------------------------
                // OPEN
                // --------------------------------------------------------

                int openRet =
                    EcFaceCamSdkHelper.ECF_Open(
                        xmlParams);

                if (openRet != 0)
                {
                    ShowSkipOption(
                        $"❌ Camera open failed (Code {openRet}).");

                    return;
                }

                _opened = true;

                // Give the camera a short exposure/stabilization window.
                await Task.Delay(500);

                // IMPORTANT:
                // Do NOT call ECF_StartMonitor() or ECF_StartDetectAsyn() here.
                // Both start Eyecool face-analysis work and were observed on the
                // physical kiosk to make the customer preview visibly lag.
                // Preview frames continue after ECF_Open(); TaiSDK is used only
                // on a sampled JPEG every few hundred milliseconds for geometry.
                StatusText.Text =
                    L10n.T(
                        "Mx_Detecting",
                        "Position your face");

                HintText.Text =
                    L10n.T(
                        "Mx_AlignFace",
                        "Fit your face inside the oval");

                FaceGuideText.Text =
                    "Position your face inside the oval";
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] Start camera exception:");

                Console.WriteLine(ex);

                ShowSkipOption(
                    "❌ Camera Exception: " +
                    ex.Message);
            }
        }

        // ================================================================
        // SDK CALLBACK
        //
        // CALLBACK_EVENT_PREVIEW drives the visible video via JPEG-pull -
        // this matches JpegPull_net7, the only architecture confirmed
        // smooth on the real kiosk hardware with SsDuck_model.dat present.
        // Native HWND hosting (ECF_SetDisplayWindowEx) was the previous
        // approach here and did not fix the lag.
        // ================================================================

        private void OnSdkEvent(
            int eventId,
            IntPtr context)
        {
            if (_stopping)
                return;

            // In smooth-preview mode we intentionally do not start Eyecool
            // monitor/liveness. The only SDK event needed here is PREVIEW.
            if (eventId == CALLBACK_EVENT_PREVIEW)
            {
                HandlePreviewFrame();
            }
        }

        // ================================================================
        // PREVIEW FRAME (JPEG-pull)
        //
        // Called on every CALLBACK_EVENT_PREVIEW - whatever thread the SDK
        // invokes the callback on, not the UI thread. Decode happens here,
        // off the UI thread; the resulting BitmapImage is frozen (making it
        // safely shareable across threads) before being handed to the
        // Dispatcher, so the UI thread only ever does a trivial Source
        // assignment rather than a JPEG decode.
        //
        // _previewBuffer is reused across calls rather than freshly
        // allocated per frame like JpegPull_net7's own demo code does -
        // BitmapCacheOption.OnLoad forces WPF to fully decode and cache
        // pixel data during EndInit(), so the buffer is safe to overwrite
        // on the next frame the moment EndInit() returns.
        // ================================================================
        private void HandlePreviewFrame()
        {
            if (_stopping || !_opened)
                return;

            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(ref _lastPreviewTicks);

            if (nowTicks - lastTicks < PreviewIntervalTicks)
                return;

            Interlocked.Exchange(ref _lastPreviewTicks, nowTicks);

            try
            {
                _previewLengthBuffer[0] = 0;

                int ret =
                    EcFaceCamSdkHelper.ECF_CopyFrameWithAlphaProvider(
                        IMAGE_TYPE_VIS,
                        _previewBuffer,
                        _previewLengthBuffer,
                        null);

                int length = _previewLengthBuffer[0];

                if (ret != 0 ||
                    length <= 0 ||
                    length > _previewBuffer.Length)
                {
                    return;
                }

                // One allocation for the current JPEG is unavoidable because
                // the SDK owns/reuses its source buffer. The important part is
                // that old frames are REPLACED rather than queued to WPF.
                byte[] current = new byte[length];
                Buffer.BlockCopy(
                    _previewBuffer,
                    0,
                    current,
                    0,
                    length);

                Interlocked.Increment(ref _previewFrameNumber);

                Interlocked.Exchange(
                    ref _latestPreviewJpeg,
                    current);

                ScheduleLatestPreviewRender();
                TryScheduleFaceAnalysis(current);
                TryCaptureWhenStable();
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    "Preview callback error: " + ex.Message);
            }
        }

        private void ScheduleLatestPreviewRender()
        {
            if (Interlocked.CompareExchange(
                    ref _previewRenderScheduled,
                    1,
                    0) != 0)
            {
                return;
            }

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        while (!_stopping)
                        {
                            byte[]? jpeg =
                                Interlocked.Exchange(
                                    ref _latestPreviewJpeg,
                                    null);

                            if (jpeg == null || jpeg.Length == 0)
                                break;

                            BitmapImage image;

                            using (var stream = new MemoryStream(jpeg, false))
                            {
                                image = new BitmapImage();
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                                image.StreamSource = stream;
                                image.EndInit();
                                image.Freeze();
                            }

                            await Dispatcher.InvokeAsync(
                                () =>
                                {
                                    if (IsLoaded && !_stopping)
                                    {
                                        VisImage.Source = image;
                                    }
                                },
                                DispatcherPriority.Render);
                        }
                    }
                    catch (Exception ex)
                    {
                        KioskLocalLogger.LogError(
                            "FaceVerification",
                            "Preview render error: " + ex.Message);
                    }
                    finally
                    {
                        Interlocked.Exchange(
                            ref _previewRenderScheduled,
                            0);

                        // A frame may have arrived between the last exchange
                        // and clearing the scheduled flag. Schedule once more
                        // so the newest frame is never stranded.
                        if (_latestPreviewJpeg != null && !_stopping)
                        {
                            ScheduleLatestPreviewRender();
                        }
                    }
                });
        }

        private void TryScheduleFaceAnalysis(byte[] jpeg)
        {
            if (_stopping ||
                !_opened ||
                _handledThisSession)
            {
                return;
            }

            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastFaceAnalysisMs);

            if (now - last < FaceAnalysisIntervalMs)
                return;

            if (Interlocked.CompareExchange(
                    ref _faceAnalysisRunning,
                    1,
                    0) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _lastFaceAnalysisMs, now);

            _ = Task.Run(
                () =>
                {
                    try
                    {
                        AnalyzeFacePosition(jpeg);
                    }
                    finally
                    {
                        Interlocked.Exchange(
                            ref _faceAnalysisRunning,
                            0);
                    }
                });
        }

        private void AnalyzeFacePosition(byte[] jpeg)
        {
            if (_stopping ||
                !_opened ||
                _handledThisSession)
            {
                return;
            }

            var engine =
                GlobalHardwareManager
                    .GetOrCreateFaceEngine()
                    .Current;

            if (!engine.TryDetectFaces(
                    jpeg,
                    out var faces,
                    out _))
            {
                SetFaceGuidance(
                    FaceGuidanceState.NoFace);

                return;
            }

            if (faces.Length > 1)
            {
                SetFaceGuidance(
                    FaceGuidanceState.MultipleFaces);

                return;
            }

            var face = faces[0];

            int width = face.x2 - face.x1;
            int height = face.y2 - face.y1;

            if (width <= 0 || height <= 0)
            {
                SetFaceGuidance(
                    FaceGuidanceState.NoFace);

                return;
            }

            int minSide = Math.Min(width, height);

            double centerX =
                (face.x1 + face.x2) / 2.0;

            double centerY =
                (face.y1 + face.y2) / 2.0;

            // Innov8tif notes that a minimum detected face side below about
            // 180 px can reduce selfie/liveness quality. The maximum-size and
            // centre tolerances below are kiosk guidance values for 640x480,
            // chosen to keep the entire head comfortably inside the guide.
            if (minSide < 180)
            {
                SetFaceGuidance(
                    FaceGuidanceState.MoveCloser);

                return;
            }

            if (height > 360 || width > 300)
            {
                SetFaceGuidance(
                    FaceGuidanceState.MoveBack);

                return;
            }

            if (Math.Abs(centerX - (CameraWidth / 2.0)) > 65 ||
                Math.Abs(centerY - (CameraHeight / 2.0)) > 55)
            {
                SetFaceGuidance(
                    FaceGuidanceState.CenterFace);

                return;
            }

            SetFaceGuidance(
                FaceGuidanceState.Good);
        }

        private void SetFaceGuidance(
            FaceGuidanceState state)
        {
            if (_stopping || _handledThisSession)
                return;

            if (state == FaceGuidanceState.Good)
            {
                Interlocked.Increment(ref _goodFaceSamples);
            }
            else
            {
                Interlocked.Exchange(ref _goodFaceSamples, 0);
            }

            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        if (!IsLoaded ||
                            _stopping ||
                            _handledThisSession)
                        {
                            return;
                        }

                        switch (state)
                        {
                            case FaceGuidanceState.NoFace:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.White;
                                FaceGuideText.Text =
                                    "Position your face inside the oval";
                                StatusText.Text =
                                    "Face not detected";
                                HintText.Text =
                                    "Look directly at the camera";
                                break;

                            case FaceGuidanceState.MultipleFaces:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.OrangeRed;
                                FaceGuideText.Text =
                                    "Only one person should be visible";
                                StatusText.Text =
                                    "More than one face detected";
                                HintText.Text =
                                    "Please make sure only you are in the camera";
                                break;

                            case FaceGuidanceState.MoveCloser:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.Orange;
                                FaceGuideText.Text =
                                    "Please move closer";
                                StatusText.Text =
                                    "You are too far away";
                                HintText.Text =
                                    "Move a little closer to the kiosk";
                                break;

                            case FaceGuidanceState.MoveBack:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.Orange;
                                FaceGuideText.Text =
                                    "Please move slightly back";
                                StatusText.Text =
                                    "You are too close";
                                HintText.Text =
                                    "Move a little further from the kiosk";
                                break;

                            case FaceGuidanceState.CenterFace:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.Orange;
                                FaceGuideText.Text =
                                    "Move your face into the centre";
                                StatusText.Text =
                                    "Please centre your face";
                                HintText.Text =
                                    "Keep your whole head inside the oval";
                                break;

                            case FaceGuidanceState.Good:
                                FaceGuide.Stroke =
                                    System.Windows.Media.Brushes.LimeGreen;
                                FaceGuideText.Text =
                                    "Perfect position — hold still";
                                StatusText.Text =
                                    "Perfect position";
                                HintText.Text =
                                    "Hold still while we capture your photo";
                                break;
                        }
                    }),
                DispatcherPriority.Background);
        }

        private void TryCaptureWhenStable()
        {
            if (_stopping ||
                !_opened ||
                _handledThisSession)
            {
                return;
            }

            if (Volatile.Read(ref _goodFaceSamples) <
                RequiredGoodFaceSamples)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _captureInProgress,
                    1,
                    0) != 0)
            {
                return;
            }

            CaptureGoodFace();
        }

        private void CaptureGoodFace()
        {
            if (_stopping ||
                !_opened)
            {
                Interlocked.Exchange(
                    ref _captureInProgress,
                    0);

                return;
            }

            try
            {
                // Plenty for a native 640x480 JPEG.
                byte[] buffer =
                    new byte[1024 * 1024];

                int[] length =
                    new int[1];

                int ret =
                    EcFaceCamSdkHelper
                        .ECF_SnapFrame(
                            IMAGE_TYPE_VIS,
                            buffer,
                            length);

                //if (ret != 0 ||
                //    length[0] <= 0 ||
                //    length[0] > buffer.Length)
                //{
                //    Interlocked.Exchange(
                //        ref _captureInProgress,
                //        0);

                //    return;
                //}

                if (ret != 0 ||
    length[0] <= 0 ||
    length[0] > buffer.Length)
                {
                    Interlocked.Exchange(
                        ref _captureInProgress,
                        0);

                    Interlocked.Exchange(ref _goodFaceSamples, 0);

                    Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            FaceGuide.Stroke =
                                System.Windows.Media.Brushes.Orange;

                            FaceGuideText.Text =
                                "Please hold still and try again";

                            StatusText.Text =
                                "Unable to capture photo";

                            HintText.Text =
                                "Please keep your face inside the oval";
                        }),
                        DispatcherPriority.Background);

                    return;
                }

                byte[] rawJpeg =
                    new byte[length[0]];

                Buffer.BlockCopy(
                    buffer,
                    0,
                    rawJpeg,
                    0,
                    length[0]);

                _handledThisSession =
                    true;

                Dispatcher.BeginInvoke(
                    new Action(async () =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        FaceGuide.Stroke =
                            System.Windows.Media.Brushes.LimeGreen;

                        FaceGuideText.Text =
                            "Photo captured";

                        StatusText.Text =
                            L10n.T(
                                "Mx_CaptureSuccess",
                                "Capture success ✅");

                        HintText.Text =
                            "Verifying your identity...";

                        byte[] selfie =
                            NormalizeSelfieForEkyc(
                                rawJpeg);

                        _ctl.State
                            .LiveFaceImageBase64 =
                            Convert.ToBase64String(
                                selfie);

                        // Camera is finished.
                        // Release it BEFORE TaiSDK/network work.
                        StopCamera();

                        await HandleCaptureAsync(
                            selfie);
                    }),
                    DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(
                    ref _captureInProgress,
                    0);

                KioskLocalLogger.LogError(
                    "FaceVerification",
                    "SnapFrame failed: " +
                    ex.Message);
            }
        }
        private static byte[] NormalizeSelfieForEkyc(
    byte[] sourceJpeg)
        {
            using var input =
                new MemoryStream(
                    sourceJpeg,
                    false);

            BitmapFrame frame =
                BitmapFrame.Create(
                    input,
                    BitmapCreateOptions
                        .PreservePixelFormat,
                    BitmapCacheOption
                        .OnLoad);

            if (frame.PixelWidth < 450 ||
                frame.PixelHeight < 450)
            {
                throw new InvalidOperationException(
                    $"Captured selfie resolution is too small: " +
                    $"{frame.PixelWidth}x{frame.PixelHeight}.");
            }

            int squareSize =
                Math.Min(
                    frame.PixelWidth,
                    frame.PixelHeight);

            int x =
                (frame.PixelWidth -
                 squareSize) / 2;

            int y =
                (frame.PixelHeight -
                 squareSize) / 2;

            var crop =
                new CroppedBitmap(
                    frame,
                    new Int32Rect(
                        x,
                        y,
                        squareSize,
                        squareSize));

            BitmapSource output =
                crop;

            // Native input is currently 640x480,
            // therefore centre-crop produces 480x480.
            //
            // If another camera resolution is ever used,
            // make sure the output still meets Innov8tif's
            // minimum 450x450 requirement.
            if (crop.PixelWidth != 480 ||
                crop.PixelHeight != 480)
            {
                double scaleX =
                    480.0 /
                    crop.PixelWidth;

                double scaleY =
                    480.0 /
                    crop.PixelHeight;

                output =
                    new TransformedBitmap(
                        crop,
                        new System.Windows.Media
                            .ScaleTransform(
                                scaleX,
                                scaleY));
            }

            var encoder =
                new JpegBitmapEncoder
                {
                    QualityLevel =
                        95
                };

            encoder.Frames.Add(
                BitmapFrame.Create(
                    output));

            using var outputStream =
                new MemoryStream();

            encoder.Save(
                outputStream);

            return outputStream.ToArray();
        }
        // ================================================================
        // SHOW SKIP
        // ================================================================

        private void ShowSkipOption(
            string message)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!IsLoaded)
                        return;

                    StatusText.Text =
                        message;

                    HintText.Text =
                        L10n.T(
                            "Mx_RetryOrExit",
                            "Please retry, or exit to see a staff member.");

                    BtnSkip.Visibility =
                        Visibility.Visible;
                }),
                DispatcherPriority.Background);
        }

        // ================================================================
        // CAPTURE ROUTER
        // ================================================================

        private async Task HandleCaptureAsync(
            byte[] faceJpg)
        {
            try
            {
                var cust =
                    _ctl.State.Customer;

                if (cust == null ||
                    string.IsNullOrWhiteSpace(
                        cust.FaceImageBase64))
                {
                    ShowSkipOption(
                        L10n.T(
                            "Mx_NoDocPhoto",
                            "❌ No document photo to compare against."));

                    _handledThisSession = false;

                    return;
                }

                if (_ctl.State.IsExistingCustomer)
                {
                    await HandleExistingCustomerMatchAsync(
                        cust,
                        faceJpg);
                }
                else
                {
                    await HandleNewCustomerEkycAsync(
                        cust,
                        faceJpg);
                }
            }
            catch (Exception ex)
            {
                _handledThisSession = false;

                ShowSkipOption(
                    "❌ Verification error: " +
                    ex.Message);
            }
        }

        // ================================================================
        // EXISTING CUSTOMER
        // ================================================================

        private async Task HandleExistingCustomerMatchAsync(
            Models.MoneyExchange.CustomerProfile cust,
            byte[] faceJpg)
        {
            //var engine =
            //    GlobalHardwareManager
            //        .FaceEngine?
            //        .Current;
            var engine = GlobalHardwareManager
    .GetOrCreateFaceEngine()
    .Current;

            if (engine == null ||
                !engine.Info.IsAvailable)
            {
                ShowSkipOption(
                    L10n.T(
                        "Mx_LocalEngineUnavailable",
                        "❌ Local face engine unavailable: ")
                    +
                    (engine?.Info.Message ??
                     "not loaded"));

                return;
            }

            StatusText.Text =
                L10n.T(
                    "Mx_ComparingLocal",
                    "Comparing with your saved profile…");

            byte[]? cachedFeature = null;

            if (!string.IsNullOrWhiteSpace(
                    cust.FaceFeatureBase64))
            {
                cachedFeature =
                    Convert.FromBase64String(
                        cust.FaceFeatureBase64);
            }

            byte[]? cachedImage = null;

            if (cachedFeature == null &&
                !string.IsNullOrWhiteSpace(
                    cust.FaceImageBase64))
            {
                cachedImage =
                    Convert.FromBase64String(
                        cust.FaceImageBase64);
            }

            var result =
                await Task.Run(() =>
                {
                    byte[]? storedFeature =
                        cachedFeature;

                    if (storedFeature == null &&
                        cachedImage != null)
                    {
                        if (!engine.TryExtractFeature(
                                cachedImage,
                                out storedFeature,
                                out var extractErr) ||
                            storedFeature == null)
                        {
                            return new LocalMatchResult(
                                false,
                                null,
                                -1,
                                "Could not read stored profile: " +
                                extractErr);
                        }
                    }

                    if (storedFeature == null)
                    {
                        return new LocalMatchResult(
                            false,
                            null,
                            -1,
                            "No reference photo on file.");
                    }

                    if (!engine.TryExtractFeature(
                            faceJpg,
                            out var liveFeature,
                            out var liveErr) ||
                        liveFeature == null)
                    {
                        return new LocalMatchResult(
                            false,
                            null,
                            -1,
                            "Could not process live photo: " +
                            liveErr);
                    }

                    if (!engine.TryCompare(
                            liveFeature,
                            storedFeature,
                            out var score,
                            out var cmpErr))
                    {
                        return new LocalMatchResult(
                            false,
                            null,
                            -1,
                            "Comparison failed: " +
                            cmpErr);
                    }

                    return new LocalMatchResult(
                        true,
                        liveFeature,
                        score,
                        null);
                });

            if (!result.Success)
            {
                ShowSkipOption(
                    "❌ " +
                    result.Error);

                return;
            }

            bool matched =
                result.Score >=
                LocalMatchThreshold;

            if (matched)
            {
                if (result.LiveFeature != null)
                {
                    _ctl.SaveFace(
                        Convert.ToBase64String(
                            result.LiveFeature),
                        Convert.ToBase64String(
                            faceJpg));
                }

                StatusText.Text =
                    $"{L10n.T("Mx_Matched", "Matched ✅")} " +
                    $"(score {result.Score})";

                _ctl.State.FaceVerified =
                    true;

                await ShowWelcomeAndNext();
            }
            else
            {
                StatusText.Text =
                    $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} " +
                    $"(score {result.Score})";

                _ctl.State.FaceVerified =
                    false;

                FailPopup.IsOpen =
                    true;
            }
        }

        // ================================================================
        // NEW CUSTOMER / eKYC
        // ================================================================
        private async Task HandleNewCustomerEkycAsync(
    Models.MoneyExchange.CustomerProfile cust,
    byte[] faceJpg)
        {
            string? journeyId = _ctl.State.EkycJourneyId;

            if (string.IsNullOrWhiteSpace(journeyId) && _journeyTask != null)
            {
                var journey = await _journeyTask;

                if (journey.ok && !string.IsNullOrWhiteSpace(journey.journeyId))
                {
                    journeyId = journey.journeyId;
                    _ctl.State.EkycJourneyId = journeyId;
                }
                else
                {
                    KioskLocalLogger.LogError(
                        "FaceVerification",
                        "Background eKYC journey creation failed: " + journey.error);
                }
            }

            if (string.IsNullOrWhiteSpace(journeyId))
            {
                var journey = await _ekyc.CreateJourneyIdAsync(cust.IdNo);

                if (!journey.ok || string.IsNullOrWhiteSpace(journey.journeyId))
                {
                    ShowSkipOption(
                        "❌ " +
                        L10n.T("Mx_EkycUnavailable", "eKYC service unavailable: ") +
                        (journey.error ?? "could not create journey"));

                    return;
                }

                journeyId = journey.journeyId;
                _ctl.State.EkycJourneyId = journeyId;
            }

            if (string.IsNullOrWhiteSpace(cust.FaceImageBase64))
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    "New customer has no document portrait for OkayFace comparison.");

                CustomDialog.ShowError(
                    L10n.T("Mx_VerificationFailed", "Verification Failed"),
                    "The document photo could not be prepared for face verification. Please rescan the document.");

                ExitRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            StatusText.Text = L10n.T(
                "Mx_VerifyingEkyc",
                "Checking face and liveness...");

            HintText.Text = L10n.T(
                "Mx_TakesFewSeconds",
                "Please keep looking directly at the camera");

            string liveBase64 = Convert.ToBase64String(faceJpg);

            var outcome = await _ekyc.MatchFaceAsync(
                journeyId!,
                cust.FaceImageBase64,
                liveBase64);

            if (!outcome.CallSucceeded)
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    $"OkayFace call failed for journey {journeyId}: {outcome.ErrorMessage}");

                ShowSkipOption(
                    "❌ " +
                    L10n.T("Mx_EkycServiceError", "eKYC service error: ") +
                    outcome.ErrorMessage);

                return;
            }

            string scoreLabel = outcome.ScorePercent.HasValue
                ? $"{outcome.ScorePercent.Value:0.#}%"
                : "n/a";

            string liveLabel = outcome.LivenessProbability.HasValue
                ? $"{outcome.LivenessProbability.Value:0.00}"
                : "n/a";

            if (!outcome.Matched)
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    $"Face/liveness verification failed. Journey={journeyId}, " +
                    $"Face={scoreLabel}, Liveness={liveLabel}, Status={outcome.Status}, " +
                    $"MessageCode={outcome.MessageCode}");

                StatusText.Text = outcome.FriendlyMessage != null
                    ? $"{L10n.T("Mx_Mismatch", "Verification failed ❌")} — {outcome.FriendlyMessage}"
                    : $"{L10n.T("Mx_Mismatch", "Verification failed ❌")} — Face {scoreLabel}, Liveness {liveLabel}";

                _ctl.State.FaceVerified = false;
                if (IsRetryableFaceFailure(outcome))
                {
                    StatusText.Text =
                        outcome.FriendlyMessage
                        ?? "Please adjust your position and try again.";

                    HintText.Text =
                        "Press Retry and follow the camera guide.";

                    BtnRetry.Visibility =
                        Visibility.Visible;

                    BtnSkip.Visibility =
                        Visibility.Visible;

                    return;
                }
                FailPopup.IsOpen = true;
                return;
            }

            StatusText.Text =
                $"{L10n.T("Mx_Matched", "Face matched ✅")} " +
                $"({scoreLabel})";

            // Face image has already been captured.
            // Stop the native camera before the scorecard network call.
            StopCamera();

            StatusText.Text = L10n.T(
                "Mx_CheckingScorecard",
                "Finalizing identity verification...");

            HintText.Text = L10n.T(
                "Mx_TakesFewSeconds",
                "Please wait while we complete the final checks");

            var scorecard = await _ekyc.GetScorecardResultAsync(journeyId!);

            if (!scorecard.CallSucceeded)
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    $"Scorecard service failed for journey {journeyId}: {scorecard.ErrorMessage}");

                ShowSkipOption(
                    "❌ " +
                    L10n.T(
                        "Mx_ScorecardUnavailable",
                        "Verification service unavailable: ") +
                    scorecard.ErrorMessage);

                return;
            }

            if (scorecard.Passed != true)
            {
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    $"Scorecard rejected journey {journeyId}: " +
                    $"{scorecard.ErrorMessage}. RawJson: {scorecard.RawJson}");

                _ctl.State.FaceVerified = false;

                CustomDialog.ShowError(
                    L10n.T(
                        "Mx_ScorecardFailedTitle",
                        "Unable to Verify This Customer"),
                    L10n.T(
                        "Mx_ScorecardFailedBody",
                        "We couldn't complete identity verification for this transaction. Please proceed to the counter for assistance."));

                ExitRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            // =============================================================
            // ONLY SAVE REUSABLE BIOMETRIC AFTER THE ENTIRE eKYC PASSES
            // =============================================================

            try
            {
                //var engine = GlobalHardwareManager.FaceEngine?.Current;
                var engine = GlobalHardwareManager
    .GetOrCreateFaceEngine()
    .Current;

                if (engine != null && engine.Info.IsAvailable)
                {
                    var localFeature = await Task.Run(() =>
                    {
                        if (engine.TryExtractFeature(faceJpg, out var feature, out _) &&
                            feature != null)
                        {
                            return feature;
                        }

                        return null;
                    });

                    if (localFeature != null)
                    {
                        _ctl.SaveFace(
                            Convert.ToBase64String(localFeature),
                            liveBase64);
                    }
                    else
                    {
                        KioskLocalLogger.LogError(
                            "FaceVerification",
                            "eKYC passed but local biometric feature extraction returned no feature.");
                    }
                }
            }
            catch (Exception ex)
            {
                // The central eKYC has already passed.
                // A local cache failure must not invalidate the verified customer.
                KioskLocalLogger.LogError(
                    "FaceVerification",
                    "eKYC passed but local biometric cache save failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }

            _ctl.State.FaceVerified = true;

            KioskLocalLogger.LogInfo(
                "FaceVerification",
                $"New-customer eKYC passed. Journey={journeyId}, Face={scoreLabel}, Liveness={liveLabel}");

            await ShowWelcomeAndNext();
        }
        private bool IsRetryableFaceFailure(
    FaceMatchOutcome outcome)
        {
            string code =
                outcome.MessageCode ?? "";

            return
                code.Equals(
                    "FACE_TOO_SMALL",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "FACE_TOO_CLOSE",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "FACE_CLOSE_TO_BORDER",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "FACE_CROPPED",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "FACE_ANGLE_TOO_LARGE",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "FACE_NOT_FOUND",
                    StringComparison.OrdinalIgnoreCase) ||

                code.Equals(
                    "EYES_CLOSED",
                    StringComparison.OrdinalIgnoreCase);
        }
        //private async Task HandleNewCustomerEkycAsync(
        //    Models.MoneyExchange.CustomerProfile cust,
        //    byte[] faceJpg)
        //{
        //    string? journeyId = null;

        //    if (_journeyTask != null)
        //    {
        //        var journey =
        //            await _journeyTask;

        //        if (journey.ok)
        //        {
        //            journeyId =
        //                journey.journeyId;
        //        }
        //        else
        //        {
        //            Console.WriteLine(
        //                "eKYC journey creation failed: " +
        //                journey.error);
        //        }
        //    }

        //    if (string.IsNullOrWhiteSpace(
        //            journeyId))
        //    {
        //        var journey =
        //            await _ekyc.CreateJourneyIdAsync(
        //                cust.IdNo);

        //        if (!journey.ok ||
        //            string.IsNullOrWhiteSpace(
        //                journey.journeyId))
        //        {
        //            ShowSkipOption(
        //                "❌ " +
        //                L10n.T(
        //                    "Mx_EkycUnavailable",
        //                    "eKYC service unavailable: ")
        //                +
        //                (journey.error ??
        //                 "could not create journey"));

        //            return;
        //        }

        //        journeyId =
        //            journey.journeyId;

        //        // Save it back for Scorecard (called further below) and in
        //        // case anything else in this flow still needs it - this is
        //        // the MyKad path, where no earlier step had a document
        //        // image to create a journey from yet.
        //        _ctl.State.EkycJourneyId = journeyId;
        //    }

        //    StatusText.Text =
        //        L10n.T(
        //            "Mx_VerifyingEkyc",
        //            "Verifying with eKYC service…");

        //    HintText.Text =
        //        L10n.T(
        //            "Mx_TakesFewSeconds",
        //            "This can take a few seconds");

        //    string liveBase64 =
        //        Convert.ToBase64String(
        //            faceJpg);

        //    var outcome =
        //        await _ekyc.MatchFaceAsync(
        //            journeyId!,
        //            cust.FaceImageBase64,
        //            liveBase64);

        //    if (!outcome.CallSucceeded)
        //    {
        //        ShowSkipOption(
        //            "❌ " +
        //            L10n.T(
        //                "Mx_EkycServiceError",
        //                "eKYC service error: ")
        //            +
        //            outcome.ErrorMessage);

        //        return;
        //    }

        //    string scoreLabel =
        //        outcome.ScorePercent.HasValue
        //            ? $"{outcome.ScorePercent.Value:0.#}%"
        //            : "n/a";

        //    if (outcome.Matched)
        //    {
        //        var engine =
        //            GlobalHardwareManager
        //                .FaceEngine?
        //                .Current;

        //        if (engine != null &&
        //            engine.Info.IsAvailable)
        //        {
        //            var localFeature =
        //                await Task.Run(() =>
        //                {
        //                    if (engine.TryExtractFeature(
        //                            faceJpg,
        //                            out var feature,
        //                            out _) &&
        //                        feature != null)
        //                    {
        //                        return feature;
        //                    }

        //                    return null;
        //                });

        //            if (localFeature != null)
        //            {
        //                _ctl.SaveFace(
        //                    Convert.ToBase64String(
        //                        localFeature),
        //                    liveBase64);
        //            }
        //        }

        //        StatusText.Text =
        //            $"{L10n.T("Mx_Matched", "Matched ✅")} " +
        //            $"(score {scoreLabel})";

        //        // Camera's job is done the moment the match succeeds - the
        //        // face image needed has already been captured and used.
        //        // Stopping it HERE, before Scorecard, not after - the
        //        // Scorecard call is a network round-trip that can take
        //        // several seconds, and the camera SDK was previously left
        //        // fully running through that entire wait (StopCamera was
        //        // only ever called from UserControl_Unloaded). Holding a
        //        // native camera SDK active and idle through an unrelated
        //        // slow network call is exactly the kind of window a
        //        // threading/native-interop crash can surface in - this is
        //        // the most likely explanation for the app crash reported
        //        // after "Verifying document" ran for a few seconds. No
        //        // reason to keep hardware busy for something it has no
        //        // further part in.
        //        StopCamera();

        //        // Scorecard is now the FINAL gate, per instruction - not
        //        // OkayFace's own match result in isolation. Called once,
        //        // here, after OkayID, OkayDoc (both already run in
        //        // CustomerDetailsStep) and OkayFace/OkayLive (just above)
        //        // have all completed against the same journeyId.
        //        StatusText.Text =
        //            L10n.T(
        //                "Mx_CheckingScorecard",
        //                "Finalizing verification…");

        //        var scorecard =
        //            await _ekyc.GetScorecardResultAsync(journeyId!);

        //        if (!scorecard.CallSucceeded)
        //        {
        //            ShowSkipOption(
        //                "❌ " +
        //                L10n.T(
        //                    "Mx_ScorecardUnavailable",
        //                    "Verification service unavailable: ")
        //                + scorecard.ErrorMessage);
        //            return;
        //        }

        //        if (scorecard.Passed != true)
        //        {
        //            // Fail-safe: Passed is false OR null (could not be
        //            // determined) - either way this does not proceed. See
        //            // the HONESTY FLAG comment on ScorecardOutcome in
        //            // EkycFaceMatchClient.cs for why an ambiguous result is
        //            // treated the same as an explicit reject.
        //            KioskLocalLogger.LogError(
        //                "FaceVerification",
        //                $"Scorecard did not pass for journey {journeyId}: {scorecard.ErrorMessage}. RawJson: {scorecard.RawJson}");

        //            _ctl.State.FaceVerified = false;

        //            CustomDialog.ShowError(
        //                L10n.T("Mx_ScorecardFailedTitle", "Unable to Verify This Customer"),
        //                L10n.T("Mx_ScorecardFailedBody", "We couldn't complete verification for this transaction. Please proceed to the counter for assistance."));

        //            ExitRequested?.Invoke(this, EventArgs.Empty);
        //            return;
        //        }

        //        _ctl.State.FaceVerified =
        //            true;

        //        await ShowWelcomeAndNext();
        //    }
        //    else
        //    {
        //        StatusText.Text =
        //            outcome.FriendlyMessage != null
        //                ? $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} — " +
        //                  outcome.FriendlyMessage
        //                : $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} " +
        //                  $"(score {scoreLabel})";

        //        _ctl.State.FaceVerified =
        //            false;

        //        FailPopup.IsOpen =
        //            true;
        //    }
        //}

        // ================================================================
        // SUCCESS
        // ================================================================

        private async Task ShowWelcomeAndNext()
        {
            if (!IsLoaded)
                return;

            WelcomePopup.IsOpen =
                true;

            await Task.Delay(2500);

            if (!IsLoaded)
                return;

            NextRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // ================================================================
        // FAILURE POPUP
        // ================================================================

        private void FailAcknowledge_Click(
            object sender,
            RoutedEventArgs e)
        {
            ExitRequested?.Invoke(
                this,
                EventArgs.Empty);
        }

        // ================================================================
        // LOCAL MATCH RESULT
        // ================================================================
        private void LogCameraPerformance(string stage)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();

                long workingMb = process.WorkingSet64 / 1024 / 1024;
                long privateMb = process.PrivateMemorySize64 / 1024 / 1024;

                var gcInfo = GC.GetGCMemoryInfo();
                long availableMb = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;

                KioskLocalLogger.LogInfo(
                    "FacePerformance",
                    $"{stage} | " +
                    $"WorkingSet={workingMb}MB | " +
                    $"Private={privateMb}MB | " +
                    $"GC Available={availableMb}MB | " +
                    $"Threads={process.Threads.Count}");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError(
                    "FacePerformance",
                    "Performance logging failed: " + ex.Message);
            }
        }
        private sealed class LocalMatchResult
        {
            public bool Success { get; }

            public byte[]? LiveFeature { get; }

            public int Score { get; }

            public string? Error { get; }

            public LocalMatchResult(
                bool success,
                byte[]? liveFeature,
                int score,
                string? error)
            {
                Success = success;
                LiveFeature = liveFeature;
                Score = score;
                Error = error;
            }
        }
    }
}