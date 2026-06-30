using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;

namespace OmniKiosk.Wpf.Views.Remittance
{
    public partial class CameraWindow : Window
    {
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource;
        private Bitmap capturedBitmap;
        private List<string> availableCameras;
        private int frameCounter = 0;
        private DispatcherTimer monitorTimer;

        public byte[] CapturedImageData { get; private set; }

        public CameraWindow()
        {
            InitializeComponent();
            this.Loaded += CameraWindow_Loaded;
        }

        private void CameraWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                Thread.Sleep(500);
                Dispatcher.Invoke(() => CheckAndInitializeCamera());
            });
        }

        private void CheckAndInitializeCamera()
        {
            try
            {
                LoadingMessage.Text = "Checking for cameras...";

                videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

                if (videoDevices.Count == 0)
                {
                    MessageBox.Show(
                        "No camera was detected on this system.\n\n" +
                        "Please ensure:\n" +
                        "• A camera is connected\n" +
                        "• Camera drivers are installed\n" +
                        "• No other application is using the camera",
                        "Camera Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    this.Close();
                    return;
                }

                availableCameras = new List<string>();
                foreach (FilterInfo device in videoDevices)
                {
                    availableCameras.Add(device.Name);
                    System.Diagnostics.Debug.WriteLine($"Found camera: {device.Name}");
                }

                if (videoDevices.Count > 1)
                {
                    ShowCameraSelection();
                }
                else
                {
                    InitializeCamera(0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                MessageBox.Show($"Error accessing camera:\n\n{ex.Message}", "Camera Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void ShowCameraSelection()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            CameraSelectionPanel.Visibility = Visibility.Visible;
            CameraListBox.ItemsSource = availableCameras;
            CaptureButton.IsEnabled = false;
        }

        private void CameraListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CameraListBox.SelectedIndex >= 0)
            {
                CameraSelectionPanel.Visibility = Visibility.Collapsed;
                LoadingPanel.Visibility = Visibility.Visible;
                LoadingMessage.Text = "Initializing selected camera...";
                InitializeCamera(CameraListBox.SelectedIndex);
            }
        }

        private void InitializeCamera(int deviceIndex)
        {
            try
            {
                string cameraName = videoDevices[deviceIndex].Name;
                LoadingMessage.Text = $"Starting {cameraName}...";
                System.Diagnostics.Debug.WriteLine($"Initializing: {cameraName}");

                videoSource = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);

                // Log all capabilities
                if (videoSource.VideoCapabilities != null && videoSource.VideoCapabilities.Length > 0)
                {
                    foreach (var cap in videoSource.VideoCapabilities)
                    {
                        System.Diagnostics.Debug.WriteLine($"Available: {cap.FrameSize.Width}x{cap.FrameSize.Height} @ {cap.AverageFrameRate}fps");
                    }

                    // Use 640x480 or first available
                    var preferredCap = videoSource.VideoCapabilities
                        .FirstOrDefault(c => c.FrameSize.Width == 640 && c.FrameSize.Height == 480);

                    if (preferredCap == null)
                    {
                        preferredCap = videoSource.VideoCapabilities[0];
                    }

                    videoSource.VideoResolution = preferredCap;
                    System.Diagnostics.Debug.WriteLine($"Using: {preferredCap.FrameSize.Width}x{preferredCap.FrameSize.Height}");
                }

                // Set additional properties to ensure frame delivery
                videoSource.ProvideSnapshots = false;

                // Attach event handler BEFORE starting
                videoSource.NewFrame += VideoSource_NewFrame;

                System.Diagnostics.Debug.WriteLine("Starting camera...");

                // Start camera
                videoSource.Start();

                System.Diagnostics.Debug.WriteLine("Camera start command sent");

                // Wait for camera to actually start
                int attempts = 0;
                while (!videoSource.IsRunning && attempts < 50)
                {
                    Thread.Sleep(100);
                    attempts++;
                }

                System.Diagnostics.Debug.WriteLine($"Camera running: {videoSource.IsRunning} after {attempts} attempts");

                // Start monitoring timer
                StartFrameMonitoring();

                // Hide loading after a delay
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();

                    if (frameCounter == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: No frames received after 3 seconds!");
                        LoadingMessage.Text = "Camera not responding. Retrying...";

                        // Try to restart
                        RestartCamera(deviceIndex);
                    }
                    else
                    {
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        FaceGuide.Visibility = Visibility.Visible;
                        CaptureButton.IsEnabled = true;
                        System.Diagnostics.Debug.WriteLine($"Camera ready. Frames: {frameCounter}");
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Init error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Failed to initialize camera:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void StartFrameMonitoring()
        {
            monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            monitorTimer.Tick += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Frame check: {frameCounter} frames received, Camera running: {videoSource?.IsRunning}");
            };
            monitorTimer.Start();
        }

        private void RestartCamera(int deviceIndex)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to restart camera...");

                // Stop existing
                if (videoSource != null)
                {
                    videoSource.NewFrame -= VideoSource_NewFrame;
                    if (videoSource.IsRunning)
                    {
                        videoSource.SignalToStop();
                        videoSource.WaitForStop();
                    }
                    videoSource = null;
                }

                frameCounter = 0;
                Thread.Sleep(1000);

                // Reinitialize
                videoSource = new VideoCaptureDevice(videoDevices[deviceIndex].MonikerString);

                // Try a different resolution
                if (videoSource.VideoCapabilities != null && videoSource.VideoCapabilities.Length > 0)
                {
                    // Try 320x240 for better compatibility
                    var cap = videoSource.VideoCapabilities
                        .FirstOrDefault(c => c.FrameSize.Width == 320 && c.FrameSize.Height == 240);

                    if (cap == null)
                        cap = videoSource.VideoCapabilities[0];

                    videoSource.VideoResolution = cap;
                    System.Diagnostics.Debug.WriteLine($"Retry with: {cap.FrameSize.Width}x{cap.FrameSize.Height}");
                }

                videoSource.NewFrame += VideoSource_NewFrame;
                videoSource.Start();

                // Wait and check again
                var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                retryTimer.Tick += (s, e) =>
                {
                    retryTimer.Stop();

                    if (frameCounter == 0)
                    {
                        MessageBox.Show(
                            "Camera is not responding after multiple attempts.\n\n" +
                            "This may be a driver or compatibility issue.\n\n" +
                            "Try:\n" +
                            "• Restarting the application\n" +
                            "• Using a different camera\n" +
                            "• Updating camera drivers",
                            "Camera Not Responding",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        this.Close();
                    }
                    else
                    {
                        LoadingPanel.Visibility = Visibility.Collapsed;
                        FaceGuide.Visibility = Visibility.Visible;
                        CaptureButton.IsEnabled = true;
                    }
                };
                retryTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Restart error: {ex.Message}");
                MessageBox.Show($"Failed to restart camera:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            try
            {
                frameCounter++;

                // Log frequently initially to debug
                if (frameCounter <= 10 || frameCounter % 30 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Frame {frameCounter} received!");
                }

                // Clone and convert
                Bitmap bitmap = (Bitmap)eventArgs.Frame.Clone();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using (MemoryStream memory = new MemoryStream())
                        {
                            bitmap.Save(memory, ImageFormat.Bmp);
                            memory.Position = 0;

                            BitmapImage bitmapImage = new BitmapImage();
                            bitmapImage.BeginInit();
                            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                            bitmapImage.StreamSource = memory;
                            bitmapImage.EndInit();
                            bitmapImage.Freeze();

                            VideoPlayer.Source = bitmapImage;

                            if (frameCounter == 1)
                            {
                                System.Diagnostics.Debug.WriteLine($"First frame displayed! {bitmapImage.PixelWidth}x{bitmapImage.PixelHeight}");
                            }
                        }
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }), DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NewFrame error: {ex.Message}");
            }
        }

        private void Capture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Capturing...");

                var currentImage = VideoPlayer.Source as BitmapImage;

                if (currentImage != null)
                {
                    capturedBitmap = BitmapImageToBitmap(currentImage);

                    CapturedImage.Source = currentImage;
                    CapturedImage.Visibility = Visibility.Visible;
                    VideoPlayer.Visibility = Visibility.Collapsed;
                    FaceGuide.Visibility = Visibility.Collapsed;

                    CaptureButton.Visibility = Visibility.Collapsed;
                    UsePhotoButton.Visibility = Visibility.Visible;
                    RetakeButton.Visibility = Visibility.Visible;

                    if (monitorTimer != null)
                    {
                        monitorTimer.Stop();
                    }

                    if (videoSource != null && videoSource.IsRunning)
                    {
                        videoSource.SignalToStop();
                        videoSource.WaitForStop();
                    }

                    System.Diagnostics.Debug.WriteLine("Captured!");
                }
                else
                {
                    MessageBox.Show("No video frame available", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Bitmap BitmapImageToBitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                return new Bitmap(outStream);
            }
        }

        private void Retake_Click(object sender, RoutedEventArgs e)
        {
            CapturedImage.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Visible;
            FaceGuide.Visibility = Visibility.Visible;

            CaptureButton.Visibility = Visibility.Visible;
            UsePhotoButton.Visibility = Visibility.Collapsed;
            RetakeButton.Visibility = Visibility.Collapsed;

            if (videoSource != null && !videoSource.IsRunning)
            {
                frameCounter = 0;
                videoSource.Start();

                if (monitorTimer != null)
                {
                    monitorTimer.Start();
                }
            }
        }

        private void UsePhoto_Click(object sender, RoutedEventArgs e)
        {
            if (capturedBitmap != null)
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);

                        if (jpegEncoder != null)
                        {
                            EncoderParameters encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                            capturedBitmap.Save(ms, jpegEncoder, encoderParams);
                        }
                        else
                        {
                            capturedBitmap.Save(ms, ImageFormat.Jpeg);
                        }

                        CapturedImageData = ms.ToArray();
                        System.Diagnostics.Debug.WriteLine($"Saved: {CapturedImageData.Length} bytes");
                    }

                    this.DialogResult = true;
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (monitorTimer != null)
                {
                    monitorTimer.Stop();
                    monitorTimer = null;
                }

                if (videoSource != null)
                {
                    videoSource.NewFrame -= VideoSource_NewFrame;

                    if (videoSource.IsRunning)
                    {
                        videoSource.SignalToStop();
                        videoSource.WaitForStop();
                    }
                    videoSource = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Close error: {ex.Message}");
            }

            base.OnClosing(e);
        }
    }
}