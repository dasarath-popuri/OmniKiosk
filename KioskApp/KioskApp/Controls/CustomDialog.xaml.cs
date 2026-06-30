using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Linq;

namespace OmniKiosk.Wpf.Controls
{
    public partial class CustomDialog : Window
    {
        private Border _blurOverlay;
        private Window _targetWindow;
        private static CustomDialog _currentDialog; // Track current dialog to prevent duplicates

        public CustomDialog(string title, string message, string icon = "✓", string buttonText = "OK, Continue")
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            IconText.Text = icon;
            OkButton.Content = buttonText;

            this.Loaded += (s, e) => ApplyBlurEffect();
            this.Closed += (s, e) =>
            {
                RemoveBlurEffect();
                if (_currentDialog == this)
                {
                    _currentDialog = null;
                }
            };
        }

        private void ApplyBlurEffect()
        {
            // Get the owner window (RemittanceMainWindow or MainWindow)
            _targetWindow = this.Owner ?? GetActiveWindow();

            if (_targetWindow != null)
            {
                // Find the root Grid in the window
                var rootGrid = FindRootGrid(_targetWindow);

                if (rootGrid != null)
                {
                    // Create blur overlay
                    _blurOverlay = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), // Dark semi-transparent
                        Effect = new BlurEffect
                        {
                            Radius = 8,
                            KernelType = KernelType.Gaussian
                        }
                    };

                    // Add to grid
                    rootGrid.Children.Add(_blurOverlay);

                    // Set to span all rows and columns
                    Grid.SetRowSpan(_blurOverlay, rootGrid.RowDefinitions.Count > 0 ? rootGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(_blurOverlay, rootGrid.ColumnDefinitions.Count > 0 ? rootGrid.ColumnDefinitions.Count : 1);

                    // Ensure overlay is on top
                    Panel.SetZIndex(_blurOverlay, 99999);
                }
            }
        }

        private void RemoveBlurEffect()
        {
            if (_targetWindow != null && _blurOverlay != null)
            {
                var rootGrid = FindRootGrid(_targetWindow);
                if (rootGrid != null)
                {
                    rootGrid.Children.Remove(_blurOverlay);
                }
                _blurOverlay = null;
            }
        }

        private Grid FindRootGrid(Window window)
        {
            // Direct content is Grid
            if (window.Content is Grid grid)
            {
                return grid;
            }

            // Content might be wrapped in another container
            if (window.Content is FrameworkElement element)
            {
                return FindGridInVisualTree(element);
            }

            return null;
        }

        private Grid FindGridInVisualTree(DependencyObject element)
        {
            if (element == null) return null;

            if (element is Grid grid)
                return grid;

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = FindGridInVisualTree(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private static Window GetActiveWindow()
        {
            // Try to find active window
            var activeWindow = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            if (activeWindow != null)
                return activeWindow;

            // Try to find RemittanceMainWindow
            var remittanceWindow = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.GetType().Name == "RemittanceMainWindow" && w.IsVisible);

            if (remittanceWindow != null)
                return remittanceWindow;

            // Fallback to MainWindow
            return Application.Current.MainWindow;
        }

        /// <summary>
        /// Force closes the current dialog (used by SessionTimeoutManager)
        /// </summary>
        public static void CloseCurrentDialog()
        {
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Force closing current dialog");
                _currentDialog.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _currentDialog.DialogResult = false;
                        _currentDialog.Close();
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CustomDialog: Error closing dialog - {ex.Message}");
                    }
                    _currentDialog = null;
                });
            }
        }

        public static void ShowSuccess(string title, string message, string buttonText = "OK, Continue")
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Dialog already open, skipping ShowSuccess");
                return;
            }

            var dialog = new CustomDialog(title, message, "✓", buttonText);
            dialog.IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            dialog.IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
            dialog.OkButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            dialog.Owner = GetActiveWindow();

            _currentDialog = dialog;
            dialog.ShowDialog();
        }

        public static void ShowInfo(string title, string message, string buttonText = "Got it")
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Dialog already open, skipping ShowInfo");
                return;
            }

            var dialog = new CustomDialog(title, message, "ℹ", buttonText);
            dialog.IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            dialog.IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
            dialog.OkButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            dialog.Owner = GetActiveWindow();

            _currentDialog = dialog;
            dialog.ShowDialog();
        }

        public static void ShowWarning(string title, string message, string buttonText = "I Understand")
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Dialog already open, skipping ShowWarning");
                return;
            }

            var dialog = new CustomDialog(title, message, "⚠", buttonText);
            dialog.IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            dialog.IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
            dialog.OkButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            dialog.Owner = GetActiveWindow();

            _currentDialog = dialog;
            dialog.ShowDialog();
        }

        public static void ShowError(string title, string message, string buttonText = "Close")
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Dialog already open, skipping ShowError");
                return;
            }

            var dialog = new CustomDialog(title, message, "✗", buttonText);
            dialog.IconText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            dialog.IconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
            dialog.OkButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            dialog.Owner = GetActiveWindow();

            _currentDialog = dialog;
            dialog.ShowDialog();
        }

        public static bool ShowQuestion(string title, string message, string yesText = "Yes, Proceed", string noText = "No, Cancel")
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomDialog: Dialog already open, skipping ShowQuestion");
                return false;
            }

            bool result = CustomQuestionDialog.Show(title, message, yesText, noText);
            return result;
        }
    }
}