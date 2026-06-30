using System;
using System.IO;

namespace OmniKiosk.Wpf.Sdk.Face
{
    public static class FaceMatcher
    {
        private static readonly object _lock = new();
        private static FaceMatchSdkHelper? _sdk;
        private static bool _inited;

        private static void EnsureInit()
        {
            lock (_lock)
            {
                if (_inited && _sdk != null && _sdk.IsReady) return;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var taiPath = Path.Combine(baseDir, "FaceMatchSDK", "TaiSDK.dll");

                _sdk?.Dispose();
                _sdk = new FaceMatchSdkHelper(taiPath);

                var init = _sdk.Init();
                if (!init.ok)
                    throw new InvalidOperationException(init.message);

                _inited = true;
            }
        }

        public static byte[] ExtractFeature(byte[] faceImageBytes)
        {
            EnsureInit();

            var r = _sdk!.ExtractFeatureFromImage(faceImageBytes);
            if (!r.ok || r.feature == null)
                throw new InvalidOperationException(r.message);

            return r.feature;
        }

        public static int SimilarityScore(byte[] a, byte[] b)
        {
            EnsureInit();

            var r = _sdk!.Compare(a, b);
            if (!r.ok) return -1;

            return r.score; // 0..100
        }
    }
}