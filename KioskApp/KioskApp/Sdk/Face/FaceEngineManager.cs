using System;
using System.IO;

namespace OmniKiosk.Wpf.Sdk.Face
{
    public sealed class FaceEngineManager : IDisposable
    {
        public sealed class EngineInfo
        {
            public bool IsAvailable { get; init; }
            public string Message { get; init; } = "";
        }

        public sealed class Engine
        {
            private readonly FaceMatchSdkHelper? _sdk;
            public EngineInfo Info { get; }

            public Engine(FaceMatchSdkHelper? sdk, EngineInfo info)
            {
                _sdk = sdk;
                Info = info;
            }

            public bool TryExtractFeature(byte[] faceJpeg, out byte[]? feature, out string err)
            {
                feature = null;
                err = "";

                if (_sdk == null || !Info.IsAvailable)
                {
                    err = Info.Message;
                    return false;
                }

                var r = _sdk.ExtractFeatureFromImage(faceJpeg);
                if (!r.ok)
                {
                    err = r.message;
                    return false;
                }

                feature = r.feature;
                return true;
            }

            public bool TryCompare(byte[] feat1, byte[] feat2, out int score, out string err)
            {
                score = -1;
                err = "";

                if (_sdk == null || !Info.IsAvailable)
                {
                    err = Info.Message;
                    return false;
                }

                var r = _sdk.Compare(feat1, feat2);
                if (!r.ok)
                {
                    err = r.message;
                    return false;
                }

                score = r.score;
                return true;
            }
        }

        private FaceMatchSdkHelper? _sdk;
        public Engine Current { get; private set; }

        public FaceEngineManager()
        {
            // IMPORTANT: point to TaiSDK.dll next to exe (same folder as SsNow.dll etc.)
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var taiPath = Path.Combine(baseDir, "TaiSDK.dll");

            if (!File.Exists(taiPath))
            {
                Current = new Engine(null, new EngineInfo
                {
                    IsAvailable = false,
                    Message = $"TaiSDK.dll not found: {taiPath}"
                });
                return;
            }

            try
            {
                _sdk = new FaceMatchSdkHelper(taiPath);
                var init = _sdk.Init();

                Current = new Engine(_sdk, new EngineInfo
                {
                    IsAvailable = init.ok,
                    Message = init.ok ? init.message : init.message
                });
            }
            catch (Exception ex)
            {
                Current = new Engine(null, new EngineInfo
                {
                    IsAvailable = false,
                    Message = "Face engine init error: " + ex.Message
                });
            }
        }

        public void Dispose()
        {
            try { _sdk?.Dispose(); } catch { }
            _sdk = null;
        }
    }
}