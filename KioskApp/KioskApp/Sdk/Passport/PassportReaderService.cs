using System;
using System.IO;

namespace OmniKiosk.Wpf.Sdk.Passport
{
    public sealed class PassportReaderService : IDisposable
    {
        private readonly string _userId;
        private readonly string _libPath;
        private IDCardSdk? _sdk;

        // Tracks which mode the scanner is currently registered for, so
        // callers (and this class's own methods) can tell whether a
        // re-init is needed before use. Passport is the default/normal
        // mode - MyKad mode is only entered deliberately, for the
        // dual-hardware capture flow, and must be explicitly switched back.
        // Passport (single document mode) - kept for the SDK test view and
        // for InitForMyKadCapture/SwitchBackToPassportMode's dual-hardware
        // flow, unchanged. Any = the standard mode now used for real
        // customer scans - registers BOTH document types simultaneously
        // (AddIDCardID accumulates, confirmed from the SDK manual), so a
        // single scan recognizes EITHER an IC or a passport automatically,
        // per instruction to standardize on one reader/one flow for both.
        public enum ScanMode { Passport, MyKad, Any }
        public ScanMode CurrentMode { get; private set; } = ScanMode.Passport;

        // Document MAINIDs, confirmed directly from the SDK vendor's own
        // "Document Recognition SDK user manual" (Appendix B, Document main
        // type table): Passport = 13 (single, universal MAINID covering
        // every ICAO 9303-compliant e-passport regardless of issuing
        // country - not a country-specific value), Malaysian ID card
        // (MyKad) = 2001.
        private const int MAINID_PASSPORT = 13;
        private const int MAINID_MYKAD = 2001;

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

            var ret = _sdk.InitIDCard(_userId, 0, _libPath);
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

            // Standard scan mode now registers BOTH document types at once
            // (AddIDCardID accumulates per the SDK manual, section 3.1.4.8 -
            // calling it twice adds both types to the classifier rather than
            // replacing the first). nSubID={0} with count=1 is the
            // documented wildcard for "all sub-types of this document type"
            // for EACH one. This lets a single scan recognize either an IC
            // or a passport automatically - AutoProcessIDCard's returned
            // mainId tells the caller which one was actually placed.
            _sdk.ResetIDCardID();
            _sdk.AddIDCardID(MAINID_PASSPORT, new[] { 0 }, 1);
            _sdk.AddIDCardID(MAINID_MYKAD, new[] { 0 }, 1);
            CurrentMode = ScanMode.Any;

            // Read VIZ + chip DG1/DG2
            _sdk.SetRecogVIZ(true);
            _sdk.SetRecogDG(6);         // DG1 (2) + DG2 (4) = 6
            _sdk.SetAnalyseMRZ(true);

            // Save images: White(1) + OCR Head(8) + Chip Head(16) = 25
            _sdk.SetSaveImageType(25);
        }

        // Switches the SAME scanner to recognize a MyKad optically instead
        // of a passport - for the dual-hardware flow: IC chip reader
        // already gave reliable extracted fields for a NEW customer, but
        // MyKad's chip-reader path has no way to produce a visual image of
        // the card surface (see EkycFaceMatchClient.VerifyPassportAuthenticityAsync
        // remarks - OkayDoc-MyKad needs an optical image, which the chip
        // reader cannot supply). This repurposes the passport reader's
        // existing camera for that image capture only - it does NOT
        // attempt to re-extract fields the chip reader already gave us
        // reliably; see TryCaptureMyKadImage below.
        //
        // *** HARDWARE VALIDATION NEEDED ***: MAINID 2001 and the
        // wildcard-subID convention are both confirmed directly from the
        // vendor's own SDK manual, and AutoProcessIDCard's return-code
        // behavior (see TryCaptureMyKadImage) is documented to work
        // identically across document types. What is NOT yet confirmed is
        // whether the passport reader's physical scanning surface/guide
        // can accommodate a rigid, credit-card-sized MyKad the way it
        // accommodates a passport's photo page - that is an ergonomic
        // question a real hardware test needs to answer, not something
        // verifiable from documentation.
        public void InitForMyKadCapture()
        {
            if (_sdk == null)
                throw new InvalidOperationException("Call Init() before InitForMyKadCapture().");

            _sdk.ResetIDCardID();
            _sdk.AddIDCardID(MAINID_MYKAD, new[] { 0 }, 1);
            CurrentMode = ScanMode.MyKad;
        }

