using OmniKiosk.Wpf.Controls;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.Ekyc;
using OmniKiosk.Wpf.Services.MoneyExchange;
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

        private readonly byte[] _previewBuffer = new byte[200 * 1024];
        private int _previewBusy;

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

        private const int CALLBACK_EVENT_PREVIEW = 50;

        private const int CALLBACK_EVENT_SUCC = 100;

        private const int CALLBACK_EVENT_FAIL = 101;

        private const int CALLBACK_EVENT_TIMEOUT = 102;

        private const int CALLBACK_EVENT_MOTIVE = 7;

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

            await StartCameraAndDetectAsync();
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

        // ================================================================
        // CAMERA STOP
        // ================================================================

        private void StopCamera()
        {
            if (!_opened)
                return;

            // Clear the last displayed frame so a stale image isn't left
            // on screen between sessions (e.g. Retry, or navigating away).
            VisImage.Source = null;

            try
            {
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
            await StartDetectAsync();
        }

        // ================================================================
        // CAMERA START
        //
        // Video is generated by WPF: CALLBACK_EVENT_PREVIEW pulls each
        // frame via ECF_CopyFrameWithAlpha and displays it in VisImage.
        // See OnSdkEvent / HandlePreviewFrame below.
        // ================================================================

        private async Task StartCameraAndDetectAsync()
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

                // --------------------------------------------------------
                // START ASYNCHRONOUS LIVENESS
                // --------------------------------------------------------

                await StartDetectAsync();
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
        // START DETECTION
        // ================================================================

        private Task StartDetectAsync()
        {
            if (!_opened ||
                _stopping)
            {
                return Task.CompletedTask;
            }

            WelcomePopup.IsOpen = false;
            FailPopup.IsOpen = false;

            _handledThisSession = false;

            BtnSkip.Visibility =
                Visibility.Collapsed;

            try
            {
                int ret =
                    EcFaceCamSdkHelper
                        .ECF_StartDetectAsyn();

                if (ret != 0)
                {
                    ShowSkipOption(
                        $"❌ Start detection failed (Code {ret}).");

                    return Task.CompletedTask;
                }

                StatusText.Text =
                    L10n.T(
                        "Mx_Detecting",
                        "Detecting…");

                HintText.Text =
                    L10n.T(
                        "Mx_LookStraight",
                        "Please look straight");
            }
            catch (Exception ex)
            {
                ShowSkipOption(
                    "❌ Start Detect Error: " +
                    ex.Message);
            }

            return Task.CompletedTask;
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

            // ------------------------------------------------------------
            // PREVIEW
            // ------------------------------------------------------------

            if (eventId ==
                CALLBACK_EVENT_PREVIEW)
            {
                HandlePreviewFrame();
                return;
            }

            // ------------------------------------------------------------
            // MOTION BLUR
            // ------------------------------------------------------------

            if (eventId ==
                CALLBACK_EVENT_MOTIVE)
            {
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        HintText.Text =
                            L10n.T(
                                "Mx_KeepStill",
                                "Please keep your face steady.");
                    }),
                    DispatcherPriority.Background);

                return;
            }

            // ------------------------------------------------------------
            // SUCCESS
            // ------------------------------------------------------------

            if (eventId ==
                CALLBACK_EVENT_SUCC)
            {
                if (_handledThisSession)
                    return;

                _handledThisSession = true;

                // Get image ONCE after successful detection.
                //
                // This is not preview processing.
                byte[]? faceJpg =
                    TryGetCapturedFaceJpeg();

                if (faceJpg == null ||
                    faceJpg.Length == 0)
                {
                    _handledThisSession = false;

                    Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            ShowSkipOption(
                                "❌ Could not read captured face image.");
                        }),
                        DispatcherPriority.Background);

                    return;
                }

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        StatusText.Text =
                            L10n.T(
                                "Mx_CaptureSuccess",
                                "Capture success ✅");

                        _ctl.State.LiveFaceImageBase64 =
                            Convert.ToBase64String(
                                faceJpg);

                        _ = HandleCaptureAsync(
                            faceJpg);
                    }),
                    DispatcherPriority.Background);

                return;
            }

            // ------------------------------------------------------------
            // FAIL
            // ------------------------------------------------------------

            if (eventId ==
                CALLBACK_EVENT_FAIL)
            {
                _handledThisSession = false;

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        ShowSkipOption(
                            L10n.T(
                                "Mx_LivenessFailed",
                                "Liveness check failed ❌ Please try again."));
                    }),
                    DispatcherPriority.Background);

                return;
            }

            // ------------------------------------------------------------
            // TIMEOUT
            // ------------------------------------------------------------

            if (eventId ==
                CALLBACK_EVENT_TIMEOUT)
            {
                _handledThisSession = false;

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        ShowSkipOption(
                            L10n.T(
                                "Mx_DetectTimeout",
                                "Timeout ⏳ No face detected."));
                    }),
                    DispatcherPriority.Background);

                return;
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
            // Drop this frame if the previous one hasn't finished
            // decoding/dispatching yet, rather than queueing up behind it.
            if (Interlocked.CompareExchange(
                    ref _previewBusy,
                    1,
                    0) != 0)
            {
                return;
            }

            try
            {
                int dataLen = 0;

                int ret =
                    EcFaceCamSdkHelper
                        .ECF_CopyFrameWithAlpha(
                            IMAGE_TYPE_VIS,
                            _previewBuffer,
                            ref dataLen,
                            null);

                if (ret != 0 ||
                    dataLen <= 0 ||
                    dataLen > _previewBuffer.Length)
                {
                    return;
                }

                var frame = new BitmapImage();

                frame.BeginInit();
                frame.CacheOption = BitmapCacheOption.OnLoad;
                frame.StreamSource =
                    new MemoryStream(
                        _previewBuffer,
                        0,
                        dataLen);
                frame.EndInit();
                frame.Freeze();

                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!IsLoaded ||
                            _stopping)
                        {
                            return;
                        }

                        VisImage.Source = frame;
                    }),
                    DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] Preview frame error: " +
                    ex.Message);
            }
            finally
            {
                Interlocked.Exchange(
                    ref _previewBusy,
                    0);
            }
        }

        // ================================================================
        // GET CAPTURED FACE IMAGE
        //
        // ONLY called after SUCCESS.
        //
        // NEVER called for preview frames.
        // ================================================================

        private byte[]? TryGetCapturedFaceJpeg()
        {
            try
            {
                int dataLen = 0;

                int firstRet =
                    EcFaceCamSdkHelper
                        .ECF_GetImageData(
                            IMAGE_TYPE_CROP_VIS,
                            null,
                            ref dataLen);

                if (firstRet != 0 ||
                    dataLen <= 0)
                {
                    return null;
                }

                byte[] buffer =
                    new byte[dataLen];

                int secondRet =
                    EcFaceCamSdkHelper
                        .ECF_GetImageData(
                            IMAGE_TYPE_CROP_VIS,
                            buffer,
                            ref dataLen);

                if (secondRet != 0 ||
                    dataLen <= 0)
                {
                    return null;
                }

                if (buffer.Length != dataLen)
                {
                    Array.Resize(
                        ref buffer,
                        dataLen);
                }

                return buffer;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "[Eyecool] GetImageData error:");

                Console.WriteLine(ex);

                return null;
            }
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
            var engine =
                GlobalHardwareManager
                    .FaceEngine?
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
            string? journeyId = null;

            if (_journeyTask != null)
            {
                var journey =
                    await _journeyTask;

                if (journey.ok)
                {
                    journeyId =
                        journey.journeyId;
                }
                else
                {
                    Console.WriteLine(
                        "eKYC journey creation failed: " +
                        journey.error);
                }
            }

            if (string.IsNullOrWhiteSpace(
                    journeyId))
            {
                var journey =
                    await _ekyc.CreateJourneyIdAsync(
                        cust.IdNo);

                if (!journey.ok ||
                    string.IsNullOrWhiteSpace(
                        journey.journeyId))
                {
                    ShowSkipOption(
                        "❌ " +
                        L10n.T(
                            "Mx_EkycUnavailable",
                            "eKYC service unavailable: ")
                        +
                        (journey.error ??
                         "could not create journey"));

                    return;
                }

                journeyId =
                    journey.journeyId;

                // Save it back for Scorecard (called further below) and in
                // case anything else in this flow still needs it - this is
                // the MyKad path, where no earlier step had a document
                // image to create a journey from yet.
                _ctl.State.EkycJourneyId = journeyId;
            }

            StatusText.Text =
                L10n.T(
                    "Mx_VerifyingEkyc",
                    "Verifying with eKYC service…");

            HintText.Text =
                L10n.T(
                    "Mx_TakesFewSeconds",
                    "This can take a few seconds");

            string liveBase64 =
                Convert.ToBase64String(
                    faceJpg);

            var outcome =
                await _ekyc.MatchFaceAsync(
                    journeyId!,
                    cust.FaceImageBase64,
                    liveBase64);

            if (!outcome.CallSucceeded)
            {
                ShowSkipOption(
                    "❌ " +
                    L10n.T(
                        "Mx_EkycServiceError",
                        "eKYC service error: ")
                    +
                    outcome.ErrorMessage);

                return;
            }

            string scoreLabel =
                outcome.ScorePercent.HasValue
                    ? $"{outcome.ScorePercent.Value:0.#}%"
                    : "n/a";

            if (outcome.Matched)
            {
                var engine =
                    GlobalHardwareManager
                        .FaceEngine?
                        .Current;

                if (engine != null &&
                    engine.Info.IsAvailable)
                {
                    var localFeature =
                        await Task.Run(() =>
                        {
                            if (engine.TryExtractFeature(
                                    faceJpg,
                                    out var feature,
                                    out _) &&
                                feature != null)
                            {
                                return feature;
                            }

                            return null;
                        });

                    if (localFeature != null)
                    {
                        _ctl.SaveFace(
                            Convert.ToBase64String(
                                localFeature),
                            liveBase64);
                    }
                }

                StatusText.Text =
                    $"{L10n.T("Mx_Matched", "Matched ✅")} " +
                    $"(score {scoreLabel})";

                // Camera's job is done the moment the match succeeds - the
                // face image needed has already been captured and used.
                // Stopping it HERE, before Scorecard, not after - the
                // Scorecard call is a network round-trip that can take
                // several seconds, and the camera SDK was previously left
                // fully running through that entire wait (StopCamera was
                // only ever called from UserControl_Unloaded). Holding a
                // native camera SDK active and idle through an unrelated
                // slow network call is exactly the kind of window a
                // threading/native-interop crash can surface in - this is
                // the most likely explanation for the app crash reported
                // after "Verifying document" ran for a few seconds. No
                // reason to keep hardware busy for something it has no
                // further part in.
                StopCamera();

                // Scorecard is now the FINAL gate, per instruction - not
                // OkayFace's own match result in isolation. Called once,
                // here, after OkayID, OkayDoc (both already run in
                // CustomerDetailsStep) and OkayFace/OkayLive (just above)
                // have all completed against the same journeyId.
                StatusText.Text =
                    L10n.T(
                        "Mx_CheckingScorecard",
                        "Finalizing verification…");

                var scorecard =
                    await _ekyc.GetScorecardResultAsync(journeyId!);

                if (!scorecard.CallSucceeded)
                {
                    ShowSkipOption(
                        "❌ " +
                        L10n.T(
                            "Mx_ScorecardUnavailable",
                            "Verification service unavailable: ")
                        + scorecard.ErrorMessage);
                    return;
                }

                if (scorecard.Passed != true)
                {
                    // Fail-safe: Passed is false OR null (could not be
                    // determined) - either way this does not proceed. See
                    // the HONESTY FLAG comment on ScorecardOutcome in
                    // EkycFaceMatchClient.cs for why an ambiguous result is
                    // treated the same as an explicit reject.
                    KioskLocalLogger.LogError(
                        "FaceVerification",
                        $"Scorecard did not pass for journey {journeyId}: {scorecard.ErrorMessage}. RawJson: {scorecard.RawJson}");

                    _ctl.State.FaceVerified = false;

                    CustomDialog.ShowError(
                        L10n.T("Mx_ScorecardFailedTitle", "Unable to Verify This Customer"),
                        L10n.T("Mx_ScorecardFailedBody", "We couldn't complete verification for this transaction. Please proceed to the counter for assistance."));

                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                _ctl.State.FaceVerified =
                    true;

                await ShowWelcomeAndNext();
            }
            else
            {
                StatusText.Text =
                    outcome.FriendlyMessage != null
                        ? $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} — " +
                          outcome.FriendlyMessage
                        : $"{L10n.T("Mx_Mismatch", "Mismatch ❌")} " +
                          $"(score {scoreLabel})";

                _ctl.State.FaceVerified =
                    false;

                FailPopup.IsOpen =
                    true;
            }
        }

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