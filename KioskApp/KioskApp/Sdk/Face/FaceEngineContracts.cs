using System;

namespace OmniKiosk.Wpf.Sdk.Face
{
    public enum FaceEngineStatus
    {
        Available,
        Unavailable_NoLicense,
        Unavailable_MissingFiles,
        Unavailable_Unknown
    }

    public sealed class FaceEngineInfo
    {
        public FaceEngineStatus Status { get; init; }
        public string Message { get; init; } = "";
        public bool IsAvailable => Status == FaceEngineStatus.Available;
    }

    public interface IFaceEngine
    {
        FaceEngineInfo Info { get; }

        /// <summary>Try to extract biometric feature from face image bytes.</summary>
        bool TryExtractFeature(byte[] faceImageBytes, out byte[]? feature, out string? error);

        /// <summary>Try to compare 2 features. Score typically 0..100. Return false if not supported.</summary>
        bool TryCompare(byte[] feature1, byte[] feature2, out int score, out string? error);
    }
}