namespace OmniKiosk.Wpf.Config
{
    public static class KioskSettings
    {
        // Passport SDK user id
        public const string PassportReaderUserId = "587911410110328496";

        // Folder name in output containing SDK files
        public const string PassportLibFolder = "Lib";

        // eKYC face-match backend (NotificationEngine, IIS-hosted).
        public const string EkycApiBaseUrl = "http://172.168.0.21:9096";
        public const string EkycApiBaseUrlFallback = "https://omniremit.rma.com.my:47443";

        public const string EkycLoginId = "vivoprod2574@dfsdf";
        public const string EkycLoginPassword = "vivo@#$23sftyg";
        public const string EkycSharedSecretKey = "Rm@-&In$sP";

        public const string MoneyExchangeApiBaseUrl = "https://localhost:7002";

        public const string ConfigApiBaseUrl = "http://YOUR_CONFIG_API_SERVER:PORT/";
        public const string KioskLoginId = "KIOSK-K101";       // matches the UserProfile row for this specific terminal
        public const string KioskLoginPassword = "REPLACE_ME"; // the machine secret, hashed the same way staff passwords are
    }
}