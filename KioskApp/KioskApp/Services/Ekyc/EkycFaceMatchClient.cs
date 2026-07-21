using System;
using System.Collections.Generic;
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

                        // GetToken() returns Ok(rawJwtString), which ASP.NET Core serves
                        // as plain text - no surrounding JSON quotes. Take the body as
                        // the token directly; Trim('"') is just defensive in case the
                        // server ever does return it JSON-quoted instead.
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