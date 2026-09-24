using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;

namespace OmniKiosk.Wpf.Controls
{
    public partial class CustomAmountDialog : Window
    {
        private Border? _blurOverlay;
        private Window? _targetWindow;
        private static CustomAmountDialog? _currentDialog;

        private string _typed = "";
        public decimal? Result { get; private set; }

        public CustomAmountDialog(string title, string currencyPrefix, decimal? initialAmount)
        {
            InitializeComponent();
            TitleText.Text = title;
            CurrencyPrefixText.Text = currencyPrefix;

            if (initialAmount.HasValue && initialAmount.Value > 0)
                _typed = initialAmount.Value.ToString("0.##");

            UpdateDisplay();

            this.Loaded += (s, e) => ApplyBlurEffect();
            this.Closed += (s, e) =>
            {
                RemoveBlurEffect();
                if (_currentDialog == this) _currentDialog = null;
            };
        }

        private void UpdateDisplay()
        {
            AmountDisplayText.Text = string.IsNullOrEmpty(_typed) ? "0" : _typed;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string digit) return;

            int dotIndex = _typed.IndexOf('.');
            if (dotIndex >= 0 && _typed.Length - dotIndex > 2) return;

            if (_typed == "0") _typed = digit;
            else _typed += digit;

            UpdateDisplay();
        }

        private void Decimal_Click(object sender, RoutedEventArgs e)
        {
            if (_typed.Contains('.')) return;
            if (string.IsNullOrEmpty(_typed)) _typed = "0";
            _typed += ".";
            UpdateDisplay();
        }

        private void Backspace_Click(object sender, RoutedEventArgs e)
        {
            if (_typed.Length > 0)
                _typed = _typed.Substring(0, _typed.Length - 1);
            UpdateDisplay();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _typed = "";
            UpdateDisplay();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(_typed, out var amount) || amount <= 0)
            {
                ErrorText.Text = "Please enter an amount greater than zero.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            Result = amount;
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            this.DialogResult = false;
            this.Close();
        }

        private void ApplyBlurEffect()
        {
            _targetWindow = this.Owner ?? GetActiveWindow();
            if (_targetWindow == null) return;

            var rootGrid = FindRootGrid(_targetWindow);
            if (rootGrid == null) return;

            _blurOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                Effect = new BlurEffect { Radius = 8, KernelType = KernelType.Gaussian }
            };

            rootGrid.Children.Add(_blurOverlay);
            Grid.SetRowSpan(_blurOverlay, rootGrid.RowDefinitions.Count > 0 ? rootGrid.RowDefinitions.Count : 1);
            Grid.SetColumnSpan(_blurOverlay, rootGrid.ColumnDefinitions.Count > 0 ? rootGrid.ColumnDefinitions.Count : 1);
            Panel.SetZIndex(_blurOverlay, 99999);
        }

        private void RemoveBlurEffect()
        {
            if (_targetWindow != null && _blurOverlay != null)
            {
                var rootGrid = FindRootGrid(_targetWindow);
                rootGrid?.Children.Remove(_blurOverlay);
                _blurOverlay = null;
            }
        }

        private Grid? FindRootGrid(Window window)
        {
            if (window.Content is Grid grid) return grid;
            if (window.Content is FrameworkElement element) return FindGridInVisualTree(element);
            return null;
        }

        private Grid? FindGridInVisualTree(DependencyObject element)
        {
            if (element is Grid grid) return grid;
            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                var result = FindGridInVisualTree(VisualTreeHelper.GetChild(element, i));
                if (result != null) return result;
            }
            return null;
        }

        private static Window? GetActiveWindow()
        {
            var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null) return activeWindow;

            var remittanceWindow = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.GetType().Name == "RemittanceMainWindow" && w.IsVisible);
            if (remittanceWindow != null) return remittanceWindow;

            return Application.Current.MainWindow;
        }

        public static decimal? Show(string title, string currencyPrefix, decimal? initialAmount = null)
        {
            if (_currentDialog != null && _currentDialog.IsVisible)
                return null;

            var dialog = new CustomAmountDialog(title, currencyPrefix, initialAmount)
            {
                Owner = GetActiveWindow()
            };

            _currentDialog = dialog;
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}