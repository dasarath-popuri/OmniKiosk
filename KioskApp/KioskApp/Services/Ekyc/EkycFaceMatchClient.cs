using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OmniKiosk.Wpf.Config;

namespace OmniKiosk.Wpf.Services.Ekyc
{
    public class GenericEkycRequest
    {
        public string mobile_number { get; set; } = "";
        public string ip_address { get; set; } = "";
        public int? sender_id { get; set; }
    }

    public class CreateJourneyIdRequest : GenericEkycRequest { }

    public class CreateJourneyIdResponse
    {
        public string? response_message { get; set; }
        public bool success { get; set; }
        public string? response_message1 { get; set; }
        public JourneyIdResult? journeyidresponse { get; set; }
    }

    public class JourneyIdResult
    {
        public string? status { get; set; }
        public string? message { get; set; }
        public string? journeyId { get; set; }
    }

    public class CentralizeOkayFaceRequest : GenericEkycRequest
    {
        public OkayFaceInner eKYCrequest { get; set; } = new();
    }

    public class OkayFaceInner
    {
        public string journeyId { get; set; } = "";
        public string livenessDetection { get; set; } = "true";
        public string imageIdCardBase64 { get; set; } = "";
        public string imageBestBase64 { get; set; } = "";
    }

    public class CentralizeOkayFaceResponse
    {
        public string? response_message { get; set; }
        public bool success { get; set; }
        public string? response_message1 { get; set; }
        public OkayFaceResult? centralizeOkayFaceresponse { get; set; }
    }

    public class OkayFaceResult
    {
        public string? status { get; set; }
        public string? message { get; set; }
        public string? messageCode { get; set; }
        public ImageBestLiveness? imageBestLiveness { get; set; }
        public ResultIdCard? result_idcard { get; set; }
    }

    public class ImageBestLiveness
    {
        public double probability { get; set; }
        public double score { get; set; }
        public decimal quality { get; set; }
    }

    public class ResultIdCard
    {
        public double confidence { get; set; }
    }

    // ============================================================
    // OkayID - sends complete document images to Innov8tif for OCR
    // extraction, independent of the hardware reader's own extraction.
    // Field names verified directly against Innov8tif's EMAS eKYC Portal
    // docs (api2-ekycportal.innov8tif.com) - these differ from OkayFace's
    // field names above (base64ImageString here, not imageIdCardBase64/
    // imageBestBase64).
    // ============================================================

    public class CentralizeOkayIdRequest : GenericEkycRequest
    {
        public OkayIdInner eKYCrequest { get; set; } = new();
    }

    public class OkayIdInner
    {
        public string journeyId { get; set; } = "";
        public string base64ImageString { get; set; } = "";   // front id card/passport image
        public string? backImage { get; set; }                 // back id card image, MyKad only
        public string imageFormat { get; set; } = "JPG";
        public bool docTypeEnabled { get; set; } = true;
        public bool imageEnabled { get; set; } = false;
        public bool faceImageEnabled { get; set; } = false;
        public bool cambodia { get; set; } = false;
    }

    public class CentralizeOkayIdResponse
    {
        public string? status { get; set; }
        public string? message { get; set; }
        public string? documentType { get; set; }
        public List<OkayIdResultWrapper>? result { get; set; }
    }

    public class OkayIdResultWrapper
    {
        public OkayIdVerifiedFields? ListVerifiedFields { get; set; }
    }

    public class OkayIdVerifiedFields
    {
        public List<OkayIdFieldMap>? pFieldMaps { get; set; }
    }

    public class OkayIdFieldMap
    {
        public int FieldType { get; set; }
        public string? Field_Visual { get; set; }
        public string? Field_MRZ { get; set; }
    }

    // ============================================================
    // OkayDoc (Passport variant only - MyKad's chip reader has no optical
    // scan capability, see VerifyPassportAuthenticityAsync remarks below).
    // Field names verified directly against Innov8tif's EMAS eKYC Portal docs.
    // ============================================================

