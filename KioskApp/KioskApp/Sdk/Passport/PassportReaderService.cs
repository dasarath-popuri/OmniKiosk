using System;
using System.IO;

namespace OmniKiosk.Wpf.Sdk.Passport
{
    public sealed class PassportReaderService : IDisposable
    {
        private readonly string _userId;
        private readonly string _libPath;
        private IDCardSdk? _sdk;

        public PassportReaderService(string userId, string libPath)
        {
            _userId = userId;
            _libPath = libPath;
        }

        public void Init()
        {
            if (string.IsNullOrWhiteSpace(_userId))
                throw new ArgumentException("UserId is empty.");

            if (!Directory.Exists(_libPath))
                throw new DirectoryNotFoundException("Lib folder not found: " + _libPath);

            var dll = Path.Combine(_libPath, "IDCard.dll");
            if (!File.Exists(dll))
                throw new FileNotFoundException("IDCard.dll not found in: " + _libPath);

            _sdk?.Dispose();
            _sdk = new IDCardSdk(dll);

            var ret = _sdk.InitIDCard(_userId, 0,_libPath);
            if (ret != 0)
                throw new InvalidOperationException($"InitIDCard failed. ret={ret}");

            // load config if present
            var cfg = Path.Combine(_libPath, "IDCardConfig.ini");
            if (File.Exists(cfg))
            {
                var cfgRet = _sdk.SetConfigByFile(cfg);
                if (cfgRet != 0)
                    throw new InvalidOperationException($"SetConfigByFile failed. ret={cfgRet}");
            }

            // English
            _sdk.SetLanguage(1);

            // Passport only (MAINID=13)
            _sdk.ResetIDCardID();
            _sdk.AddIDCardID(13, new[] { 0 }, 1);

            // Read VIZ + chip DG1/DG2
            _sdk.SetRecogVIZ(true);
            _sdk.SetRecogDG(6);         // DG1 (2) + DG2 (4) = 6
            _sdk.SetAnalyseMRZ(true);

            // Save images: White(1) + OCR Head(8) + Chip Head(16) = 25
            _sdk.SetSaveImageType(25);
        }

        public int CheckOnlineEx() => _sdk == null ? 3 : _sdk.CheckDeviceOnlineEx();


        public string DumpFirstNFields(int attr, int count = 120)
        {
            if (_sdk == null) return "SDK not initialized";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var v = _sdk.GetRecogResultStr(attr, i);
                if (!string.IsNullOrWhiteSpace(v))
                    sb.AppendLine($"attr={attr} idx={i}: {v}");
            }
            return sb.ToString();
        }
        public bool TryReadPassport(out PassportDoc doc, out string? portraitPath)
        {
            doc = new PassportDoc();
            portraitPath = null;

            if (_sdk == null) return false;

            int cardType = 0;
            int mainId = _sdk.AutoProcessIDCard(ref cardType);

            // success or partial success codes (depends on reader model)
            if (!(mainId > 0 || mainId == -8 || mainId == -1115 || mainId == -1117))
                return false;

            // These indexes follow the chip-field table used in most Sinosecu passports:
            //doc.PassportNumber = _sdk.GetRecogResultStr(0, 14);
            //doc.EnglishName = _sdk.GetRecogResultStr(0, 15);
            //doc.DateOfBirth = _sdk.GetRecogResultStr(0, 16);
            //doc.DateOfExpiry = _sdk.GetRecogResultStr(0, 17);
            //doc.Sex = _sdk.GetRecogResultStr(0, 18);
            //doc.Nationality = _sdk.GetRecogResultStr(0, 20);

            // OCR page results (attr=1) — because chip results (attr=0) are empty currently
            doc.DocumentType = _sdk.GetRecogResultStr(1, 0);   // P
            doc.PassportNumber = _sdk.GetRecogResultStr(1, 1) ?? _sdk.GetRecogResultStr(1, 13);
            doc.FullName = _sdk.GetRecogResultStr(1, 3);
            doc.Sex = _sdk.GetRecogResultStr(1, 4);
            doc.DateOfBirth = _sdk.GetRecogResultStr(1, 5);
            doc.DateOfExpiry = _sdk.GetRecogResultStr(1, 6);
            doc.Nationality = _sdk.GetRecogResultStr(1, 7) ?? _sdk.GetRecogResultStr(1, 12);

            // Optional extras seen in your dump
            doc.Surname = _sdk.GetRecogResultStr(1, 8);
            doc.GivenNames = _sdk.GetRecogResultStr(1, 9);
            doc.MrzLine1 = _sdk.GetRecogResultStr(1, 10);
            doc.MrzLine2 = _sdk.GetRecogResultStr(1, 11);
            doc.PlaceOfBirth = _sdk.GetRecogResultStr(1, 14);
            doc.PlaceOfIssue = _sdk.GetRecogResultStr(1, 15);
            doc.DateOfIssue = _sdk.GetRecogResultStr(1, 16);
            doc.PersonalNumber = _sdk.GetRecogResultStr(1, 21);
            // Save images to temp
            var tempDir = Path.Combine(Path.GetTempPath(), "KioskPassport");
            Directory.CreateDirectory(tempDir);

            var baseFile = Path.Combine(tempDir, "Passport.jpg");
            SafeDelete(baseFile);
            SafeDelete(InsertSuffix(baseFile, "Head"));
            SafeDelete(InsertSuffix(baseFile, "HeadEC"));

            _sdk.SaveImageEx(baseFile, 25);

            var chipHead = InsertSuffix(baseFile, "HeadEC");
            var ocrHead = InsertSuffix(baseFile, "Head");
            portraitPath = File.Exists(chipHead) ? chipHead : (File.Exists(ocrHead) ? ocrHead : null);

            return true;
        }

        public void Dispose()
        {
            try { _sdk?.FreeIDCard(); } catch { }
            _sdk?.Dispose();
            _sdk = null;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string InsertSuffix(string path, string suffix)
        {
            var ext = Path.GetExtension(path);
            return path.Substring(0, path.Length - ext.Length) + suffix + ext;
        }
    }

    //public sealed class PassportDoc
    //{
    //    public string? PassportNumber { get; set; }
    //    public string? EnglishName { get; set; }
    //    public string? Nationality { get; set; }
    //    public string? DateOfBirth { get; set; }
    //    public string? DateOfExpiry { get; set; }
    //    public string? Sex { get; set; }
    //}
    public sealed class PassportDoc
    {
        public string? DocumentType { get; set; }
        public string? PassportNumber { get; set; }
        public string? FullName { get; set; }
        public string? Surname { get; set; }
        public string? GivenNames { get; set; }
        public string? Nationality { get; set; }
        public string? Sex { get; set; }
        public string? DateOfBirth { get; set; }
        public string? DateOfExpiry { get; set; }
        public string? DateOfIssue { get; set; }
        public string? PlaceOfBirth { get; set; }
        public string? PlaceOfIssue { get; set; }
        public string? PersonalNumber { get; set; }
        public string? MrzLine1 { get; set; }
        public string? MrzLine2 { get; set; }
    }
}