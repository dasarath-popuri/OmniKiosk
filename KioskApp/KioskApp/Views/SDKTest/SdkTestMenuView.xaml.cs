using System;
using System.Windows.Controls;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class SdkTestMenuView : UserControl
    {
        public event EventHandler<SdkNavigationEventArgs>? NavigateRequested;
        public event EventHandler? BackRequested;

        public SdkTestMenuView()
        {
            InitializeComponent();
        }

        private void Back_Click(object sender, System.Windows.RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);

        private void TestFace_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.Face));

        private void TestPassport_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.Passport));

        private void TestIc_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.IC));

        private void TestDispenser_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.MoneyDispenser));

        private void TestReceiver_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.MoneyReceiver));

        private void TestPrinter_Click(object sender, System.Windows.RoutedEventArgs e)
            => NavigateRequested?.Invoke(this, new SdkNavigationEventArgs(SdkTarget.Printer));

        
    }

    public enum SdkTarget
    {
        Passport,
        IC,
        Face,
        MoneyDispenser,
        MoneyReceiver,
        Printer
    }

    public class SdkNavigationEventArgs : EventArgs
    {
        public SdkTarget Target { get; }

        public SdkNavigationEventArgs(SdkTarget target) => Target = target;
    }
}