    public class CentralizeOkayDocPassportRequest : GenericEkycRequest
    {
        public OkayDocPassportInner eKYCrequest { get; set; } = new();
    }

    public class OkayDocPassportInner
    {
        public string journeyId { get; set; } = "";
        public string type { get; set; } = "passport";
        public string version { get; set; } = "3";
        public string country { get; set; } = "OTHER";   // per Innov8tif docs: per-country codes deprecated, always use OTHER
        public string halfSizeImage { get; set; } = "";  // full passport photo-page image, base64
    }

    // ============================================================
    // OkayDoc, Non-Passport (MyKad). Confirmed directly from Innov8tif's
    // live docs (api2-ekycportal.innov8tif.com/emas-ekyc-portal/
    // centralized-okaydoc/non-passport) - request field names, version 7
    // check-flag names, and the recommended-threshold table are all taken
    // from that source, not inferred by analogy with the Passport variant.
    //
    // Field name is idImageBase64Image here (NOT halfSizeImage, which is
    // Passport-only) - confirmed from the documented Postman example for
    // MyKad Version 7.
    // ============================================================

    public class CentralizeOkayDocMyKadRequest : GenericEkycRequest
    {
        public OkayDocMyKadInner eKYCrequest { get; set; } = new();
    }

    public class OkayDocMyKadInner
    {
        public string journeyId { get; set; } = "";
        public string type { get; set; } = "nonpassport";
        public string version { get; set; } = "7";
        public string docType { get; set; } = "mykad";
        public string idImageBase64Image { get; set; } = "";
        public bool landmarkCheck { get; set; } = true;
        public bool fontCheck { get; set; } = true;
        public bool microprintCheck { get; set; } = true;
        public bool photoSubstitutionCheck { get; set; } = true;
        public bool icTypeCheck { get; set; } = true;
        public bool colorMode { get; set; } = true;
        public bool hologram { get; set; } = true;
        public bool screenDetection { get; set; } = true;
        public bool ghostPhotoColorDetection { get; set; } = true;
        public bool idBlurDetection { get; set; } = true;
        public bool idBrightnessDetection { get; set; } = true;
    }

    public class CentralizeOkayDocResponse
    {
        public string? status { get; set; }
        public string? messageCode { get; set; }
        public string? id { get; set; }
        public List<OkayDocMethodResult>? methodList { get; set; }
    }

    public class OkayDocMethodResult
    {
        public string? method { get; set; }
        public string? label { get; set; }
        public List<OkayDocComponent>? componentList { get; set; }
    }

    public class OkayDocComponent
    {
        public string? code { get; set; }
        public string? label { get; set; }
        public string? value { get; set; }   // "Pass" / "Fail" for most checks
    }

