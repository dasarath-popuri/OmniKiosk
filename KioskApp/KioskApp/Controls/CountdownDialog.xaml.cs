using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;

namespace OmniKiosk.Wpf.Controls
{
    public partial class CountdownDialog : Window
    {
        private Border _blurOverlay;
        private Border _timerBorder;
        private bool _buttonClicked = false;

        public event EventHandler StayHereClicked;
        public event EventHandler GoBackClicked;

        public CountdownDialog(int initialSeconds)
        {
            InitializeComponent();
            _timerBorder = FindTimerBorder();
            UpdateCountdown(initialSeconds);

            this.Loaded += OnLoaded;
            this.Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyBlurEffect();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            RemoveBlurEffect();
        }

        private Border FindTimerBorder()
        {
            if (CountdownText.Parent is Viewbox viewbox && viewbox.Parent is Border border)
            {
                return border;
            }
            return null;
        }

        public void UpdateCountdown(int seconds)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateCountdown(seconds));
                return;
            }

            try
            {
                CountdownText.Text = seconds.ToString();
                SecondsText.Text = seconds.ToString();

                if (_timerBorder != null)
                {
                    Color bgColor;
                    Color textColor;

                    if (seconds <= 5)
                    {
                        bgColor = (Color)ColorConverter.ConvertFromString("#EF4444"); // Red
                        textColor = (Color)ColorConverter.ConvertFromString("#EF4444");
                    }
                    else if (seconds <= 10)
                    {
                        bgColor = (Color)ColorConverter.ConvertFromString("#F59E0B"); // Orange
                        textColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    }
                    else
                    {
                        bgColor = (Color)ColorConverter.ConvertFromString("#F59E0B"); // Yellow
                        textColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
                    }

                    _timerBorder.Background = new SolidColorBrush(bgColor);
                    CountdownText.Foreground = Brushes.White;
                    SecondsText.Foreground = new SolidColorBrush(textColor);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CountdownDialog: Error updating - {ex.Message}");
            }
        }

        private void StayHereButton_Click(object sender, RoutedEventArgs e)
        {
            if (_buttonClicked) return;
            _buttonClicked = true;

            System.Diagnostics.Debug.WriteLine("CountdownDialog: Stay Here button clicked");

            // Disable buttons immediately
            StayHereButton.IsEnabled = false;
            GoBackButton.IsEnabled = false;

            // Raise event - dialog will close immediately in SessionTimeoutManager
            StayHereClicked?.Invoke(this, EventArgs.Empty);
        }

        private void GoBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_buttonClicked) return;
            _buttonClicked = true;

            System.Diagnostics.Debug.WriteLine("CountdownDialog: Go Back button clicked");

            // Disable buttons immediately
            StayHereButton.IsEnabled = false;
            GoBackButton.IsEnabled = false;

            // Raise event - dialog will close immediately in SessionTimeoutManager
            GoBackClicked?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyBlurEffect()
        {
            try
            {
                if (Owner != null && Owner.Content is Grid rootGrid)
                {
                    _blurOverlay = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                        Effect = new BlurEffect { Radius = 8, KernelType = KernelType.Gaussian }
                    };

                    rootGrid.Children.Add(_blurOverlay);
                    Grid.SetRowSpan(_blurOverlay, Math.Max(1, rootGrid.RowDefinitions.Count));
                    Grid.SetColumnSpan(_blurOverlay, Math.Max(1, rootGrid.ColumnDefinitions.Count));
                    Panel.SetZIndex(_blurOverlay, 99999);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CountdownDialog: Error applying blur - {ex.Message}");
            }
        }

        private void RemoveBlurEffect()
        {
            try
            {
                if (Owner?.Content is Grid rootGrid && _blurOverlay != null)
                {
                    rootGrid.Children.Remove(_blurOverlay);
                    _blurOverlay = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CountdownDialog: Error removing blur - {ex.Message}");
            }
        }
    }
}