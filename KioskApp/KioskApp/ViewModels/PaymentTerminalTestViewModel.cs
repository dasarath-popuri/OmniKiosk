using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OmniKiosk.Wpf.ViewModels
{
    public class PaymentTerminalTestViewModel : INotifyPropertyChanged
    {
        private string _log;
        public string Log
        {
            get => _log;
            set { _log = value; OnPropertyChanged(); }
        }

        public void AppendLog(string item)
        {
            Log += item + "\n";
            OnPropertyChanged(nameof(Log));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
