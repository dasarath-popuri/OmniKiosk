using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using OmniKiosk.Wpf.FaceSdk;
using OmniKiosk.Wpf.Services;

namespace OmniKiosk.Wpf.Views
{
    public partial class FaceRecognitionView : UserControl
    {
        // keep delegate alive to avoid crash
        private static EcFaceCamSdkHelper.CallbackDelegate _callback;

        private bool _cameraStarted;
        private FaceMode _mode = FaceMode.AutoRecognize;

        // Let MainWindow subscribe and handle back navigation
        //public event Action? BackRequested;
        public event EventHandler? BackRequested;

        public FaceRecognitionView()
        {
            InitializeComponent();
            _callback = new EcFaceCamSdkHelper.CallbackDelegate(OnCameraEvent);
            Loaded += FaceRecognitionView_Loaded;
            Unloaded += FaceRecognitionView_Unloaded;
        }

        private void FaceRecognitionView_Loaded(object sender, RoutedEventArgs e)
        {
            if (Resources["ScannerAnimation"] is Storyboard sb)
                sb.Begin();

            StartCameraSafe();
        }

        private void FaceRecognitionView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopCameraSafe();
        }

        public void SetMode(FaceMode mode)
        {
            _mode = mode;
            StatusText.Text = mode == FaceMode.AutoRegister
                ? "Registering new user..."
                : "Recognizing customer...";
        }

        // ✅ XAML button handler
        //private void BtnBack_Click(object sender, RoutedEventArgs e)
        //{
        //    StopCameraSafe();
        //    BackRequested?.Invoke();
        //}