        // Switches back to normal passport-only recognition after a MyKad
        // capture - call this once the dual-hardware flow's MyKad step is
        // done, before this scanner is used for a normal passport scan
        // again (e.g., the next customer).
        // Switches back to the standard scan mode (both document types
        // registered together) after a MyKad-only capture cycle - not
        // passport-only anymore, since Any is now the normal resting
        // state for real customer scans (see Init()'s own comment).
        public void SwitchBackToPassportMode()
        {
            if (_sdk == null) return;
            _sdk.ResetIDCardID();
            _sdk.AddIDCardID(MAINID_PASSPORT, new[] { 0 }, 1);
            _sdk.AddIDCardID(MAINID_MYKAD, new[] { 0 }, 1);
            CurrentMode = ScanMode.Any;
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
        public bool TryReadPassport(out PassportDoc doc, out string? portraitPath, out string? fullPageImagePath)
        {
            doc = new PassportDoc();
            portraitPath = null;
            fullPageImagePath = null;

            if (_sdk == null) return false;
            if (CurrentMode != ScanMode.Passport)
                throw new InvalidOperationException("Scanner is in MyKad capture mode - call SwitchBackToPassportMode() first.");

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
            // Address (chip index 11, per the vendor SDK manual's "4.2.2
            // Chip field index" table - not all passports carry this on
            // the chip, it's country-optional). Added alongside the other
            // attr=0 fields above and inheriting the SAME caveat: real
            // hardware testing already found chip results (attr=0) come
            // back empty on this setup, so this is unlikely to populate
            // today - kept here, commented out like its neighbors, ready
            // to activate if/when chip reading is working, rather than
            // silently implied to work now.
            //doc.Address = _sdk.GetRecogResultStr(0, 11);

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

            // baseFile IS the full page scan SaveImageEx just wrote above -
            // it was already being created on every read, just never
            // returned to the caller. This is what OkayDoc's passport
            // check needs (the full photo-page image), as distinct from
            // portraitPath's cropped face-only crop.
            fullPageImagePath = File.Exists(baseFile) ? baseFile : null;

            return true;
        }

        // ============================================================
        // TryReadAnyDocument - the standard scan method now, used for
        // BOTH IC and Passport, per instruction to standardize on one
        // reader/one flow rather than a separate chip-based path for IC.
        // Requires Init() to have been called (registers both MAINID 13
        // and 2001 together - see Init()'s own comment). Branches on
        // AutoProcessIDCard's returned mainId to know which document was
        // actually placed, and extracts fields using that document's own
        // known field layout - passport's MRZ/VIZ table for MAINID 13/-8/
        // -1115/-1117, MyKad's field table (confirmed from the vendor SDK
        // manual, same indices as MyKadOpticalFields) for MAINID 2001.
        //
        // doc.DetectedDocType is set to "Passport" or "MyKad" so the
        // caller knows which OkayDoc variant to run and how to interpret
        // fields that don't apply to both (DateOfExpiry/PlaceOfIssue are
        // passport-only and stay null for MyKad).
        // ============================================================
        public bool TryReadAnyDocument(out PassportDoc doc, out string? portraitPath, out string? fullPageImagePath)
        {
            doc = new PassportDoc();
            portraitPath = null;
            fullPageImagePath = null;

            if (_sdk == null) return false;
            if (CurrentMode != ScanMode.Any)
                throw new InvalidOperationException("Scanner is not in unified (Any) scan mode - call Init() first.");

            int cardType = 0;
            int mainId = _sdk.AutoProcessIDCard(ref cardType);

            bool isPassportFamily = mainId > 0 || mainId == -8 || mainId == -1115 || mainId == -1117;
            bool isMyKad = mainId == MAINID_MYKAD;

            if (!isPassportFamily && !isMyKad)
                return false;

            if (isMyKad)
            {
                doc.DetectedDocType = "MyKad";
                // Field indices confirmed from the vendor SDK manual's
                // "4.2.3 Field definition of each document" table for
                // Malaysian ID card (MAINID 2001) - same table
                // MyKadOpticalFields above is built from.
                doc.PassportNumber = _sdk.GetRecogResultStr(1, 1);   // Citizen ID Card Number - reused field, same meaning as an ID number for either document type
                doc.FullName = _sdk.GetRecogResultStr(1, 2);
                doc.Sex = _sdk.GetRecogResultStr(1, 3);
                doc.DateOfBirth = _sdk.GetRecogResultStr(1, 4);
                doc.Nationality = _sdk.GetRecogResultStr(1, 5);
                doc.Address = _sdk.GetRecogResultStr(1, 6);
                // DateOfExpiry, PlaceOfBirth, PlaceOfIssue, MRZ lines: not
                // applicable to MyKad, left null.
            }
            else
            {
                doc.DetectedDocType = "Passport";
                // Same OCR/VIZ field layout as TryReadPassport above.
                doc.DocumentType = _sdk.GetRecogResultStr(1, 0);
                doc.PassportNumber = _sdk.GetRecogResultStr(1, 1) ?? _sdk.GetRecogResultStr(1, 13);
                doc.FullName = _sdk.GetRecogResultStr(1, 3);
                doc.Sex = _sdk.GetRecogResultStr(1, 4);
                doc.DateOfBirth = _sdk.GetRecogResultStr(1, 5);
                doc.DateOfExpiry = _sdk.GetRecogResultStr(1, 6);
                doc.Nationality = _sdk.GetRecogResultStr(1, 7) ?? _sdk.GetRecogResultStr(1, 12);
                doc.Surname = _sdk.GetRecogResultStr(1, 8);
                doc.GivenNames = _sdk.GetRecogResultStr(1, 9);
                doc.MrzLine1 = _sdk.GetRecogResultStr(1, 10);
                doc.MrzLine2 = _sdk.GetRecogResultStr(1, 11);
                doc.PlaceOfBirth = _sdk.GetRecogResultStr(1, 14);
                doc.PlaceOfIssue = _sdk.GetRecogResultStr(1, 15);
                doc.DateOfIssue = _sdk.GetRecogResultStr(1, 16);
                doc.PersonalNumber = _sdk.GetRecogResultStr(1, 21);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "KioskPassport");
            Directory.CreateDirectory(tempDir);

            var baseFile = Path.Combine(tempDir, "Document.jpg");
            SafeDelete(baseFile);
            SafeDelete(InsertSuffix(baseFile, "Head"));
            SafeDelete(InsertSuffix(baseFile, "HeadEC"));

            _sdk.SaveImageEx(baseFile, 25);

            var chipHead = InsertSuffix(baseFile, "HeadEC");
            var ocrHead = InsertSuffix(baseFile, "Head");
            portraitPath = File.Exists(chipHead) ? chipHead : (File.Exists(ocrHead) ? ocrHead : null);
            fullPageImagePath = File.Exists(baseFile) ? baseFile : null;

            return true;
        }

        // Captures a full optical image of a MyKad placed on this scanner,
        // for OkayDoc-MyKad's authenticity checks - NOT for re-extracting
        // identity fields (the IC chip reader already provided those
        // reliably; this method deliberately does not attempt to parse
        // MyKad's own field layout, which is completely different from
        // passport's MRZ-based one).
        //
        // Requires InitForMyKadCapture() to have been called first. Returns
        // false (with errorReason set) if nothing was recognized, or if
        // something WAS recognized but as a different document type than
        // expected (e.g. a passport got placed here by mistake) - this is a
        // deliberate check, since accepting any successful recognition
        // regardless of type could silently save the wrong kind of image.
        // Extracted text fields from the MyKad's OPTICAL recognition
        // (MAINID 2001), per the vendor SDK manual's "4.2.3 Field
        // definition of each document" table: index 0=Reserve,
        // 1=Citizen ID Card Number, 2=Name, 3=Gender, 4=Birth Date,
        // 5=Nationality, 6=Address.
        //
        // *** attr VALUE NOT CONFIRMED *** - the manual's table gives the
        // field INDEX for each item, but I have not found an explicit
        // confirmed example showing which attr number to pass alongside
        // that index for THIS specific document type (passport's OCR
        // fields use attr=1, confirmed by the working code above - MyKad's
        // equivalent attr is assumed to also be 1, by analogy, not
        // verified). If these come back empty/wrong on real hardware,
        // this attr value is the first thing to check.
        //
        // This is supplementary to the IC chip reader's own extraction
        // (IcReaderService.cs) - NOT a replacement. The chip data is
        // already trusted; this exists mainly to capture Address, which
        // the chip reader path does not currently expose at all.
        public sealed class MyKadOpticalFields
        {
            public string? CitizenIdCardNumber { get; set; }
            public string? Name { get; set; }
            public string? Gender { get; set; }
            public string? BirthDate { get; set; }
            public string? Nationality { get; set; }
            public string? Address { get; set; }
        }

        public bool TryCaptureMyKadImage(out string? fullPageImagePath, out MyKadOpticalFields? extractedFields, out string? errorReason)
        {
            fullPageImagePath = null;
            extractedFields = null;
            errorReason = null;

            if (_sdk == null) { errorReason = "SDK not initialized."; return false; }
            if (CurrentMode != ScanMode.MyKad)
            {
                errorReason = "Scanner is not in MyKad capture mode - call InitForMyKadCapture() first.";
                return false;
            }

            int cardType = 0;
            int mainId = _sdk.AutoProcessIDCard(ref cardType);

            // Same accepted-code set as TryReadPassport - per the SDK
            // manual, these codes carry the same meaning regardless of
            // which document MAINID was registered. -8 specifically
            // ("chip read failed, page recognition succeeded") is the
            // EXPECTED code here every time, since this scanner has no
            // chip-reading capability for a MyKad at all - only the
            // optical/page path can ever succeed through this hardware.
            if (mainId == MAINID_MYKAD || mainId == -8)
            {
                // fall through - treated as a successful MyKad capture
            }
            else if (mainId > 0)
            {
                errorReason = $"Recognized a different document type (MAINID={mainId}) - expected a MyKad. Please place the MyKad card, not another document.";
                return false;
            }
            else
            {
                errorReason = $"No document recognized (code={mainId}). Please place the MyKad flat on the scanner.";
                return false;
            }

            // See the attr-value caveat on MyKadOpticalFields above. A
            // failure to extract any of these does not fail the overall
            // capture - the image itself (below) is still the primary,
            // load-bearing output of this method.
            extractedFields = new MyKadOpticalFields
            {
                CitizenIdCardNumber = _sdk.GetRecogResultStr(1, 1),
                Name = _sdk.GetRecogResultStr(1, 2),
                Gender = _sdk.GetRecogResultStr(1, 3),
                BirthDate = _sdk.GetRecogResultStr(1, 4),
                Nationality = _sdk.GetRecogResultStr(1, 5),
                Address = _sdk.GetRecogResultStr(1, 6)
            };

            var tempDir = Path.Combine(Path.GetTempPath(), "KioskPassport");
            Directory.CreateDirectory(tempDir);

            var baseFile = Path.Combine(tempDir, "MyKadOptical.jpg");
            SafeDelete(baseFile);

            _sdk.SaveImageEx(baseFile, 25);

            if (!File.Exists(baseFile))
            {
                errorReason = "Document was recognized but no image was saved.";
                return false;
            }

            fullPageImagePath = baseFile;
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
        // Chip-only field (see the commented-out read above) - not
        // populated via the OCR/VIZ path, since ICAO 9303's visual
        // inspection zone doesn't carry an address field the way some
        // national ID chips do.
        public string? Address { get; set; }
        // Set by TryReadAnyDocument - "Passport" or "MyKad", identifying
        // which document type AutoProcessIDCard actually recognized. Null
        // when populated by the older, document-type-specific
        // TryReadPassport (still used by the dual-hardware flow and the
        // SDK test view), since the caller already knows the type there.
        public string? DetectedDocType { get; set; }
        public string? MrzLine1 { get; set; }
        public string? MrzLine2 { get; set; }
    }
}