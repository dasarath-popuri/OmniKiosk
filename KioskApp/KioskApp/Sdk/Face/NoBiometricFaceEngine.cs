namespace OmniKiosk.Wpf.Sdk.Face
{
    public sealed class NoBiometricFaceEngine : IFaceEngine
    {
        private readonly FaceEngineInfo _info;

        public NoBiometricFaceEngine(string reason)
        {
            _info = new FaceEngineInfo
            {
                Status = FaceEngineStatus.Unavailable_NoLicense,
                Message = reason
            };
        }

        public FaceEngineInfo Info => _info;

        public bool TryExtractFeature(byte[] faceImageBytes, out byte[]? feature, out string? error)
        {
            feature = null;
            error = _info.Message;
            return false;
        }

        public bool TryCompare(byte[] feature1, byte[] feature2, out int score, out string? error)
        {
            score = -1;
            error = _info.Message;
            return false;
        }
    }
}