    public sealed class DocumentAuthenticityOutcome
    {
        public bool CallSucceeded { get; set; }
        public bool AllChecksPassed { get; set; }
        public List<(string Check, string Result)> FailedChecks { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    // ============================================================
    // Scorecard - the final combined decision from all four eKYC calls
    // (OkayID, OkayDoc, OkayFace, OkayLive) run against one shared journeyId.
    //
    // Response shape CONFIRMED from a real sample response (not inferred):
    //   {
    //     "status": "success", "messageCode": null, "message": null,
    //     "scorecardResultList": [
    //       { "scorecardStatus": "clear", "docType": "mykad_back",
    //         "checkResultList": [ { "checkType": "liveFaceCheck", "checkStatus": "P" }, ... ] },
    //       { "scorecardStatus": "cautious", "docType": "mykad_new",
    //         "checkResultList": [ ..., { "checkType": "hologram", "checkStatus": "F" }, ... ] }
    //     ]
    //   }
    //
    // scorecardResultList holds one entry per document type processed in
    // this journey (e.g. MyKad front and back) - ALL entries must be
    // "clear" for the overall verification to pass; if any one document in
    // the list isn't clear, the whole result is treated as not passed.
    //
    // checkStatus is "P" (Pass), "C" (Cautious), or "F" (Fail) per
    // individual check - but scorecardStatus is NOT simply "any F means
    // reject": the confirmed sample above shows scorecardStatus="cautious"
    // on a document whose checkResultList contains an actual "F" (hologram)
    // alongside a "C" (microprint) and eight "P"s. Innov8tif's own
    // aggregation logic behind scorecardStatus is therefore doing some
    // weighting we don't have visibility into - this code trusts
    // scorecardStatus directly rather than trying to recompute pass/fail
    // itself from the individual checkResultList entries.
    //
    // Only "clear" is treated as a pass. "cautious" is confirmed to be a
    // real value and is deliberately NOT treated as passing - consistent
    // with this project's fail-safe posture for a three-state result
    // (see the OkayDoc-MyKad "Cautious" handling elsewhere in this file
    // for the same reasoning applied there). Any other value (including
    // one not seen in the confirmed sample, e.g. a presumed "reject") is
    // also treated as not passing.
    // ============================================================

    public class GetScorecardRequest : GenericEkycRequest
    {
        public ScorecardInner eKYCrequest { get; set; } = new();
    }

    public class ScorecardInner
    {
        public string journeyId { get; set; } = "";
    }

    public class ScorecardCheckResultItem
    {
        public string? checkType { get; set; }
        public string? checkStatus { get; set; } // P | C | F
    }

    public class ScorecardDocumentResult
    {
        public string? scorecardStatus { get; set; } // clear | cautious | (other values not yet seen)
        public string? docType { get; set; }
        public List<ScorecardCheckResultItem>? checkResultList { get; set; }
    }

    public class GetScorecardResponse
    {
        public string? status { get; set; }
        public string? messageCode { get; set; }
        public string? message { get; set; }
        public List<ScorecardDocumentResult>? scorecardResultList { get; set; }
    }

    public sealed class ScorecardOutcome
    {
        public bool CallSucceeded { get; set; }
        public bool? Passed { get; set; }          // null = could not determine, treat as reject
        public string? ErrorMessage { get; set; }
        public string? RawJson { get; set; }
        public List<(string DocType, string ScorecardStatus)> DocumentResults { get; set; } = new();
    }

    public class TokenRequest
    {
        public int UserId { get; set; }
        public string MobileNo { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class FaceMatchOutcome
    {
        public bool CallSucceeded { get; set; }
        public bool Matched { get; set; }
        public double? ScorePercent { get; set; }
        public double? LivenessProbability { get; set; }
        public decimal? LivenessQuality { get; set; }
        public string? Status { get; set; }
        public string? MessageCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? FriendlyMessage { get; set; }
    }

    public sealed class EkycFaceMatchClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        private static readonly (string Label, string Url)[] BaseUrls =
        {
            ("Primary", KioskSettings.EkycApiBaseUrl),
            ("Fallback", KioskSettings.EkycApiBaseUrlFallback)
        };

        private static readonly Dictionary<string, string> FriendlyMessages = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FACE_NOT_FOUND"] = "No face detected — please look directly at the camera.",
            ["FACE_IS_OCCLUDED"] = "Please remove your mask, glasses, or anything covering your face.",
            ["ID_FACE_IS_OCCLUDED"] = "Please remove anything covering the photo on your ID.",
            ["TOO_MANY_FACES"] = "More than one face detected — please make sure it's just you in frame.",
            ["FACE_ANGLE_TOO_LARGE"] = "Please face the camera directly.",
            ["FACE_TOO_SMALL"] = "Please move closer to the camera.",
            ["FACE_TOO_CLOSE"] = "Please move back slightly from the camera.",
            ["FACE_CROPPED"] = "Please make sure your whole face is in frame.",
            ["FACE_CLOSE_TO_BORDER"] = "Please center your face in the frame.",
            ["EYES_CLOSED"] = "Please keep your eyes open.",
            ["UNRECOGNIZED_IMAGE"] = "Image could not be read — please retry.",
            ["UNRESOLVED_PIC_CONTENT"] = "Image could not be read — please retry.",
            ["PAYLOAD_TOO_LARGE"] = "Image too large — please contact support.",
            ["INVALID_JOURNEY_ID"] = "Session expired — please restart this step.",
            ["ERROR_IMAGE_ATTACK_DETECTED"] = "Verification failed — please try again in person.",
        };

        private static string? _cachedToken;
        private static DateTime _tokenExpiresUtc = DateTime.MinValue;
        private static readonly SemaphoreSlim _tokenLock = new(1, 1);

        private static void InvalidateToken()
        {
            _cachedToken = null;
            _tokenExpiresUtc = DateTime.MinValue;
        }

        private async Task<(string? token, string? error)> EnsureBearerTokenAsync(CancellationToken ct)
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiresUtc)
                return (_cachedToken, null);

            await _tokenLock.WaitAsync(ct);
            try
            {
                if (_cachedToken != null && DateTime.UtcNow < _tokenExpiresUtc)
                    return (_cachedToken, null);

                var req = new TokenRequest
                {
                    UserId = 0,
                    MobileNo = NotificationEngineCrypto.Encrypt(KioskSettings.EkycLoginId, KioskSettings.EkycSharedSecretKey),
                    Password = NotificationEngineCrypto.Encrypt(KioskSettings.EkycLoginPassword, KioskSettings.EkycSharedSecretKey)
                };
                var json = JsonSerializer.Serialize(req);
                var attempts = new List<string>();

                foreach (var (label, baseUrl) in BaseUrls)
                {
                    if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                    try
                    {
                        var url = baseUrl.TrimEnd('/') + "/Token";
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        using var resp = await _http.PostAsync(url, content, ct);
                        var body = await resp.Content.ReadAsStringAsync(ct);

                        if (!resp.IsSuccessStatusCode)
                        {
                            attempts.Add($"{label}: HTTP {(int)resp.StatusCode} {body}");
                            continue;
                        }

                        string? token = body?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            attempts.Add($"{label}: empty token in response");
                            continue;
                        }

                        _cachedToken = token;
                        _tokenExpiresUtc = DateTime.UtcNow.AddMinutes(17);
                        return (_cachedToken, null);
                    }
                    catch (Exception ex)
                    {
                        attempts.Add($"{label}: {ex.Message}");
                    }
                }

                return (null, "TOKEN ACQUISITION FAILED - " + string.Join(" | ", attempts));
            }
            finally
            {
                _tokenLock.Release();
            }
        }
        private async Task<(HttpStatusCode status, string body)> SendAuthedAsync(
            string url, string jsonBody, string token, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(request, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.StatusCode, body);
        }

