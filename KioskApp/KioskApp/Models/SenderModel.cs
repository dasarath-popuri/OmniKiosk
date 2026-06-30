//using System;
//using System.IO;
//using System.Windows.Media.Imaging;

//namespace OmniKiosk.Wpf.Models
//{
//    public class SenderModel
//    {
//        public int Id { get; set; }
//        public string FullName { get; set; }
//        public string ICNumber { get; set; }
//        public string ICType { get; set; }
//        public DateTime DateOfBirth { get; set; }
//        public string Nationality { get; set; }
//        public string MobileNo { get; set; }
//        public string Email { get; set; }
//        public string Address { get; set; }
//        public string City { get; set; }
//        public string Postcode { get; set; }
//        public string State { get; set; }
//        public string Country { get; set; }
//        public string Occupation { get; set; }
//        public string Employer { get; set; }
//        public string SourceOfFunds { get; set; }
//        public string PhotoPath { get; set; }
//        public DateTime CreatedDate { get; set; }
//        public DateTime LastUsedDate { get; set; }

//        public string DisplayName => $"{FullName} - {ICNumber}";
//        public string LastUsedText => $"Last used: {LastUsedDate:dd/MM/yyyy}";
//    }
//}

using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace OmniKiosk.Wpf.Models
{
    public class SenderModel
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string ICNumber { get; set; }
        public string ICType { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Nationality { get; set; }
        public string MobileNo { get; set; }
        public string Email { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Postcode { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string Occupation { get; set; }
        public string Employer { get; set; }
        public string SourceOfFunds { get; set; }
        public byte[] Photo { get; set; } // Changed from PhotoPath to Photo byte array
        public DateTime CreatedDate { get; set; }
        public DateTime LastUsedDate { get; set; }

        public string DisplayName => $"{FullName} - {ICNumber}";
        public string LastUsedText => $"Last used: {LastUsedDate:dd/MM/yyyy}";

        // Property for displaying photo in UI
        public BitmapImage PhotoImage
        {
            get
            {
                if (Photo == null || Photo.Length == 0)
                    return null;

                try
                {
                    using (var ms = new MemoryStream(Photo))
                    {
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = ms;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        // Property to check if photo exists
        public bool HasPhoto => Photo != null && Photo.Length > 0;
    }
}