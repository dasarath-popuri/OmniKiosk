using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Linq;

namespace OmniKiosk.Wpf.Controls
{
    public partial class CustomTermsDialog : Window
    {
        private Border _blurOverlay;
        private Window _targetWindow;
        private static CustomTermsDialog _currentDialog;

        public bool Accepted { get; private set; }

        public CustomTermsDialog()
        {
            InitializeComponent();
            Accepted = false;

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

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable buttons to prevent multiple clicks
            AcceptButton.IsEnabled = false;
            DeclineButton.IsEnabled = false;

            Accepted = true;
            this.DialogResult = true;
            this.Close();
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            // Disable buttons to prevent multiple clicks
            AcceptButton.IsEnabled = false;
            DeclineButton.IsEnabled = false;

            Accepted = false;
            this.DialogResult = false;
            this.Close();
        }

        public static bool Show()
        {
            // Prevent multiple dialogs
            if (_currentDialog != null && _currentDialog.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine("CustomTermsDialog: Dialog already open, skipping");
                return false;
            }

            var dialog = new CustomTermsDialog();
            dialog.Owner = GetActiveWindow();

            _currentDialog = dialog;
            dialog.ShowDialog();

            return dialog.Accepted;
        }
    }
}