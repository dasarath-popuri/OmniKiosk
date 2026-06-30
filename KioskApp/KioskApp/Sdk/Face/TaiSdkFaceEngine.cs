using System;
using System.IO;

namespace OmniKiosk.Wpf.Sdk.Face
{
    public sealed class TaiSdkFaceEngine : IFaceEngine, IDisposable
    {
        private FaceMatchSdkHelper? _sdk;
        private FaceEngineInfo _info;

        public FaceEngineInfo Info => _info;

        public TaiSdkFaceEngine()
        {
            _info = new FaceEngineInfo
            {
                Status = FaceEngineStatus.Unavailable_Unknown,
                Message = "Not initialized"
            };

            TryInit();
        }

        private void TryInit()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var taiPath = Path.Combine(baseDir, "FaceMatchSDK", "TaiSDK.dll");

                _sdk = new FaceMatchSdkHelper(taiPath);
                var init = _sdk.Init();

                if (!init.ok)
                    throw new InvalidOperationException(init.message);

                _info = new FaceEngineInfo
                {
                    Status = FaceEngineStatus.Available,
                    Message = init.message
                };
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? "";

                if (msg.Contains("ret=-19", StringComparison.OrdinalIgnoreCase))
                {
                    _info = new FaceEngineInfo
                    {
                        Status = FaceEngineStatus.Unavailable_NoLicense,
                        Message = "FaceMatch license not detected (dongle/software activation missing)."
                    };
                }
                else if (msg.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("DllNotFound", StringComparison.OrdinalIgnoreCase))
                {
                    _info = new FaceEngineInfo
                    {
                        Status = FaceEngineStatus.Unavailable_MissingFiles,
                        Message = "FaceMatch native files missing (TaiSDK/SsNow/model.dat)."
                    };
                }
                else
                {
                    _info = new FaceEngineInfo
                    {
                        Status = FaceEngineStatus.Unavailable_Unknown,
                        Message = "FaceMatch unavailable: " + msg
                    };
                }
            }
        }

        public bool TryExtractFeature(byte[] faceImageBytes, out byte[]? feature, out string? error)
        {
            feature = null;
            error = null;

            if (!_info.IsAvailable || _sdk == null || !_sdk.IsReady)
            {
                error = _info.Message;
                return false;
            }

            var r = _sdk.ExtractFeatureFromImage(faceImageBytes);
            if (!r.ok || r.feature == null)
            {
                error = r.message;
                return false;
            }

            feature = r.feature;
            return true;
        }

        public bool TryCompare(byte[] feature1, byte[] feature2, out int score, out string? error)
        {
            score = -1;
            error = null;

            if (!_info.IsAvailable || _sdk == null || !_sdk.IsReady)
            {
                error = _info.Message;
                return false;
            }

            var r = _sdk.Compare(feature1, feature2);
            if (!r.ok)
            {
                error = r.message;
                return false;
            }

            score = r.score;
            return true;
        }

        public void Dispose()
        {
            try { _sdk?.Dispose(); } catch { }
            _sdk = null;
        }
    }
}