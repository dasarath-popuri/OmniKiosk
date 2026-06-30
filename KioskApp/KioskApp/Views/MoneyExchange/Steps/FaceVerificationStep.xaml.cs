using OmniKiosk.Wpf.Sdk.Face;
using OmniKiosk.Wpf.Services;
using OmniKiosk.Wpf.Services.MoneyExchange;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

        private readonly FaceEngineManager _engine = GlobalHardwareManager.FaceEngine;

        private const int MatchThreshold = 75;
        private const int CALLBACK_EVENT_SUCC = 100;
        private const int CALLBACK_EVENT_TIMEOUT = -101;
        private const int IMAGE_TYPE_CROP_VIS = 4;

        public FaceVerificationStep(MoneyExchangeFlowController ctl)
        {
            InitializeComponent();
            _ctl = ctl;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            WelcomeName.Text = _ctl.State.Customer?.FullName ?? "";
            StartCameraAndDetect();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            WelcomePopup.IsOpen = false; FailPopup.IsOpen = false;
            try
            {
                if (_opened)
                {
                    EcFaceCamSdkHelper.ECF_Stop();
                    EcFaceCamSdkHelper.ECF_Close();
                    _opened = false;
                }
            }
            catch (Exception ex)
            {
                // Write to console since UI is unloading
                Console.WriteLine("Camera Stop Error: " + ex.Message);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            _ctl.State.FaceVerified = true;
            NextRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Retry_Click(object sender, RoutedEventArgs e) => StartDetect();

        private void ShowSkipOption(string msg)
        {
            Dispatcher.Invoke(() => {
                StatusText.Text = msg;
                HintText.Text = "Please retry, or skip to continue.";
                BtnSkip.Visibility = Visibility.Visible;
            });
        }

        private void StartCameraAndDetect()
        {
            try
            {
                StatusText.Text = "Initializing Camera…"; HintText.Text = "Align your face in the frame";
                _cb ??= new EcFaceCamSdkHelper.CallbackDelegate(OnSdkEvent);
                EcFaceCamSdkHelper.ECF_SetCallBack(_cb, IntPtr.Zero);

                if (VisHost.HostHandle == IntPtr.Zero || NirHost.HostHandle == IntPtr.Zero)
                {
                    ShowSkipOption("❌ Camera hardware render handles not found.");
                    return;
                }

                EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(0, VisHost.HostHandle, 0, 0, 0, 0);
                EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(1, NirHost.HostHandle, 0, 0, 0, 0);
                var paramsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraConfig", "xmlSamples_IR_ON.txt");
                var ret = EcFaceCamSdkHelper.ECF_Open(File.Exists(paramsPath) ? File.ReadAllText(paramsPath) : "");

                if (ret == 0) { _opened = true; StartDetect(); }
                else { ShowSkipOption($"❌ Camera Init failed (Code {ret})."); }
            }
            catch (Exception ex) { ShowSkipOption("❌ Camera Exception: " + ex.Message); }
        }

        private void StartDetect()
        {
            if (!_opened) return;
            WelcomePopup.IsOpen = false; FailPopup.IsOpen = false; _handledThisSession = false;
            BtnSkip.Visibility = Visibility.Collapsed;

            try
            {
                EcFaceCamSdkHelper.ECF_StartDetectAsyn();
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
                    _handledThisSession = true; StatusText.Text = "Capture success ✅";
                    var faceJpg = TryGetCapturedFaceJpeg();

                    if (faceJpg == null || faceJpg.Length == 0)
                    {
                        _handledThisSession = false; ShowSkipOption("❌ Could not read camera frame."); return;
                    }

                    _ctl.State.LiveFaceImageBase64 = Convert.ToBase64String(faceJpg);
                    var eng = _engine.Current;

                    if (eng != null && eng.Info.IsAvailable && eng.TryExtractFeature(faceJpg, out var liveFeature, out _))
                    {
                        var cust = _ctl.State.Customer;
                        if (cust != null && !string.IsNullOrWhiteSpace(cust.FaceImageBase64))
                        {
                            if (eng.TryExtractFeature(Convert.FromBase64String(cust.FaceImageBase64), out var passFeature, out _))
                            {
                                if (eng.TryCompare(liveFeature!, passFeature, out var score, out _))
                                {
                                    if (score >= MatchThreshold)
                                    {
                                        StatusText.Text = $"Matched ✅ (score {score})"; _ctl.State.FaceVerified = true; _ = ShowWelcomeAndNext(); return;
                                    }
                                    else
                                    {
                                        StatusText.Text = $"Mismatch ❌ (score {score})"; _ctl.State.FaceVerified = false; FailPopup.IsOpen = true; return;
                                    }
                                }
                            }
                            else { ShowSkipOption("❌ Document photo cannot be analyzed."); return; }
                        }
                        else { ShowSkipOption("❌ No document photo to compare against."); return; }
                    }
                    else { ShowSkipOption("❌ Could not extract facial features."); return; }
                }
                else if (eventId == CALLBACK_EVENT_TIMEOUT)
                {
                    _handledThisSession = false; ShowSkipOption("Timeout ⏳ No face detected.");
                }
            }));
        }

        private async Task ShowWelcomeAndNext() { WelcomePopup.IsOpen = true; await Task.Delay(2500); NextRequested?.Invoke(this, EventArgs.Empty); }
        private void FailAcknowledge_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        private byte[]? TryGetCapturedFaceJpeg()
        {
            try
            {
                int len = 0;
                if (EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, null, ref len) != 0 || len <= 0) return null;
                var buf = new byte[len];
                if (EcFaceCamSdkHelper.ECF_GetImageData(IMAGE_TYPE_CROP_VIS, buf, ref len) != 0 || len <= 0) return null;
                if (buf.Length != len) Array.Resize(ref buf, len);
                return buf;
            }
            catch (Exception ex)
            {
                ShowSkipOption("❌ Frame Extraction Error: " + ex.Message);
                return null;
            }
        }
    }
}