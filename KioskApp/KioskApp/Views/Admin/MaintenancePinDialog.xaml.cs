using OmniKiosk.Wpf.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OmniKiosk.Wpf.Views.Admin
{
    public partial class MaintenancePinDialog :
        Window
    {
        public MaintenancePinDialog()
        {
            InitializeComponent();
        }

        private void Digit_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            string digit =
                button.Tag?.ToString()
                ?? "";

            if (digit.Length != 1 ||
                !char.IsDigit(digit[0]))
            {
                return;
            }

            if (PinBox.Password.Length >= 6)
                return;

            PinBox.Password +=
                digit;

            ErrorText.Text =
                "";
        }

        private void Clear_Click(
            object sender,
            RoutedEventArgs e)
        {
            PinBox.Clear();

            ErrorText.Text =
                "";
        }

        private void Backspace_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (PinBox.Password.Length == 0)
                return;

            PinBox.Password =
                PinBox.Password.Substring(
                    0,
                    PinBox.Password.Length - 1);

            ErrorText.Text =
                "";
        }

        private async void Validate_Click(
            object sender,
            RoutedEventArgs e)
        {
            string pin =
                PinBox.Password;

            if (pin.Length != 6 ||
                !pin.All(char.IsDigit))
            {
                ErrorText.Text =
                    "Please enter a valid 6-digit PIN.";

                return;
            }

            ValidateButton.IsEnabled =
                false;

            ErrorText.Text =
                "Validating...";

            try
            {
                var result =
                    await KioskAuthService
                        .ValidateMaintenancePinAsync(
                            pin);

                PinBox.Clear();

                if (result.Success)
                {
                    DialogResult =
                        true;

                    Close();

                    return;
                }

                ErrorText.Text =
                    string.IsNullOrWhiteSpace(
                        result.Message)
                        ? "Invalid maintenance PIN."
                        : result.Message;
            }
            catch (Exception ex)
            {
                PinBox.Clear();

                ErrorText.Text =
                    "Unable to validate PIN. " +
                    ex.Message;
            }
            finally
            {
                ValidateButton.IsEnabled =
                    true;
            }
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            PinBox.Clear();

            DialogResult =
                false;

            Close();
        }
    }
}