        public async Task<(bool ok, string? journeyId, string? error)> CreateJourneyIdAsync(
            string? referenceId = null, CancellationToken ct = default)
        {
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null) return (false, null, tokenError);

            var req = new CreateJourneyIdRequest
            {
                mobile_number = string.IsNullOrWhiteSpace(referenceId) ? "KIOSK" : referenceId,
                ip_address = GetLocalIp(),
                sender_id = null
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/eKYC_Create_JourneryId";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<CreateJourneyIdResponse>(body);
                    if (parsed?.success == true && !string.IsNullOrWhiteSpace(parsed.journeyidresponse?.journeyId))
                        return (true, parsed.journeyidresponse!.journeyId, null);

                    attempts.Add($"{label} ({baseUrl}): {parsed?.response_message1 ?? parsed?.journeyidresponse?.message ?? "journey creation failed"}");
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return (false, null, string.Join(" | ", attempts));
        }

        // ============================================================
        // VerifyDocumentAsync - OkayID.
        // extractedFields is keyed by Innov8tif's numeric FieldType (2 =
        // document/ID number, 25 = name, 5 = date of birth, 12 = gender,
        // per Innov8tif's Malaysia field-type reference).
        // ============================================================
        public async Task<(bool ok, Dictionary<int, string> extractedFields, string? documentType, string? error)>
            VerifyDocumentAsync(string journeyId, string frontImageBase64, string? backImageBase64 = null, CancellationToken ct = default)
        {
            var empty = new Dictionary<int, string>();
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null) return (false, empty, null, tokenError);

