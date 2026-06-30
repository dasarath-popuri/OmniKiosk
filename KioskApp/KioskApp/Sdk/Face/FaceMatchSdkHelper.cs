using System;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Sdk.Face
{
    /// <summary>
    /// TaiSDK.dll wrapper (BINARY features).
    /// Manual:
    /// - face_init returns >0 feature length (binary)
    /// - face_get_feature_from_image returns >0 (success), feature is binary bytes
    /// - face_comp_feature returns similarity 0..100
    /// </summary>
    public sealed class FaceMatchSdkHelper : IDisposable
    {
        private readonly object _lock = new();
        private IntPtr _hDll = IntPtr.Zero;
        private int _hCtx = 0;
        private int _featLen = 0;
        private bool _inited;

        // delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int face_init_delegate(out int hCtx);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int face_exit_delegate(int hCtx);

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        //private delegate int face_get_feature_from_image_delegate(int hCtx, byte[] pic_bin, int pic_len, byte[] feature);

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        //private delegate int face_comp_feature_delegate(int hCtx, byte[] feature1, byte[] feature2);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int face_get_feature_from_image_delegate(int hCtx, byte[] pic_bin, int pic_len, IntPtr feature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int face_comp_feature_delegate(int hCtx, IntPtr feature1, IntPtr feature2);
        private readonly face_init_delegate _face_init;
        private readonly face_exit_delegate _face_exit;
        private readonly face_get_feature_from_image_delegate _get_feat_from_image;
        private readonly face_comp_feature_delegate _comp_feature;

        public FaceMatchSdkHelper(string taiSdkDllPath)
        {
            _hDll = Native.LoadLibrary(taiSdkDllPath);
            if (_hDll == IntPtr.Zero)
                throw new InvalidOperationException($"LoadLibrary failed: {taiSdkDllPath}");

            _face_init = Load<face_init_delegate>("face_init");
            _face_exit = Load<face_exit_delegate>("face_exit");
            _get_feat_from_image = Load<face_get_feature_from_image_delegate>("face_get_feature_from_image");
            _comp_feature = Load<face_comp_feature_delegate>("face_comp_feature");
        }

        public (bool ok, int code, string message) Init()
        {
            lock (_lock)
            {
                if (_inited) return (true, 0, "Already initialized.");

                int ret = _face_init(out _hCtx);

                // Manual: >0 is success and ret is length of BINARY feature :contentReference[oaicite:5]{index=5}
                if (ret <= 0)
                {
                    _hCtx = 0;
                    _featLen = 0;
                    _inited = false;
                    return (false, ret, $"face_init failed. ret={ret}");
                }

                _featLen = ret;
                _inited = true;
                return (true, ret, $"Init OK. BinaryFeatureLen={_featLen}");
            }
        }

        public bool IsReady
        {
            get { lock (_lock) return _inited && _hCtx != 0 && _featLen > 0; }
        }

        /// <summary>
        /// Extract BINARY feature from an image blob (jpg/png/bmp bytes).
        /// IMPORTANT: allocate 2x featLen (manual suggestion), but TRIM to returned length.
        /// </summary>
        public (bool ok, byte[]? feature, int code, string message) ExtractFeatureFromImage(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return (false, null, -3, "imageBytes empty");

            lock (_lock)
            {
                if (!_inited) return (false, null, -50, "SDK not initialized");

                // Allocate large buffer (manual suggests 2x; keep that)
                int cap = _featLen * 2;
                IntPtr pBuf = IntPtr.Zero;

                try
                {
                    pBuf = Marshal.AllocHGlobal(cap);
                    // zero memory to ensure null termination exists even if SDK forgets
                    Span<byte> zero = new byte[cap];
                    Marshal.Copy(zero.ToArray(), 0, pBuf, cap);

                    int ret = _get_feat_from_image(_hCtx, imageBytes, imageBytes.Length, pBuf);
                    if (ret <= 0)
                        return (false, null, ret, $"face_get_feature_from_image failed ret={ret}");

                    // ✅ Treat returned feature as TEXT bytes, ensure we append '\0'
                    var feature = new byte[ret + 1];
                    Marshal.Copy(pBuf, feature, 0, ret);
                    feature[ret] = 0;

                    return (true, feature, ret, $"Extract OK. sdkRet={ret}, cap={cap}");
                }
                finally
                {
                    if (pBuf != IntPtr.Zero) Marshal.FreeHGlobal(pBuf);
                }
            }
        }        /// <summary>
                 /// Compare two BINARY features. Returns 0..100 (manual).
                 /// </summary>
        public (bool ok, int score, int code, string message) Compare(byte[] feat1, byte[] feat2)
        {
            if (feat1 == null || feat1.Length == 0) return (false, -1, -3, "feat1 empty");
            if (feat2 == null || feat2.Length == 0) return (false, -1, -3, "feat2 empty");

            lock (_lock)
            {
                if (!_inited) return (false, -1, -50, "SDK not initialized");

                // ✅ Must be null-terminated
                if (feat1[^1] != 0) return (false, -1, -2, "feat1 missing null terminator");
                if (feat2[^1] != 0) return (false, -1, -2, "feat2 missing null terminator");

                GCHandle h1 = default, h2 = default;
                try
                {
                    h1 = GCHandle.Alloc(feat1, GCHandleType.Pinned);
                    h2 = GCHandle.Alloc(feat2, GCHandleType.Pinned);

                    int ret = _comp_feature(_hCtx, h1.AddrOfPinnedObject(), h2.AddrOfPinnedObject());
                    if (ret < 0) return (false, -1, ret, $"face_comp_feature failed ret={ret}");

                    return (true, ret, 0, "Compare OK");
                }
                finally
                {
                    if (h1.IsAllocated) h1.Free();
                    if (h2.IsAllocated) h2.Free();
                }
            }
        }
        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    if (_inited && _hCtx != 0)
                    {
                        _face_exit(_hCtx);
                        _hCtx = 0;
                        _featLen = 0;
                        _inited = false;
                    }
                }
                catch { /* ignore */ }

                if (_hDll != IntPtr.Zero)
                {
                    Native.FreeLibrary(_hDll);
                    _hDll = IntPtr.Zero;
                }
            }
        }

        private T Load<T>(string name) where T : Delegate
        {
            var p = Native.GetProcAddress(_hDll, name);
            if (p == IntPtr.Zero)
                throw new MissingMethodException($"Export not found: {name}");

            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        private static class Native
        {
            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32", SetLastError = true)]
            public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
        }
    }
}