        private void BtnBack_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                // stop camera if needed
                EcFaceCamSdkHelper.ECF_Stop();
                EcFaceCamSdkHelper.ECF_Close();
            }
            catch { }

            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        // ✅ XAML button handler
        private void RegisterCurrentFace_Click(object sender, RoutedEventArgs e)
        {
            // switch to register mode for the next liveness success
            SetMode(FaceMode.AutoRegister);
            StatusText.Text = "Registration mode: Look at the camera...";
            RestartDetection();
        }

        //private void StartCameraSafe()
        //{
        //    if (_cameraStarted) return;

        //    string cfgPath = Path.Combine(
        //        AppDomain.CurrentDomain.BaseDirectory,
        //        "CameraConfig",
        //        "xmlSamples_IR_ON.txt"
        //    );

        //    string xml = File.ReadAllText(cfgPath);
        //    int openRet = EcFaceCamSdkHelper.ECF_Open(xml);
        //    if (openRet != 0)
        //    {
        //        StatusText.Text = $"ECF_Open failed: {openRet}";
        //        return;
        //    }

        //    // ✅ Bind preview output into native HWND
        //    int wndRet = EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(0, NativePreview.HostHandle, 0, 0, 640, 480);
        //    if (wndRet != 0)
        //    {
        //        StatusText.Text = $"SetDisplayWindowEx failed: {wndRet}";
        //        return;
        //    }
        //    _callbackRef = OnCameraEvent;
        //    //EcFaceCamSdkHelper.ECF_SetCallBack(_callbackRef, IntPtr.Zero);

        //    //EcFaceCamSdkHelper.ECF_StartDetectAsyn();
        //    int cbRet = EcFaceCamSdkHelper.ECF_SetCallBack(_callbackRef, IntPtr.Zero);
        //    if (cbRet != 0)
        //    {
        //        StatusText.Text = $"SetCallBack failed: {cbRet}";
        //        return;
        //    }

        //    int detRet = EcFaceCamSdkHelper.ECF_StartDetectAsyn();
        //    if (detRet != 0)
        //    {
        //        StatusText.Text = $"StartDetectAsyn failed: {detRet}";
        //        return;
        //    }
        //    _cameraStarted = true;

        //    StatusText.Text = "Look at the camera...";
        //}

        private void StartCameraSafe()
        {
            if (_cameraStarted) return;

            try
            {
                var parentWindow = Window.GetWindow(this);
                IntPtr hwnd = new WindowInteropHelper(parentWindow).Handle;

                VideoContainer.UpdateLayout();

                // top-left of VideoContainer in screen coords
                var screenTL = VideoContainer.PointToScreen(new Point(0, 0));
                var screenBR = VideoContainer.PointToScreen(new Point(VideoContainer.ActualWidth, VideoContainer.ActualHeight));

                // top-left of window in screen coords
                var winTL = parentWindow.PointToScreen(new Point(0, 0));

                int left = (int)(screenTL.X - winTL.X);
                int top = (int)(screenTL.Y - winTL.Y);
                int right = (int)(screenBR.X - winTL.X);
                int bottom = (int)(screenBR.Y - winTL.Y);

                // 1) Set display window (VIS)
                int wret = EcFaceCamSdkHelper.ECF_SetDisplayWindowEx(0, hwnd, left, top, right, bottom);
                if (wret != 0)
                {
                    StatusText.Text = $"ECF_SetDisplayWindowEx failed: {wret}";
                    return;
                }

                // 2) Set callback BEFORE open
                int cbret = EcFaceCamSdkHelper.ECF_SetCallBack(_callback, IntPtr.Zero);
                if (cbret != 0)
                {
                    StatusText.Text = $"ECF_SetCallBack failed: {cbret}";
                    return;
                }

                // 3) Load XML text (your file is .txt but contains XML)
                string paramsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CameraConfig", "xmlSamples_IR_ON.txt");
                string strParams = File.Exists(paramsPath) ? File.ReadAllText(paramsPath) : "";

                if (string.IsNullOrWhiteSpace(strParams))
                {
                    StatusText.Text = "Camera config file is empty/missing: CameraConfig/xmlSamples_IR_ON.txt";
                    return;
                }

                // 4) Open (PASS XML STRING, not file path)
                int nRet = EcFaceCamSdkHelper.ECF_Open(strParams);
                if (nRet != 0)
                {
                    StatusText.Text = $"ECF_Open failed (code {nRet}).";
                    return;
                }

                // 5) Start detect
                int dret = EcFaceCamSdkHelper.ECF_StartDetectAsyn();
                if (dret != 0)
                {
                    StatusText.Text = $"ECF_StartDetectAsyn failed: {dret}";
                    return;
                }

                _cameraStarted = true;
                StatusText.Text = "Align your face inside the frame...";

                if (this.Resources["ScannerAnimation"] is System.Windows.Media.Animation.Storyboard sb)
                    sb.Begin();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Initialization Error: " + ex.Message;
            }
        }

        private void StopCameraSafe()
        {
            if (!_cameraStarted) return;

            try
            {
                EcFaceCamSdkHelper.ECF_Stop();
                EcFaceCamSdkHelper.ECF_Close();
            }
            catch { /* ignore */ }

            _cameraStarted = false;
        }

        private void OnCameraEvent(int eventId, IntPtr context)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                StatusText.Text = DescribeEvent(eventId);

                if (eventId == 100)
                {
                    StatusText.Text = "Liveness passed. Processing...";
                    EcFaceCamSdkHelper.ECF_Stop();
                    Task.Delay(250).ContinueWith(_ => ProcessFace());
                    return;
                }

                if (eventId == 101 || eventId == 102)
                {
                    // fail / timeout => restart
                    RestartDetection();
                }
            }));
        }
        private string DescribeEvent(int eventId)
        {
            return eventId switch
            {
                0 => "Hold still…",
                1 => "No face detected",
                2 => "Multiple faces detected",
                7 => "Motion blur – hold still",
                8 => "Lighting issue – adjust position",
                9 => "Not centered – move to center",
                12 => "Not in ROI – align face in frame",
                13 => "Remove sunglasses",
                14 => "Remove mask",
                15 => "Try removing glasses",
                100 => "Liveness OK",
                101 => "Liveness failed",
                102 => "Timeout",
                _ => $"Event: {eventId}"
            };
        }

        private void RestartDetection()
        {
            EcFaceCamSdkHelper.ECF_Stop();
            Task.Delay(400).ContinueWith(_ => EcFaceCamSdkHelper.ECF_StartDetectAsyn());
        }

        private void ProcessFace()
        {
            byte[] faceJpeg = EcFaceCamSdkHelper.GetCroppedVisFace();
            if (faceJpeg == null)
            {
                Dispatcher.Invoke(() => StatusText.Text = "Face capture failed");
                RestartDetection();
                return;
            }

            byte[] feature = FaceSdk.FaceSdk.ExtractFeature(faceJpeg);
            if (feature == null)
            {
                Dispatcher.Invoke(() => StatusText.Text = "Feature extraction failed");
                RestartDetection();
                return;
            }

            if (_mode == FaceMode.AutoRecognize) RecognizeUser(feature);
            else RegisterUser(feature);
        }

        private void RecognizeUser(byte[] feature)
        {
            var faces = FaceStore.Load();
            foreach (var face in faces)
            {
                int score = FaceSdk.FaceSdk.Compare(feature, face.Feature);
                if (score >= 80)
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Welcome {face.UserId} 😊");
                    return;
                }
            }

            Dispatcher.Invoke(() => StatusText.Text = "Not recognized. Tap Register Face.");
        }

        private void RegisterUser(byte[] feature)
        {
            string name = "Customer_" + DateTime.Now.ToString("HHmmss");
            FaceStore.Save(name, feature);
            Dispatcher.Invoke(() => StatusText.Text = $"Registered: {name}");
        }

        public enum FaceMode
        {
            AutoRecognize,
            AutoRegister
        }
    }
}
