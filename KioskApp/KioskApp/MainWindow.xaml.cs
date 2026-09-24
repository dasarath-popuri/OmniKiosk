using OmniKiosk.Wpf.Views;
using OmniKiosk.Wpf.Views.Remittance;
using OmniKiosk.Wpf.Views.SDKTest;
using OmniKiosk.Wpf.Views.MoneyExchange;
using OmniKiosk.Wpf.Services;
using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;


namespace OmniKiosk.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly Uri queueXUri = new Uri("http://121.122.30.121:5110/QueueXchange");
        private readonly Uri omniUri = new Uri("https://omniremit.ai");
        private bool _isDarkMode = false;
        private string _currentLanguage = "en";
        private readonly Stack<UserControl> _sdkNav = new();
        private readonly MenuApiClient _menuClient = new();

        // Make these properties public so they can be accessed from other pages
        public Grid HomeScreen1 => (Grid)FindName("HomeScreen");
        public Grid LanguageSelectionScreen1 => (Grid)FindName("LanguageSelectionScreen");
        public Grid MenuScreen1 => (Grid)FindName("MenuScreen");
        public Grid WebViewScreen1 => (Grid)FindName("WebViewScreen");
        public Border TapCatcher1 => (Border)FindName("TapCatcher");

        public MainWindow()
        {
            InitializeComponent();

            // Load default theme
            LoadTheme(false);
        }

        private void LoadTheme(bool isDark)
        {
            _isDarkMode = isDark;
            // Theme loading logic can be added here if needed
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Kiosk identity check happens FIRST, before any hardware init
            // or UI beyond the bare window - a machine that isn't
            // registered shouldn't get as far as showing the home screen
            // at all, even briefly.
            try
            {
                await KioskAuthService.GetTokenAsync();
            }
            catch (KioskNotRegisteredException ex)
            {
                HomeScreen.Visibility = Visibility.Collapsed;
                TapCatcher.Visibility = Visibility.Collapsed;
                NotRegisteredMacText.Text = ex.Message;
                NotRegisteredScreen.Visibility = Visibility.Visible;
                return; // deliberately does not proceed to hardware init or video below
            }
            catch (KioskMaintenanceException ex)
            {
                HomeScreen.Visibility = Visibility.Collapsed;
                TapCatcher.Visibility = Visibility.Collapsed;
                MaintenanceMessageText.Text = ex.ServerMessage;
                MaintenanceScreen.Visibility = Visibility.Visible;
                return;
            }
            catch (Exception ex)
            {
                // Any OTHER failure (network down, server error, etc.) is
                // NOT the same as "not registered" - logged, but does not
                // block the kiosk from at least attempting to start up.
                // Individual features that actually need the token will
                // surface their own errors when they try to use it.
                KioskLocalLogger.LogError("Startup", "Kiosk auth check failed at startup (non-blocking): " + ex.Message);
            }

            _ = OmniKiosk.Wpf.Services.GlobalHardwareManager.InitializeAllAsync();
            try
            {
                BackgroundVideo.Source = new Uri("https://raw.githubusercontent.com/dasarath-popuri/rma-partners-logos/main/Logos/VivoVideoBG.mp4");
                BackgroundVideo.Volume = 0;
                BackgroundVideo.Play();
            }
            catch { }
        }

        private void BackgroundVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Only loop if we're on the home screen
            if (HomeScreen.Visibility == Visibility.Visible)
            {
                BackgroundVideo.Position = TimeSpan.Zero;
                BackgroundVideo.Play();
            }
        }

        private void TapCatcher_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = (DependencyObject)e.OriginalSource;
            while (source != null)
            {
                if (source is Button)
                    return;
                source = VisualTreeHelper.GetParent(source);
            }

            // Pause video when leaving home screen
            PauseBackgroundVideo();

            HomeScreen.Visibility = Visibility.Collapsed;
            FadeIn(LanguageSelectionScreen);
            BtnCloseApp.Content = "Close";
            TapCatcher.Visibility = Visibility.Collapsed;
        }

        //private void BtnCloseApp_Click(object sender, RoutedEventArgs e)
        //{
        //    Application.Current.Shutdown();
        //}
        private void BtnCloseApp_Click(object sender, RoutedEventArgs e)
        {
            if (WebViewScreen.Visibility == Visibility.Visible)
            {
                WebViewScreen.Visibility = Visibility.Collapsed;
                FadeIn(MenuScreen);
                _ = ApplyMenuCardFlagsAsync();
                try { WebView.CoreWebView2.Navigate("about:blank"); } catch { }
            }
            else if (MenuScreen.Visibility == Visibility.Visible)
            {
                MenuScreen.Visibility = Visibility.Collapsed;
                FadeIn(LanguageSelectionScreen);
                //TapCatcher.Visibility = Visibility.Visible;
                //BtnCloseApp.Content = "Exit";
            }
            else if (LanguageSelectionScreen.Visibility == Visibility.Visible)
            {
                LanguageSelectionScreen.Visibility = Visibility.Collapsed;
                FadeIn(HomeScreen);
                TapCatcher.Visibility = Visibility.Visible;
                BtnCloseApp.Content = "Exit";
            }
            else if (Convert.ToString(BtnCloseApp.Content) == "Exit")
            {
                Application.Current.Shutdown();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
        private void BtnEnglish_Click(object sender, RoutedEventArgs e)
        {
            ApplyLanguage("en");
        }

        private void BtnMalay_Click(object sender, RoutedEventArgs e)
        {
            ApplyLanguage("ms");
        }

        private void ApplyLanguage(string langCode)
        {
            try
            {
                _currentLanguage = langCode;

                // Sets thread culture AND raises PropertyChanged, which every
                // {loc:Loc ...} binding across the window listens to - one call
                // now refreshes every piece of localized text automatically.
                Helpers.LocalizationManager.Instance.SetLanguage(langCode);

                LanguageSelectionScreen.Visibility = Visibility.Collapsed;
                FadeIn(MenuScreen);
                _ = ApplyMenuCardFlagsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error changing language: " + ex.Message);
            }
        }

        // Cheap, safe screen-transition polish: a single Opacity animation, no
        // layout recompute, no effects that force a re-render. Called at every
        // point a screen becomes visible instead of a bare Visibility assignment.
        private static void FadeIn(UIElement element, double milliseconds = 220)
        {
            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            var animation = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds));
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void OpenRemittance_Click(object sender, RoutedEventArgs e)
        {
            if (!OmniKiosk.Wpf.Services.GlobalHardwareManager.AllCriticalSdksReady)
            {
                ShowSdkMaintenance();
                return;
            }

            // Pause video when opening remittance
            PauseBackgroundVideo();

            var remWindow = new RemittanceMainWindow(_currentLanguage);
            remWindow.Owner = this;

            // When remittance window closes, check if we should resume video
            remWindow.Closed += (s, ev) =>
            {
                // If we're back on home screen, resume video
                if (HomeScreen.Visibility == Visibility.Visible)
                {
                    ResumeBackgroundVideo();
                }
            };

            remWindow.ShowDialog();
        }

        //private void OpenTest_Click(object sender, RoutedEventArgs e)
        //{
        //    // Pause video when opening remittance
        //    PauseBackgroundVideo();

        //    MenuScreen.Visibility = Visibility.Collapsed;

        //    // 2. Initialize and Load the FaceRecognitionView into the placeholder
        //    var faceView = new FaceRecognitionView();
        //    FaceRecognitionContent.Content = faceView;

        //    // 3. Show the Face Recognition Screen
        //    FaceRecognitionScreen.Visibility = Visibility.Visible;

        //        }

        //private void OpenTest_Click(object sender, MouseButtonEventArgs e)
        //{
        //    // 1. Pause the video so it doesn't overlap or consume resources
        //    PauseBackgroundVideo();

        //    // 2. Hide all other potential screens to avoid layering issues
        //    HomeScreen.Visibility = Visibility.Collapsed;
        //    MenuScreen.Visibility = Visibility.Collapsed;
        //    LanguageSelectionScreen.Visibility = Visibility.Collapsed;
        //    WebViewScreen.Visibility = Visibility.Collapsed;

        //    // 3. Setup and Show the Face Recognition View
        //    FaceRecognitionScreen.Visibility = Visibility.Visible;

        //    // Create a new instance and assign it to your ContentControl
        //    var faceView = new FaceRecognitionView();
        //    faceView.SetMode(FaceRecognitionView.FaceMode.AutoRegister);   // or AutoRecognize

        //    FaceRecognitionContent.Content = faceView;
        //}
        //private void OpenTest_Click(object sender, MouseButtonEventArgs e)
        //{
        //    PauseBackgroundVideo();

        //    HomeScreen.Visibility = Visibility.Collapsed;
        //    MenuScreen.Visibility = Visibility.Collapsed;
        //    LanguageSelectionScreen.Visibility = Visibility.Collapsed;
        //    WebViewScreen.Visibility = Visibility.Collapsed;

        //    // Show SDK menu screen
        //    SdkTestScreen.Visibility = Visibility.Visible;

        //    var menu = new Views.SDKTest.SdkTestMenuView();
        //    menu.NavigateRequested += SdkMenu_NavigateRequested;
        //    menu.BackRequested += SdkMenu_BackRequested;

        //    SdkTestContent.Content = menu;
        //}

        private void OpenTest_Click(object sender, MouseButtonEventArgs e)
        {
            PauseBackgroundVideo();

            HomeScreen.Visibility = Visibility.Collapsed;
            MenuScreen.Visibility = Visibility.Collapsed;
            LanguageSelectionScreen.Visibility = Visibility.Collapsed;
            WebViewScreen.Visibility = Visibility.Collapsed;

            // show SDK host area
            SdkHostScreen.Visibility = Visibility.Visible;
            _sdkNav.Clear();

            // load SDK menu
            var menu = new SdkTestMenuView();
            menu.BackRequested += (_, __) => CloseSdkHostToMenu();
            menu.NavigateRequested += (_, args) => NavigateToSdk(args.Target);

            SdkHostContent.Content = menu;
        }


        private void NavigateToSdk(SdkTarget target)
        {
            if (SdkHostContent.Content is UserControl current)
                _sdkNav.Push(current);

            UserControl next = target switch
            {
                SdkTarget.Face => CreateFaceView(),
                SdkTarget.Passport => CreatePassportView(),
                SdkTarget.IC => CreateICView(),
                SdkTarget.MoneyDispenser => CreateDispenserView(),
                SdkTarget.MoneyReceiver => CreateReceiverView(),
                SdkTarget.Printer => CreatePrinterView(),
                _ => new NotImplementedView(target.ToString())
            };

            SdkHostContent.Content = next;
        }

        private void SdkBack()
        {
            if (_sdkNav.Count > 0)
            {
                SdkHostContent.Content = _sdkNav.Pop();
                return;
            }
            CloseSdkHostToMenu();
        }

        // Item 10 - flag-driven menu. Every customer-facing card's
        // visibility comes from its own DB row (Ksk_ServiceMenuFlags) -
        // nothing is hidden by hardcoding at the code level, per
        // instruction. Test SDKs is a developer tool with no row at all,
        // deliberately outside this system, always visible.
        //
        // Card widths are adjusted based on how many end up visible, so a
        // handful of cards don't sit lost in a layout built for six - a
        // small, deliberately simple heuristic (fixed widths per visible
        // count), not a general-purpose responsive layout system.
        private async Task ApplyMenuCardFlagsAsync()
        {
            var enabled = await _menuClient.GetEnabledServicesAsync();

            // Fail-open: if the call failed (empty list back), show every
            // known card rather than leaving the customer with an empty,
            // unusable menu screen over a Config.Api hiccup.
            bool failOpen = enabled.Count == 0;

            var cardsByServiceCode = new (string ServiceCode, Border Card)[]
            {
                ("Remittance",    CardRemittance),
                ("MoneyExchange", CardMoneyExchange),
                ("QueueXchange",  CardQueueX),
                ("OmniBrowse",    CardOmni),
                ("RatesToday",    CardRates),
            };

            int visibleCount = 0;
            foreach (var (serviceCode, card) in cardsByServiceCode)
            {
                bool show = failOpen || enabled.Exists(s => s.ServiceCode == serviceCode);
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) visibleCount++;
            }

            double width = visibleCount switch
            {
                1 => 460,
                2 => 420,
                _ => 380
            };

            foreach (var (_, card) in cardsByServiceCode)
            {
                if (card.Visibility == Visibility.Visible)
                    card.Width = width;
            }
        }

        // Checked at menu-click time (Money Exchange / Remittance), not
        // just at boot - a hardware component that initialized fine at
        // startup can still fail later (cable unplugged, device reset).
        // Reuses MaintenanceScreen rather than a dismissable dialog, since
        // a kiosk missing a critical SDK genuinely cannot complete a
        // transaction right now.
        private void ShowSdkMaintenance()
        {
            HomeScreen.Visibility = Visibility.Collapsed;
            MenuScreen.Visibility = Visibility.Collapsed;
            TapCatcher.Visibility = Visibility.Collapsed;
            MaintenanceMessageText.Text =
                "One or more required systems are not responding. Please contact support.";
            MaintenanceScreen.Visibility = Visibility.Visible;
        }

        private void CloseSdkHostToMenu()
        {
            SdkHostScreen.Visibility = Visibility.Collapsed;
            SdkHostContent.Content = null;

            MenuScreen.Visibility = Visibility.Visible;
            ResumeBackgroundVideo();
        }


        private void SdkMenu_BackRequested(object? sender, EventArgs e)
        {
            // Back from SDK menu -> go back to Main Menu (your choice)
            SdkHostScreen.Visibility = Visibility.Collapsed;
            MenuScreen.Visibility = Visibility.Visible;
        }

        private void SdkMenu_NavigateRequested(object? sender, Views.SDKTest.SdkNavigationEventArgs e)
        {
            // Navigate from SDK menu to specific test view
            if (e.Target == Views.SDKTest.SdkTarget.Face)
            {
                var faceView = new FaceRecognitionView();
                faceView.SetMode(FaceRecognitionView.FaceMode.AutoRegister);

                // IMPORTANT: face view must raise BackRequested
                faceView.BackRequested += (s, ev) =>
                {
                    // Back from Face -> return to SDK menu
                    var menu = new Views.SDKTest.SdkTestMenuView();
                    menu.NavigateRequested += SdkMenu_NavigateRequested;
                    menu.BackRequested += SdkMenu_BackRequested;
                    SdkHostContent.Content = menu;
                };

                SdkHostContent.Content = faceView;
                return;
            }

            MessageBox.Show("This SDK test screen is not implemented yet: " + e.Target);
        }
        private UserControl CreateFaceView()
        {
            // NEW test view (keep your old FaceRecognitionView unchanged)
            var v = new FaceSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;
        }
        private UserControl CreateICView()
        {
            var v = new ICSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;
        }
        private UserControl CreatePassportView()
        {
            //var v = new NotImplementedView("Passport Reader (pending integration)");
            //return v;

            var v = new PassportSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;

        }
        private UserControl CreatePrinterView()
        {
            //var v = new NotImplementedView("Passport Reader (pending integration)");
            //return v;

            var v = new PrinterSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;

        }
        private UserControl CreateDispenserView()
        {
            var v = new DispenserSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;
        }
        private UserControl CreateReceiverView()
        {
            var v = new MoneyReceiverSdkTestView();
            v.BackRequested += (_, __) => SdkBack();
            return v;
        }

        private void BtnQueueX_Click(object sender, RoutedEventArgs e)
        {
            // Pause video when showing web page
            PauseBackgroundVideo();
            ShowWebPage(queueXUri.ToString());
        }

        private void BtnOmni_Click(object sender, RoutedEventArgs e)
        {
            // Pause video when showing web page
            PauseBackgroundVideo();
            ShowWebPage("https://omniremit.rma.com.my:34443/rmaproduct.html");
        }
        private void BtnRateBoard_Click(object sender, RoutedEventArgs e)
        {
            // Pause video when showing web page
            PauseBackgroundVideo();
            //ShowWebPage("http://172.168.0.13:6789/Rateboard");
            ShowWebPage("http://121.122.30.121:6789/Rateboard");
        }


        private void OpenMoneyExchange_Click(object sender, MouseButtonEventArgs e)
        {
            if (!OmniKiosk.Wpf.Services.GlobalHardwareManager.AllCriticalSdksReady)
            {
                ShowSdkMaintenance();
                return;
            }

            PauseBackgroundVideo();

            HomeScreen.Visibility = Visibility.Collapsed;
            MenuScreen.Visibility = Visibility.Collapsed;
            LanguageSelectionScreen.Visibility = Visibility.Collapsed;
            WebViewScreen.Visibility = Visibility.Collapsed;
            SdkHostScreen.Visibility = Visibility.Collapsed;

            MoneyExchangeHostScreen.Visibility = Visibility.Visible;
            MoneyExchangeHostContent.Content = null;

            var flow = new MoneyExchangeFlowView(_currentLanguage);
            flow.BackRequested += (_, __) => CloseMoneyExchangeHostToMenu();
            MoneyExchangeHostContent.Content = flow;
        }

        private void CloseMoneyExchangeHostToMenu()
        {
            MoneyExchangeHostScreen.Visibility = Visibility.Collapsed;
            MoneyExchangeHostContent.Content = null;

            MenuScreen.Visibility = Visibility.Visible;
            ResumeBackgroundVideo();
        }

        private async void ShowWebPage(string url)
        {
            MenuScreen.Visibility = Visibility.Collapsed;
            WebViewScreen.Visibility = Visibility.Visible;

            try
            {
                await EnsureWebViewInitialized();
                WebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading page: {ex.Message}");
                WebView.Source = new Uri("about:blank");
            }
        }

        private void BtnNavClose_Click(object sender, RoutedEventArgs e)
        {
            if (WebViewScreen.Visibility == Visibility.Visible)
            {
                WebViewScreen.Visibility = Visibility.Collapsed;
                MenuScreen.Visibility = Visibility.Visible;
                try { WebView.CoreWebView2?.Navigate("about:blank"); } catch { }
            }
            else if (MenuScreen.Visibility == Visibility.Visible)
            {
                MenuScreen.Visibility = Visibility.Collapsed;
                HomeScreen.Visibility = Visibility.Visible;
                TapCatcher.Visibility = Visibility.Visible;

                // Resume video when returning to home screen
                ResumeBackgroundVideo();
            }
        }

        #region Background Video Control Methods

        /// <summary>
        /// Pauses the background video
        /// </summary>
        private void PauseBackgroundVideo()
        {
            try
            {
                if (BackgroundVideo != null)
                {
                    BackgroundVideo.Pause();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error pausing video: {ex.Message}");
            }
        }

        /// <summary>
        /// Resumes the background video from current position
        /// </summary>
        private void ResumeBackgroundVideo()
        {
            try
            {
                if (BackgroundVideo != null && BackgroundVideo.Source != null)
                {
                    BackgroundVideo.Play();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resuming video: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops and resets the background video
        /// </summary>
        private void StopBackgroundVideo()
        {
            try
            {
                if (BackgroundVideo != null)
                {
                    BackgroundVideo.Stop();
                    BackgroundVideo.Position = TimeSpan.Zero;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping video: {ex.Message}");
            }
        }

        #endregion

        private async Task EnsureWebViewInitialized()
        {
            var firstInit = WebView.CoreWebView2 == null;

            if (firstInit)
            {
                var env = await CoreWebView2Environment.CreateAsync();
                await WebView.EnsureCoreWebView2Async(env);

                var core = WebView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.NavigationCompleted += CoreWebView2_NavigationCompleted;
            }

            WebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
            WebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
        }

        private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg?.Equals("print", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Dispatcher.Invoke(PrintCurrentPageSilently);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error receiving message: {ex.Message}");
            }
        }

        //private async void PrintCurrentPageSilently()
        //{
        //    try
        //    {
        //        string html = await GetHtmlFromWebViewAsync();
        //        if (string.IsNullOrWhiteSpace(html))
        //        {
        //            MessageBox.Show("No HTML content found to print.");
        //            return;
        //        }

        //        string plainText = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
        //        plainText = plainText.Replace("&nbsp;", " ");

        //        string escInit = "\x1B\x40";
        //        string escCut = "\x1D\x56\x00";
        //        string receipt = $"{escInit}{plainText}\n\n{escCut}";

        //        string printerName = "POS80";
        //        bool ok = Helpers.RawPrinterHelper.SendStringToPrinter(printerName, receipt);

        //        if (!ok)
        //            MessageBox.Show("Failed to print. Check printer connection.");
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show("Print failed: " + ex.Message);
        //    }
        //}

        private async void PrintCurrentPageSilently()
        {
            try
            {
                string html = await GetHtmlFromWebViewAsync();
                if (string.IsNullOrWhiteSpace(html))
                {
                    MessageBox.Show("No HTML content found to print.");
                    return;
                }

                // Extract queue number
                string queueNumber = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty).Trim();
                queueNumber = queueNumber.Replace("&nbsp;", " ").Trim();

                // ESC/POS Commands
                string escInit = "\x1B\x40";
                string escCut = "\x1D\x56\x00";
                string escAlignCenter = "\x1B\x61\x01";
                string escBold = "\x1B\x45\x01";
                string escBoldOff = "\x1B\x45\x00";
                string escDoubleWH = "\x1B\x21\x30";
                string escNormal = "\x1B\x21\x00";
                string escLarge = "\x1B\x21\x38";

                string dateTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

                StringBuilder receipt = new StringBuilder();

                receipt.Append(escInit);
                receipt.Append(escAlignCenter);

                // Header
                receipt.Append(escLarge);
                receipt.Append("RM APPLICATIONS\n");
                receipt.Append(escNormal);
                receipt.Append("Money Exchange\n");
                receipt.Append("\n");
                receipt.Append("================================\n");
                receipt.Append("\n");

                // Queue Number
                receipt.Append(escBold);
                receipt.Append("YOUR QUEUE NUMBER\n");
                receipt.Append(escBoldOff);
                receipt.Append(escDoubleWH);
                receipt.Append(queueNumber);
                receipt.Append(escNormal);
                receipt.Append("================================");
                // Details
                receipt.Append(dateTime + "\n");
                receipt.Append("\n");

                // Instructions
                receipt.Append(escBold);
                receipt.Append("Please wait for your number\n");
                receipt.Append("to be called\n");
                receipt.Append(escBoldOff);
                receipt.Append("\n");
                receipt.Append("Have your ID ready\n");
                receipt.Append("================================\n");

                // Thank you
                receipt.Append(escLarge);
                receipt.Append("THANK YOU!\n");
                receipt.Append(escNormal);

                receipt.Append(escCut);

                string printerName = "POS80";
                bool ok = Helpers.RawPrinterHelper.SendStringToPrinter(printerName, receipt.ToString());

                if (!ok)
                {
                    MessageBox.Show("Failed to print. Check printer connection.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print failed: " + ex.Message);
            }
        }
        private async Task<string> GetHtmlFromWebViewAsync()
        {
            try
            {
                string htmlJson = await WebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML;");
                string html = System.Text.Json.JsonSerializer.Deserialize<string>(htmlJson);
                if (string.IsNullOrWhiteSpace(html))
                    return null;

                var match = System.Text.RegularExpressions.Regex.Match(html,
                    @"<div[^>]+id=[""']queueNumber[""'][^>]*>([^<]+)</div>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    return match.Groups[1].Value.Trim(); // Returns "Q-985"
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error fetching HTML: " + ex.Message);
                return string.Empty;
            }
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess) LoadOfflineFallback();
        }

        private void LoadOfflineFallback()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Offline", "offline.html");
                if (File.Exists(path))
                    WebView.CoreWebView2.Navigate(new Uri(path).AbsoluteUri);
                else
                    WebView.NavigateToString("<html><body><h2>Offline</h2><p>Content unavailable.</p></body></html>");
            }
            catch { }
        }

        private void OpenTerminalTest_Click(object sender, RoutedEventArgs e)
        {
            // Pause video when opening terminal test
            PauseBackgroundVideo();

            var win = new PaymentTerminalTestView();
            win.Owner = this;

            // Resume video when test window closes if back on home screen
            win.Closed += (s, ev) =>
            {
                if (HomeScreen.Visibility == Visibility.Visible)
                {
                    ResumeBackgroundVideo();
                }
            };

            win.ShowDialog();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up video when window closes
            StopBackgroundVideo();
            OmniKiosk.Wpf.Services.GlobalHardwareManager.ShutdownAll();
            base.OnClosed(e);
        }
    }
}