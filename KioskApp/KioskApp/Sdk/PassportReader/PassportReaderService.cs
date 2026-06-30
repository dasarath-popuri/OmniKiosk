//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//namespace OmniKiosk.Wpf.Sdk.PassportReader
//{
//    public sealed class PassportReaderService : IDisposable
//    {
//        private bool _initialized;

//        public void Initialize(string userId, string libDir)
//        {
//            if (string.IsNullOrWhiteSpace(userId))
//                throw new ArgumentException("UserId is required.", nameof(userId));

//            if (string.IsNullOrWhiteSpace(libDir) || !Directory.Exists(libDir))
//                throw new DirectoryNotFoundException($"Lib directory not found: {libDir}");

//            // nType: keep 0 unless you need extra engines. (doc says other bits unused except bit0 for business card) :contentReference[oaicite:9]{index=9}
//            int ret = IdCardNative.InitIDCard(userId, 0, libDir);
//            if (ret != 0)
//                throw new InvalidOperationException($"InitIDCard failed. Code={ret}");

//            _initialized = true;
//        }

//        public async Task<PassportReadResult> WaitAndReadOnceAsync(
//            string outputFolder,
//            CancellationToken ct)
//        {
//            if (!_initialized)
//                throw new InvalidOperationException("Service not initialized.");

//            Directory.CreateDirectory(outputFolder);

//            // 1) Wait for document placed: DetectDocument => 1 means placed. :contentReference[oaicite:10]{index=10}
//            while (true)
//            {
//                ct.ThrowIfCancellationRequested();
//                int d = IdCardNative.DetectDocument();
//                if (d == 1) break;
//                await Task.Delay(150, ct);
//            }

//            // 2) Recognize: AutoProcessIDCard :contentReference[oaicite:11]{index=11}
//            int cardType = 0;
//            int mainId = IdCardNative.AutoProcessIDCard(ref cardType);
//            if (mainId <= 0)
//                throw new InvalidOperationException($"AutoProcessIDCard failed. Code={mainId}");

//            // 3) Read doc name
//            string docName = ReadString((sb, ref int len) => IdCardNative.GetIDCardName(sb, ref len), 256);

//            // 4) Sub ID (valid after AutoProcessIDCard) :contentReference[oaicite:12]{index=12}
//            int subId = IdCardNative.GetSubID();

//            // 5) Get all available fields dynamically:
//            // We iterate indexes until "field not exist" (return 3) as described. :contentReference[oaicite:13]{index=13}
//            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
//            ExtractFields(attribute: 1, fields); // 1 = OCR/page fields :contentReference[oaicite:14]{index=14}
//            // If you want chip fields too (attribute 0), you can also call:
//            // ExtractFields(attribute: 0, fields);

//            // 6) Save images. nType bit0..bit4: white, IR, UV, page head, chip head :contentReference[oaicite:15]{index=15}
//            string baseName = Path.Combine(outputFolder, $"passport_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
//            int saveMask = 1 /*white*/ | 8 /*page portrait*/ | 16 /*chip portrait*/;
//            _ = IdCardNative.SaveImageEx(baseName, saveMask);

//            // SDK note: if multiple images, it appends IR/UV/Head/HeadEc etc. :contentReference[oaicite:16]{index=16}
//            // We'll return the base path and let UI load what exists.
//            return new PassportReadResult
//            {
//                MainId = mainId,
//                CardType = cardType,
//                SubId = subId,
//                DocumentName = docName,
//                Fields = fields,
//                SavedImageBasePath = baseName
//            };
//        }

//        private void ExtractFields(int attribute, Dictionary<string, string> fields)
//        {
//            for (int idx = 0; idx < 400; idx++)
//            {
//                // Get field name
//                var nameBuf = new StringBuilder(256);
//                int nameLen = nameBuf.Capacity;

//                int nameRet = IdCardNative.GetFieldNameEx(attribute, idx, nameBuf, ref nameLen);
//                if (nameRet == 3) break;           // field not exist
//                if (nameRet != 0) continue;

//                string fieldName = nameBuf.ToString().Trim();
//                if (string.IsNullOrWhiteSpace(fieldName)) continue;

//                // Get field value
//                var valBuf = new StringBuilder(1024);
//                int valLen = valBuf.Capacity;

//                int valRet = IdCardNative.GetRecogResultEx(attribute, idx, valBuf, ref valLen);
//                if (valRet != 0) continue;

//                string fieldValue = valBuf.ToString().Trim();
//                if (!fields.ContainsKey(fieldName))
//                    fields[fieldName] = fieldValue;
//            }
//        }

//        private static string ReadString(Func<StringBuilder, ref int, int> fn, int initialSize)
//        {
//            var sb = new StringBuilder(initialSize);
//            int len = sb.Capacity;
//            int ret = fn(sb, ref len);
//            if (ret != 0) return string.Empty;
//            return sb.ToString().Trim();
//        }

//        public void Dispose()
//        {
//            if (_initialized)
//            {
//                IdCardNative.FreeIDCard();
//                _initialized = false;
//            }
//        }
//    }

//    public sealed class PassportReadResult
//    {
//        public int MainId { get; init; }
//        public int CardType { get; init; }
//        public int SubId { get; init; }
//        public string DocumentName { get; init; } = "";
//        public Dictionary<string, string> Fields { get; init; } = new();
//        public string SavedImageBasePath { get; init; } = "";
//    }
//}