            var req = new CentralizeOkayIdRequest
            {
                mobile_number = "KIOSK",
                ip_address = GetLocalIp(),
                sender_id = null,
                eKYCrequest = new OkayIdInner
                {
                    journeyId = journeyId,
                    base64ImageString = frontImageBase64,
                    backImage = backImageBase64,
                    docTypeEnabled = true,
                    imageEnabled = false,
                    faceImageEnabled = false
                }
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/eKYC_CentralizeOkayID_request";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<CentralizeOkayIdResponse>(body);
                    if (parsed?.status != "success")
                    {
                        attempts.Add($"{label} ({baseUrl}): {parsed?.message ?? "OkayID call did not report success"}");
                        continue;
                    }

                    var fields = new Dictionary<int, string>();
                    var maps = parsed.result?.FirstOrDefault()?.ListVerifiedFields?.pFieldMaps;
                    if (maps != null)
                    {
                        foreach (var m in maps)
                        {
                            if (m.Field_Visual != null)
                                fields[m.FieldType] = m.Field_Visual;
                        }
                    }

                    return (true, fields, parsed.documentType, null);
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return (false, empty, null, string.Join(" | ", attempts));
        }

        // ============================================================
        // VerifyPassportAuthenticityAsync - OkayDoc, Passport variant only.
        //
        // MyKad's OkayDoc variant is NOT implemented here. Its checks
        // (font, microprint, hologram, photo substitution, color mode,
        // screen detection) all require a visual/optical image of the
        // card's physical surface - but the current MyKad integration
        // (Sdk/IC/IcReaderService.cs) reads the card via a contact chip
        // interface, which extracts data fields and one embedded chip
        // photo, never an optical scan of the card face. There is
        // currently no camera or scanner in the MyKad path capable of
        // producing the input OkayDoc-MyKad needs - a hardware gap, not a
        // missing method here.
        // ============================================================
        public async Task<DocumentAuthenticityOutcome> VerifyPassportAuthenticityAsync(
            string journeyId, string fullPageImageBase64, CancellationToken ct = default)
        {
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null)
                return new DocumentAuthenticityOutcome { CallSucceeded = false, ErrorMessage = tokenError };

            var req = new CentralizeOkayDocPassportRequest
            {
                mobile_number = "KIOSK",
                ip_address = GetLocalIp(),
                sender_id = null,
                eKYCrequest = new OkayDocPassportInner
                {
                    journeyId = journeyId,
                    halfSizeImage = fullPageImageBase64
                }
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/eKYC_Centralize_OkayDoc_passport_request";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<CentralizeOkayDocResponse>(body);
                    if (parsed?.status != "success")
                    {
                        attempts.Add($"{label} ({baseUrl}): OkayDoc call did not report success");
                        continue;
                    }

                    return BuildDocumentAuthenticityOutcome(parsed);
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return new DocumentAuthenticityOutcome { CallSucceeded = false, ErrorMessage = string.Join(" | ", attempts) };
        }

        // ============================================================
        // Shared OkayDoc response parser - used by VerifyPassportAuthenticityAsync
        // above. NOTE: VerifyMyKadAuthenticityAsync below does NOT use this -
        // it has its own, more detailed parser, since MyKad's response mixes
        // numeric threshold scores (landmark, microprint) with Pass/Fail
        // strings, which this simpler parser doesn't handle. Kept here,
        // simple, for Passport specifically: FIXES a real bug found while
        // verifying against Innov8tif's own confirmed sample response: the
        // "landmark" method's components return NUMERIC SCORE STRINGS as
        // their value (e.g. "93.5", "91.97"), not "Pass"/"Fail" the way
        // every other method does. The original code treated anything not
        // literally equal to "Pass" as a failure - which would have
        // incorrectly flagged every landmark check as failed, every time,
        // since a landmark's value is never the literal string "Pass".
        //
        // Fix: landmark-method components are treated as informational
        // only and excluded from the pass/fail gate entirely, rather than
        // inventing a numeric threshold that hasn't been confirmed
        // anywhere in the docs. Every OTHER method's components (which do
        // return "Pass"/"Fail" per the confirmed sample) still require an
        // exact "Pass" to count as passed.
        // ============================================================
        private static DocumentAuthenticityOutcome BuildDocumentAuthenticityOutcome(CentralizeOkayDocResponse parsed)
        {
            var failed = new List<(string, string)>();
            foreach (var method in parsed.methodList ?? new())
            {
                bool isLandmark = string.Equals(method.method, "landmark", StringComparison.OrdinalIgnoreCase);

                foreach (var comp in method.componentList ?? new())
                {
                    if (isLandmark) continue; // informational score, not a pass/fail gate - see method comment above

                    if (!string.Equals(comp.value, "Pass", StringComparison.OrdinalIgnoreCase))
                        failed.Add((comp.label ?? comp.code ?? method.method ?? "unknown check", comp.value ?? "no result"));
                }
            }

            return new DocumentAuthenticityOutcome
            {
                CallSucceeded = true,
                AllChecksPassed = failed.Count == 0,
                FailedChecks = failed
            };
        }

