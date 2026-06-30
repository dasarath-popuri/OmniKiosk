using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmniKiosk.Wpf.FaceSdk
{
    public static class FaceSdk
    {
        // =======================
        // Native SDK Imports
        // =======================

        [DllImport("TaiSDK.dll", EntryPoint = "face_init", CallingConvention = CallingConvention.StdCall)]
        private static extern int face_init(int[] hCtx);

        [DllImport("TaiSDK.dll", EntryPoint = "face_exit", CallingConvention = CallingConvention.StdCall)]
        private static extern int face_exit(int hCtx);

        [DllImport("TaiSDK.dll", EntryPoint = "face_get_feature_from_image", CallingConvention = CallingConvention.StdCall)]
        private static extern int face_get_feature_from_image(
            int hCtx,
            byte[] pic_bin,
            int pic_len,
            byte[] feature
        );

        [DllImport("TaiSDK.dll", EntryPoint = "face_comp_feature", CallingConvention = CallingConvention.StdCall)]
        private static extern int face_comp_feature(
            int hCtx,
            byte[] feature1,
            byte[] feature2
        );

        // =======================
        // Internal State
        // =======================

        private static int _hCtxValue;
        private static bool _initialized;
        private static readonly object _lock = new object();

        public static int FeatureSize { get; private set; }

        // =======================
        // Initialization
        // =======================

        static FaceSdk()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                int[] context = { 0 };

                // SDK returns base feature length
                FeatureSize = face_init(context);
                _hCtxValue = context[0];

                if (FeatureSize <= 0 || _hCtxValue == 0)
                {
                    throw new Exception($"Face SDK initialization failed. Code: {FeatureSize}");
                }

                _initialized = true;
            }
        }

        // =======================
        // Feature Extraction
        // =======================

        public static byte[] ExtractFeature(byte[] imageBin)
        {
            if (!_initialized)
                throw new InvalidOperationException("Face SDK not initialized");

            if (imageBin == null || imageBin.Length == 0)
                return null;

            // SDK requires buffer = FeatureSize * 2
            byte[] featureBuffer = new byte[FeatureSize * 2];

            int ret = face_get_feature_from_image(
                _hCtxValue,
                imageBin,
                imageBin.Length,
                featureBuffer
            );

            if (ret <= 0)
                return null;

            // Trim to actual feature length if returned
            if (ret < featureBuffer.Length)
            {
                Array.Resize(ref featureBuffer, ret);
            }

            return featureBuffer;
        }

        // =======================
        // Feature Comparison
        // =======================

        /// <summary>
        /// Returns similarity score (0–100)
        /// </summary>
        public static int Compare(byte[] feature1, byte[] feature2)
        {
            if (!_initialized)
                throw new InvalidOperationException("Face SDK not initialized");

            if (feature1 == null || feature2 == null)
                return 0;

            return face_comp_feature(_hCtxValue, feature1, feature2);
        }

        // =======================
        // Cleanup
        // =======================

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized)
                    return;

                face_exit(_hCtxValue);
                _hCtxValue = 0;
                _initialized = false;
            }
        }
    }
}
