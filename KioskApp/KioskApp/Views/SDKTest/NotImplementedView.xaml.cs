using System.Windows.Controls;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public partial class NotImplementedView : UserControl
    {
        public NotImplementedView(string name)
        {
            InitializeComponent();
            TxtName.Text = name;
        }
    }
}
