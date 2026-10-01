using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniKiosk.Wpf.Services.Diagnostics
{
    /// <summary>
    /// Temporary UAT logger used to compare kiosk API performance
    /// over 4G and 5G.
    ///
    /// It does NOT log:
    /// - request bodies
    /// - response bodies
    /// - customer details
    /// - Base64 images
    /// - tokens
    ///
    /// It only logs timings, sizes, endpoint and HTTP result.
    /// </summary>
    public static class ApiPerformanceLogger
    {
        private static readonly object _fileLock =
            new object();

        private static readonly string _sessionId =
            DateTime.Now.ToString(
                "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture);

        private static string? _networkLabel;

        public static string NetworkLabel
        {
            get
            {
                if (_networkLabel == null)
                {
                    _networkLabel =
                        ReadNetworkLabel();
                }

                return _networkLabel;
            }
        }

        public static string SessionId =>
            _sessionId;

        public static string LogDirectory =>
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs",
                "ApiPerformance");

        public static string LogFilePath
        {
            get
            {
                string safeNetwork =
                    SanitizeFileName(
                        NetworkLabel);

                return Path.Combine(
                    LogDirectory,
                    $"ApiPerformance_{safeNetwork}_{_sessionId}.csv");
            }
        }

        /// <summary>
        /// Use this wrapper instead of HttpClient.SendAsync
        /// during the 4G/5G test.
        /// </summary>
        public static async Task<HttpResponseMessage>
            SendAsync(
                HttpClient client,
                HttpRequestMessage request,
                string apiGroup,
                string apiName,
                int attempt,
                CancellationToken cancellationToken,
                long imageBytes = 0)
        {
            long requestBytes =
                await GetContentSizeAsync(
                    request.Content,
                    cancellationToken);

            string endpoint =
                request.RequestUri?
                    .ToString()
                ?? "";

            string method =
                request.Method.Method;

            var stopwatch =
                Stopwatch.StartNew();

            try
            {
                HttpResponseMessage response =
                    await client.SendAsync(
                        request,
                        HttpCompletionOption
                            .ResponseContentRead,
                        cancellationToken);

                stopwatch.Stop();

                long responseBytes =
                    await GetContentSizeAsync(
                        response.Content,
                        cancellationToken);

                WriteEntry(
                    apiGroup:
                        apiGroup,

                    apiName:
                        apiName,

                    method:
                        method,

                    endpoint:
                        endpoint,

                    attempt:
                        attempt,

                    requestBytes:
                        requestBytes,

                    responseBytes:
                        responseBytes,

                    imageBytes:
                        imageBytes,

                    elapsedMs:
                        stopwatch.ElapsedMilliseconds,

                    statusCode:
                        (int)response.StatusCode,

                    success:
                        response.IsSuccessStatusCode,

                    error:
                        "");

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                WriteEntry(
                    apiGroup:
                        apiGroup,

                    apiName:
                        apiName,

                    method:
                        method,

                    endpoint:
                        endpoint,

                    attempt:
                        attempt,

                    requestBytes:
                        requestBytes,

                    responseBytes:
                        0,

                    imageBytes:
                        imageBytes,

                    elapsedMs:
                        stopwatch.ElapsedMilliseconds,

                    statusCode:
                        0,

                    success:
                        false,

                    error:
                        ex.GetType().Name +
                        ": " +
                        ex.Message);

                throw;
            }
        }

        /// <summary>
        /// Calculates the approximate original binary size
        /// represented by a Base64 string.
        ///
        /// The actual HTTP request size is logged separately,
        /// so this is only used to show how much image data
        /// was being sent.
        /// </summary>
        public static long GetBase64BinarySize(
            string? base64)
        {
            if (string.IsNullOrWhiteSpace(
                    base64))
            {
                return 0;
            }

            string value =
                base64.Trim();

            int comma =
                value.IndexOf(',');

            if (comma >= 0)
            {
                value =
                    value.Substring(
                        comma + 1);
            }

            value =
                value.Replace(
                    "\r",
                    "")
                .Replace(
                    "\n",
                    "");

            if (value.Length == 0)
                return 0;

            int padding =
                value.EndsWith(
                    "==",
                    StringComparison.Ordinal)
                    ? 2
                    : value.EndsWith(
                        "=",
                        StringComparison.Ordinal)
                        ? 1
                        : 0;

            return
                ((long)value.Length * 3 / 4)
                - padding;
        }

        private static async Task<long>
            GetContentSizeAsync(
                HttpContent? content,
                CancellationToken ct)
        {
            if (content == null)
                return 0;

            try
            {
                if (content.Headers
                    .ContentLength
                    .HasValue)
                {
                    return content.Headers
                        .ContentLength
                        .Value;
                }

                byte[] bytes =
                    await content
                        .ReadAsByteArrayAsync(
                            ct);

                return bytes.LongLength;
            }
            catch
            {
                return 0;
            }
        }

        private static void WriteEntry(
            string apiGroup,
            string apiName,
            string method,
            string endpoint,
            int attempt,
            long requestBytes,
            long responseBytes,
            long imageBytes,
            long elapsedMs,
            int statusCode,
            bool success,
            string error)
        {
            try
            {
                Directory.CreateDirectory(
                    LogDirectory);

                string file =
                    LogFilePath;

                lock (_fileLock)
                {
                    bool newFile =
                        !File.Exists(
                            file);

                    using var writer =
                        new StreamWriter(
                            file,
                            append: true,
                            Encoding.UTF8);

                    if (newFile)
                    {
                        writer.WriteLine(
                            "TimestampLocal," +
                            "TimestampUtc," +
                            "SessionId," +
                            "Network," +
                            "ApiGroup," +
                            "ApiName," +
                            "Method," +
                            "Endpoint," +
                            "Attempt," +
                            "RequestBytes," +
                            "RequestKB," +
                            "ResponseBytes," +
                            "ResponseKB," +
                            "ImageBytes," +
                            "ImageKB," +
                            "ElapsedMs," +
                            "ElapsedSeconds," +
                            "HttpStatus," +
                            "Success," +
                            "Error");
                    }

                    writer.WriteLine(
                        Csv(
                            DateTime.Now.ToString(
                                "yyyy-MM-dd HH:mm:ss.fff")) +
                        "," +

                        Csv(
                            DateTime.UtcNow
                                .ToString(
                                    "yyyy-MM-dd HH:mm:ss.fff")) +
                        "," +

                        Csv(
                            SessionId) +
                        "," +

                        Csv(
                            NetworkLabel) +
                        "," +

                        Csv(
                            apiGroup) +
                        "," +

                        Csv(
                            apiName) +
                        "," +

                        Csv(
                            method) +
                        "," +

                        Csv(
                            endpoint) +
                        "," +

                        attempt +
                        "," +

                        requestBytes +
                        "," +

                        (
                            requestBytes /
                            1024.0
                        ).ToString(
                            "0.00",
                            CultureInfo.InvariantCulture) +
                        "," +

                        responseBytes +
                        "," +

                        (
                            responseBytes /
                            1024.0
                        ).ToString(
                            "0.00",
                            CultureInfo.InvariantCulture) +
                        "," +

                        imageBytes +
                        "," +

                        (
                            imageBytes /
                            1024.0
                        ).ToString(
                            "0.00",
                            CultureInfo.InvariantCulture) +
                        "," +

                        elapsedMs +
                        "," +

                        (
                            elapsedMs /
                            1000.0
                        ).ToString(
                            "0.000",
                            CultureInfo.InvariantCulture) +
                        "," +

                        statusCode +
                        "," +

                        (
                            success
                                ? "1"
                                : "0"
                        ) +
                        "," +

                        Csv(
                            error));
                }
            }
            catch
            {
                // Performance logging must never
                // interrupt the kiosk transaction.
            }
        }

        private static string
            ReadNetworkLabel()
        {
            try
            {
                string file =
                    Path.Combine(
                        AppDomain.CurrentDomain
                            .BaseDirectory,
                        "network-test-mode.txt");

                if (!File.Exists(
                        file))
                {
                    return "UNSPECIFIED";
                }

                string label =
                    File.ReadAllText(
                        file)
                    .Trim();

                return string.IsNullOrWhiteSpace(
                    label)
                    ? "UNSPECIFIED"
                    : label;
            }
            catch
            {
                return "UNSPECIFIED";
            }
        }

        private static string Csv(
            string? value)
        {
            value ??= "";

            return "\"" +
                   value.Replace(
                       "\"",
                       "\"\"") +
                   "\"";
        }

        private static string
            SanitizeFileName(
                string value)
        {
            foreach (
                char c
                in Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        c,
                        '_');
            }

            return value;
        }
    }
}