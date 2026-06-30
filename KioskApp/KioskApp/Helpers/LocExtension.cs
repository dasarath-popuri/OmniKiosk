//using System;
//using System.Windows.Markup;
//using System.Windows.Data;


//namespace OmniKiosk.Wpf.Helpers
//{
//    public class LocExtension : MarkupExtension
//    {
//        public string Key { get; set; }
//        public LocExtension() { }
//        public LocExtension(string key) { Key = key; }


//        public override object ProvideValue(IServiceProvider serviceProvider)
//        {
//            var binding = new Binding($"[{Key}]")
//            {
//                Source = LocalizationManager.Instance,
//                Mode = BindingMode.OneWay
//            };
//            return binding.ProvideValue(serviceProvider);
//        }
//    }
//}