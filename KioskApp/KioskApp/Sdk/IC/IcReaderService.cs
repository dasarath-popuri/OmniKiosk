using System;
using System.Text;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Sdk.IC
{
    public class MyKadData
    {
        public string IdNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Nationality { get; set; } = "Malaysia";
        public byte[]? PhotoBytes { get; set; }
    }

    public class IcReaderService
    {
        private const int MK_NO_ERROR = 1;
        private const int MK_ERROR = 0;
        private const int MK_CARD_ABSENT = -1;
        private const int MK_READER_ABSENT = -2;
        private const int MK_NO_LICENSE = -4;

        public async Task<(MyKadData? Data, string ErrorMessage)> ReadCardAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Check Reader
                    byte[] readerBuffer = new byte[256];
                    IntPtr readerSize = new IntPtr(256);
                    if (MyKadApi.MyKad_GetReaders(readerBuffer, ref readerSize) <= 0)
                    {
                        return (null, "No smartcard reader found.");
                    }

                    // 2. Connect / Open
                    int openStatus = MyKadApi.MyKad_Open(Encoding.UTF8.GetBytes(string.Empty));
                    if (openStatus != MK_NO_ERROR)
                    {
                        string err = openStatus switch
                        {
                            MK_NO_LICENSE => "License file not found.",
                            MK_CARD_ABSENT => "Card absent. Please insert MyKad properly.",
                            MK_READER_ABSENT => "Reader not found.",
                            _ => "Failed to connect to smart card. Card may be faulty."
                        };
                        return (null, err);
                    }

                    // 3. Read Data
                    MyKadApi.MyKad_GetCardHolderInfo(out CardHolderInfo info);

                    var data = new MyKadData
                    {
                        FullName = info.Name?.Trim() ?? "",
                        IdNumber = info.Nric?.Trim() ?? "",
                        DateOfBirth = info.DateOfBirth?.Trim() ?? "",
                        Gender = info.Gender?.Trim() == "L" ? "Male" : "Female",
                        Nationality = "Malaysia"
                    };

                    // 4. Read Photo
                    byte[] photoBuffer = new byte[4000];
                    IntPtr photoSize = new IntPtr(4000);
                    int photoStatus = MyKadApi.MyKad_GetPhoto(photoBuffer, ref photoSize);

                    if (photoStatus > 0 && photoBuffer.Length > 0)
                    {
                        data.PhotoBytes = photoBuffer;
                    }

                    // 5. Cleanup
                    MyKadApi.MyKad_Close();

                    return (data, "Success");
                }
                catch (Exception ex)
                {
                    try { MyKadApi.MyKad_Close(); } catch { }
                    return (null, $"Exception: {ex.Message}");
                }
            });
        }
    }
}