        // Landmark checks confirmed from Innov8tif's docs - each has its
        // own numeric confidence score (e.g. "70.3362"), compared against
        // this threshold, NOT a literal "Pass"/"Fail" string the way most
        // other MyKad checks work. Codes for header/logo/flag/chip/hibiscus
        // are confirmed directly from a real sample response; "l-msc" is
        // inferred by following that same "l-" naming pattern, since MSC
        // appears in the threshold table but I did not find it in a
        // captured sample response - flagged here as the one inferred code
        // in this table, not a confirmed fact.
        private static readonly Dictionary<string, double> MyKadLandmarkThresholds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["l-mykad-header"] = 30,
            ["l-mykad-logo"] = 30,
            ["l-my-flag-logo"] = 30,
            ["l-chip"] = 30,
            ["l-hibiscus"] = 30,
            ["l-msc"] = 30, // inferred code - see remark above
        };

        // Microprint score is documented as two possible thresholds (41
        // "less strict", 45 "more strict") rather than one fixed number.
        // 41 (the less strict bound) is used here - the more conservative
        // choice for not rejecting a genuine customer's card, matching
        // this project's general fail-safe-toward-compliance posture
        // applied in the other direction: strict enough to still function
        // as a real check, not so strict it manufactures false rejections.
        private const double MyKadMicroprintThreshold = 41;

        // ============================================================
        // VerifyMyKadAuthenticityAsync - OkayDoc, Non-Passport (MyKad)
        // variant. Confirmed request/response shape and Version 7
        // threshold table, both taken directly from Innov8tif's live docs
        // (see the class-level remarks on OkayDocMyKadInner above) - not
        // inferred from the Passport variant.
        //
        // Response shape is genuinely mixed, unlike Passport's simple
        // "every component must say Pass": landmark items ("l-" prefixed
        // codes) and the microprint item carry NUMERIC confidence scores
        // compared against a threshold; every other item carries a literal
        // status string that must be exactly "Pass" (both "Cautious" and
        // "Fail" are treated as not passing - a fail-safe choice for a
        // three-state result, consistent with how the rest of this project
        // treats ambiguous results). "MyKad Type" (New IC / Old IC) is
        // informational only and is excluded from the pass/fail
        // determination entirely - it is a document detail, not a check
        // result.
        // ============================================================
        public async Task<DocumentAuthenticityOutcome> VerifyMyKadAuthenticityAsync(
            string journeyId, string idImageBase64, CancellationToken ct = default)
        {
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null)
                return new DocumentAuthenticityOutcome { CallSucceeded = false, ErrorMessage = tokenError };

            var req = new CentralizeOkayDocMyKadRequest
            {
                mobile_number = "KIOSK",
                ip_address = GetLocalIp(),
                sender_id = null,
                eKYCrequest = new OkayDocMyKadInner
                {
                    journeyId = journeyId,
                    idImageBase64Image = idImageBase64
                }
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    // ASSUMPTION FLAGGED: this exact URL path is inferred by
                    // following the established naming convention seen in
                    // every other confirmed endpoint in this file
                    // (eKYC_CentralizeOkayID_request, eKYC_Centralize_
                    // OkayDoc_passport_request, eKYC_CentralizeOkayFace_
                    // request) - I do not have direct access to
                    // NotificationEngine's source in this session to
                    // confirm this specific path, only the request/response
                    // shape (confirmed from Innov8tif's own docs above).
                    // Verify this path against the actual NotificationEngine
                    // controller before relying on it.
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/eKYC_Centralize_OkayDoc_nonpassport_request";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<CentralizeOkayDocResponse>(body);
                    if (parsed?.status != "success")
                    {
                        attempts.Add($"{label} ({baseUrl}): OkayDoc (MyKad) call did not report success");
                        continue;
                    }

                    var failed = new List<(string, string)>();
                    foreach (var method in parsed.methodList ?? new())
                    {
                        foreach (var comp in method.componentList ?? new())
                        {
                            string code = comp.code ?? "";
                            string checkName = comp.label ?? code;

                            // Informational only - not a pass/fail result.
                            if (string.Equals(code, "ic-type", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(checkName, "MyKad Type", StringComparison.OrdinalIgnoreCase))
                                continue;

                            bool isLandmark = MyKadLandmarkThresholds.TryGetValue(code, out var threshold);
                            bool isMicroprint = code.Contains("microprint", StringComparison.OrdinalIgnoreCase)
                                || checkName.Contains("microprint", StringComparison.OrdinalIgnoreCase);

                            if ((isLandmark || isMicroprint) && double.TryParse(comp.value, out var score))
                            {
                                double effectiveThreshold = isMicroprint ? MyKadMicroprintThreshold : threshold;
                                if (score < effectiveThreshold)
                                    failed.Add((checkName, $"{score} (below threshold {effectiveThreshold})"));
                            }
                            else if (!string.Equals(comp.value, "Pass", StringComparison.OrdinalIgnoreCase))
                            {
                                // Covers Fail, Cautious, and anything else -
                                // all treated as not passing.
                                failed.Add((checkName, comp.value ?? "no result"));
                            }
                        }
                    }

                    return new DocumentAuthenticityOutcome
                    {
                        CallSucceeded = true,
                        AllChecksPassed = failed.Count == 0,
                        FailedChecks = failed
                    };
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return new DocumentAuthenticityOutcome { CallSucceeded = false, ErrorMessage = string.Join(" | ", attempts) };
        }

        // ============================================================
        // GetScorecardResultAsync - see the class-level comment on
        // ScorecardOutcome/GetScorecardResponse above for the confirmed
        // response shape this parses against (a real sample response, not
        // inferred). Called once, after OkayID, OkayDoc, OkayFace and
        // OkayLive have all run against the SAME journeyId.
        // ============================================================
        public async Task<ScorecardOutcome> GetScorecardResultAsync(string journeyId, CancellationToken ct = default)
        {
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null)
                return new ScorecardOutcome { CallSucceeded = false, Passed = null, ErrorMessage = tokenError };

            var req = new GetScorecardRequest
            {
                mobile_number = "KIOSK",
                ip_address = GetLocalIp(),
                sender_id = null,
                eKYCrequest = new ScorecardInner { journeyId = journeyId }
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/Getscorecard_result";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<GetScorecardResponse>(body);

                    if (!string.Equals(parsed?.status, "success", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ScorecardOutcome
                        {
                            CallSucceeded = true,
                            Passed = false,
                            ErrorMessage = parsed?.message ?? "Scorecard status was not success",
                            RawJson = body
                        };
                    }

                    if (parsed.scorecardResultList == null || parsed.scorecardResultList.Count == 0)
                    {
                        return new ScorecardOutcome
                        {
                            CallSucceeded = true,
                            Passed = false,
                            ErrorMessage = "scorecardResultList missing or empty",
                            RawJson = body
                        };
                    }

                    var docResults = parsed.scorecardResultList
                        .Select(d => (DocType: d.docType ?? "unknown", ScorecardStatus: d.scorecardStatus ?? "unknown"))
                        .ToList();

                    // Every document in the list must be "clear" - if this
                    // journey covers multiple documents (e.g. MyKad front
                    // and back), all of them need to be clear, not just one.
                    bool allClear = docResults.All(d => string.Equals(d.ScorecardStatus, "clear", StringComparison.OrdinalIgnoreCase));

                    return new ScorecardOutcome
                    {
                        CallSucceeded = true,
                        Passed = allClear,
                        ErrorMessage = allClear
                            ? null
                            : "Not every document scored 'clear': " + string.Join(", ", docResults.Select(d => $"{d.DocType}={d.ScorecardStatus}")),
                        RawJson = body,
                        DocumentResults = docResults
                    };
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return new ScorecardOutcome { CallSucceeded = false, Passed = null, ErrorMessage = string.Join(" | ", attempts) };
        }

        public async Task<FaceMatchOutcome> MatchFaceAsync(
            string journeyId, string idCardImageBase64, string liveImageBase64, CancellationToken ct = default)
        {
            var (token, tokenError) = await EnsureBearerTokenAsync(ct);
            if (token == null)
                return new FaceMatchOutcome { CallSucceeded = false, Matched = false, ErrorMessage = tokenError };

            var req = new CentralizeOkayFaceRequest
            {
                mobile_number = "KIOSK",
                ip_address = GetLocalIp(),
                sender_id = null,
                eKYCrequest = new OkayFaceInner
                {
                    journeyId = journeyId,
                    livenessDetection = "true",
                    imageIdCardBase64 = idCardImageBase64,
                    imageBestBase64 = liveImageBase64
                }
            };
            var json = JsonSerializer.Serialize(req);
            var attempts = new List<string>();
            bool retried = false;

            foreach (var (label, baseUrl) in BaseUrls)
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) continue;

                try
                {
                    var url = baseUrl.TrimEnd('/') + "/api/eKYC/eKYC_CentralizeOkayFace_request";
                    var (status, body) = await SendAuthedAsync(url, json, token, ct);

                    if (status == HttpStatusCode.Unauthorized && !retried)
                    {
                        retried = true;
                        InvalidateToken();
                        var (freshToken, freshErr) = await EnsureBearerTokenAsync(ct);
                        if (freshToken == null) { attempts.Add($"{label} ({baseUrl}): {freshErr}"); continue; }
                        token = freshToken;
                        (status, body) = await SendAuthedAsync(url, json, token, ct);
                    }

                    if (status != HttpStatusCode.OK)
                    {
                        attempts.Add($"{label} ({baseUrl}): HTTP {(int)status}");
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<CentralizeOkayFaceResponse>(body);
                    var inner = parsed?.centralizeOkayFaceresponse;

                    if (parsed?.success != true || inner == null)
                    {
                        attempts.Add($"{label} ({baseUrl}): {parsed?.response_message1 ?? inner?.message ?? "request rejected"}");
                        continue;
                    }

                    double? confidence = inner.result_idcard?.confidence;
                    string? code = inner.messageCode ?? inner.message;
                    FriendlyMessages.TryGetValue(code ?? "", out var friendly);

                    return new FaceMatchOutcome
                    {
                        CallSucceeded = true,
                        Matched = string.Equals(inner.status, "success", StringComparison.OrdinalIgnoreCase),
                        ScorePercent = confidence,
                        LivenessProbability = inner.imageBestLiveness?.probability,
                        LivenessQuality = inner.imageBestLiveness?.quality,
                        Status = inner.status,
                        MessageCode = code,
                        ErrorMessage = inner.message,
                        FriendlyMessage = friendly
                    };
                }
                catch (Exception ex)
                {
                    attempts.Add($"{label} ({baseUrl}): {ex.Message}");
                }
            }

            return new FaceMatchOutcome { CallSucceeded = false, Matched = false, ErrorMessage = string.Join(" | ", attempts) };
        }

        private static string GetLocalIp()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "0.0.0.0";
            }
            catch { return "0.0.0.0"; }
        }
    }
}