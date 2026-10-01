using OmniKiosk.Wpf.Services;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace OmniKiosk.Wpf.Helpers
{
    /// <summary>
    /// UAT-only helper for saving the exact document image that is being sent
    /// to eKYC. Do not keep this enabled in production because ID images are
    /// sensitive customer data.
    /// </summary>
    public static class EkycDocumentDebugHelper
    {
        public static void SaveUatSample(
            string base64Image,
            string documentType,
            string? journeyId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(base64Image))
                    return;

                byte[] bytes =
                    Convert.FromBase64String(
                        base64Image);

                int width = 0;
                int height = 0;

                try
                {
                    using var stream =
                        new MemoryStream(
                            bytes,
                            false);

                    BitmapFrame frame =
                        BitmapFrame.Create(
                            stream,
                            BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad);

                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                }
                catch
                {
                    // Keep the raw sample even if metadata decoding fails.
                }

                string directory =
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Logs",
                        "EkycDocumentSamples");

                Directory.CreateDirectory(
                    directory);

                string safeDocType =
                    string.IsNullOrWhiteSpace(documentType)
                        ? "Document"
                        : documentType.Replace(" ", "_");

                string journeyPart =
                    string.IsNullOrWhiteSpace(journeyId)
                        ? "NoJourney"
                        : journeyId.Length > 8
                            ? journeyId.Substring(0, 8)
                            : journeyId;

                string filePath =
                    Path.Combine(
                        directory,
                        $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{safeDocType}_{journeyPart}.jpg");

                File.WriteAllBytes(
                    filePath,
                    bytes);

                KioskLocalLogger.LogInfo(
                    "EkycDocument",
                    $"UAT eKYC source image saved. " +
                    $"File={filePath}, " +
                    $"Bytes={bytes.Length}, " +
                    $"Dimensions={width}x{height}, " +
                    $"DocumentType={documentType}, " +
                    $"JourneyId={journeyId}");
            }
            catch (Exception ex)
            {
                KioskLocalLogger.LogError(
                    "EkycDocument",
                    "Unable to save UAT eKYC document sample: " +
                    ex.Message);
            }
        }
    }
}
