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

            public Engine(
                FaceMatchSdkHelper? sdk,
                EngineInfo info)
            {
                _sdk = sdk;
                Info = info;
            }

            public bool TryExtractFeature(
                byte[] faceJpeg,
                out byte[]? feature,
                out string error)
            {
                feature = null;
                error = "";

                if (_sdk == null ||
                    !Info.IsAvailable)
                {
                    error = Info.Message;
                    return false;
                }

                var result =
                    _sdk.ExtractFeatureFromImage(
                        faceJpeg);

                if (!result.ok)
                {
                    error = result.message;
                    return false;
                }

                feature = result.feature;
                return true;
            }

            public bool TryCompare(
                byte[] feat1,
                byte[] feat2,
                out int score,
                out string error)
            {
                score = -1;
                error = "";

                if (_sdk == null ||
                    !Info.IsAvailable)
                {
                    error = Info.Message;
                    return false;
                }

                var result =
                    _sdk.Compare(
                        feat1,
                        feat2);

                if (!result.ok)
                {
                    error = result.message;
                    return false;
                }

                score = result.score;
                return true;
            }

            public bool TryDetectFaces(
                byte[] imageBytes,
                out FaceMatchSdkHelper.FaceCoord[] faces,
                out string error)
            {
                faces =
                    Array.Empty<
                        FaceMatchSdkHelper.FaceCoord>();

                error = "";

                if (_sdk == null ||
                    !Info.IsAvailable)
                {
                    error = Info.Message;
                    return false;
                }

                var result =
                    _sdk.DetectFaces(
                        imageBytes);

                if (!result.ok)
                {
                    error = result.message;
                    return false;
                }

                faces = result.faces;
                return true;
            }
        }

        private FaceMatchSdkHelper? _sdk;

        public Engine Current
        {
            get;
            private set;
        }

        public FaceEngineManager()
        {
            string baseDir =
                AppDomain.CurrentDomain.BaseDirectory;

            string taiPath =
                Path.Combine(
                    baseDir,
                    "TaiSDK.dll");

            if (!File.Exists(taiPath))
            {
                Current =
                    new Engine(
                        null,
                        new EngineInfo
                        {
                            IsAvailable = false,
                            Message =
                                $"TaiSDK.dll not found: {taiPath}"
                        });

                return;
            }

            try
            {
                _sdk =
                    new FaceMatchSdkHelper(
                        taiPath);

                var init =
                    _sdk.Init();

                Current =
                    new Engine(
                        _sdk,
                        new EngineInfo
                        {
                            IsAvailable = init.ok,
                            Message = init.message
                        });
            }
            catch (Exception ex)
            {
                Current =
                    new Engine(
                        null,
                        new EngineInfo
                        {
                            IsAvailable = false,
                            Message =
                                "Face engine init error: " +
                                ex.Message
                        });
            }
        }

        public void Dispose()
        {
            try
            {
                _sdk?.Dispose();
            }
            catch
            {
            }

            _sdk = null;
        }
    }
}
