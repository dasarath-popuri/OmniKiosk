using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Linq;

namespace OmniKiosk.Wpf.Controls
{
    public partial class CustomQuestionDialog : Window
    {
        private Border _blurOverlay;
        private Window _targetWindow;
        private static CustomQuestionDialog _currentQuestionDialog; // Track current question dialog

        public bool Result { get; private set; }

        public CustomQuestionDialog(string title, string message, string yesText, string noText)
        {
            InitializeComponent();
            TitleText.Text = title;
            MessageText.Text = message;
            YesButton.Content = yesText;
            NoButton.Content = noText;

            this.Loaded += (s, e) => ApplyBlurEffect();
            this.Closed += (s, e) =>
            {
                RemoveBlurEffect();
                if (_currentQuestionDialog == this)
                {
                    _currentQuestionDialog = null;
                }
            };
        }

        private void ApplyBlurEffect()
        {
            _targetWindow = this.Owner ?? GetActiveWindow();

            if (_targetWindow != null)
            {
                var rootGrid = FindRootGrid(_targetWindow);

                if (rootGrid != null)
                {
                    _blurOverlay = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                        Effect = new BlurEffect
                        {
                            Radius = 8,
                            KernelType = KernelType.Gaussian
                        }
                    };

                    rootGrid.Children.Add(_blurOverlay);
                    Grid.SetRowSpan(_blurOverlay, rootGrid.RowDefinitions.Count > 0 ? rootGrid.RowDefinitions.Count : 1);
                    Grid.SetColumnSpan(_blurOverlay, rootGrid.ColumnDefinitions.Count > 0 ? rootGrid.ColumnDefinitions.Count : 1);
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
            if (window.Content is Grid grid)
                return grid;

            if (window.Content is FrameworkElement element)
                return FindGridInVisualTree(element);

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

        private static Window GetActiveWindow()
        {
            var activeWindow = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive);

            if (activeWindow != null)
                return activeWindow;

            var remittanceWindow = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.GetType().Name == "RemittanceMainWindow" && w.IsVisible);

            if (remittanceWindow != null)
                return remittanceWindow;

            return Application.Current.MainWindow;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.DialogResult = true;
            this.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.DialogResult = false;
            this.Close();
        }

        /// <summary>
        /// Force closes the current question dialog (used by SessionTimeoutManager)
        /// </summary>
        public static void CloseCurrentQuestionDialog()
        {
            if (_currentQuestionDialog != null && _currentQuestionDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomQuestionDialog: Force closing current question dialog");
                _currentQuestionDialog.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _currentQuestionDialog.Result = false;
                        _currentQuestionDialog.DialogResult = false;
                        _currentQuestionDialog.Close();
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CustomQuestionDialog: Error closing dialog - {ex.Message}");
                    }
                    _currentQuestionDialog = null;
                });
            }
        }

        public static bool Show(string title, string message, string yesText = "Yes, Proceed", string noText = "No, Cancel")
        {
            // Prevent multiple dialogs
            if (_currentQuestionDialog != null && _currentQuestionDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomQuestionDialog: Dialog already open, skipping");
                return false;
            }

            var dialog = new CustomQuestionDialog(title, message, yesText, noText);
            dialog.Owner = GetActiveWindow();

            _currentQuestionDialog = dialog;
            dialog.ShowDialog();

            return dialog.Result;
        }
    }
}