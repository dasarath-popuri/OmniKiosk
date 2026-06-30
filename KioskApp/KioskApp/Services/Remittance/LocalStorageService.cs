using System;
using System.IO;

namespace OmniKiosk.Wpf.Services.Remittance
{
    /// <summary>
    /// Service for managing local file storage (customer photos, documents)
    /// </summary>
    public class LocalStorageService
    {
        private readonly string _photoDir;
        private readonly string _documentDir;

        public LocalStorageService()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KioskApp");

            _photoDir = Path.Combine(baseDir, "CustomerPhotos");
            _documentDir = Path.Combine(baseDir, "Documents");

            // Create directories if they don't exist
            Directory.CreateDirectory(_photoDir);
            Directory.CreateDirectory(_documentDir);
        }

        /// <summary>
        /// Save customer photo and return the saved file path
        /// </summary>
        public string SaveCustomerPhoto(string sourceFilePath)
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException("Source photo file not found.", sourceFilePath);
            }

            var ext = Path.GetExtension(sourceFilePath);
            var fileName = $"cust_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            var destPath = Path.Combine(_photoDir, fileName);

            File.Copy(sourceFilePath, destPath, true);
            return destPath;
        }

        /// <summary>
        /// Save transaction document
        /// </summary>
        public string SaveDocument(string content, string fileName)
        {
            var filePath = Path.Combine(_documentDir, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        /// <summary>
        /// Get all customer photos
        /// </summary>
        public string[] GetCustomerPhotos()
        {
            return Directory.GetFiles(_photoDir, "cust_*.*");
        }

        /// <summary>
        /// Delete old photos (older than specified days)
        /// </summary>
        public int CleanupOldPhotos(int daysOld = 90)
        {
            var cutoffDate = DateTime.Now.AddDays(-daysOld);
            var deletedCount = 0;

            foreach (var file in Directory.GetFiles(_photoDir))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < cutoffDate)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch
                    {
                        // Skip files that cannot be deleted
                    }
                }
            }

            return deletedCount;
        }
    }
}