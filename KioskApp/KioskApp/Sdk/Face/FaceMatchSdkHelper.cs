using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Sdk.Face
{
    /// <summary>
    /// TaiSDK.dll wrapper.
    /// Provides feature extraction/comparison plus lightweight face-coordinate
    /// detection from an encoded image (JPEG/PNG/BMP bytes).
    /// </summary>
    public sealed class FaceMatchSdkHelper : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FaceCoord
        {
            public int x1;
            public int y1;
            public int x2;
            public int y2;
        }

        private readonly object _lock = new();
        private IntPtr _hDll = IntPtr.Zero;
        private int _hCtx;
        private int _featLen;
        private bool _inited;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int face_init_delegate(out int hCtx);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int face_exit_delegate(int hCtx);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int face_get_feature_from_image_delegate(
            int hCtx,
            byte[] pic_bin,
            int pic_len,
            IntPtr feature);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int face_comp_feature_delegate(
            int hCtx,
            IntPtr feature1,
            IntPtr feature2);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int face_get_pos_from_image_delegate(
            int hCtx,
            byte[] imageBytes,
            int imageLength,
            [Out] FaceCoord[] faces);

        private readonly face_init_delegate _face_init;
        private readonly face_exit_delegate _face_exit;
        private readonly face_get_feature_from_image_delegate _get_feat_from_image;
        private readonly face_comp_feature_delegate _comp_feature;
        private readonly face_get_pos_from_image_delegate _get_pos_from_image;

        public FaceMatchSdkHelper(string taiSdkDllPath)
        {
            _hDll = Native.LoadLibrary(taiSdkDllPath);

            if (_hDll == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"LoadLibrary failed: {taiSdkDllPath}");

            _face_init =
                Load<face_init_delegate>(
                    "face_init");

            _face_exit =
                Load<face_exit_delegate>(
                    "face_exit");

            _get_feat_from_image =
                Load<face_get_feature_from_image_delegate>(
                    "face_get_feature_from_image");

            _comp_feature =
                Load<face_comp_feature_delegate>(
                    "face_comp_feature");

            _get_pos_from_image =
                Load<face_get_pos_from_image_delegate>(
                    "face_get_pos_from_image");
        }

        public (
            bool ok,
            int code,
            string message)
            Init()
        {
            lock (_lock)
            {
                if (_inited)
                    return (
                        true,
                        0,
                        "Already initialized.");

                int ret =
                    _face_init(
                        out _hCtx);

                // Vendor manual: >0 is success and is the feature length.
                if (ret <= 0)
                {
                    _hCtx = 0;
                    _featLen = 0;
                    _inited = false;

                    return (
                        false,
                        ret,
                        $"face_init failed. ret={ret}");
                }

                _featLen = ret;
                _inited = true;

                return (
                    true,
                    ret,
                    $"Init OK. BinaryFeatureLen={_featLen}");
            }
        }

        public bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    return
                        _inited &&
                        _hCtx != 0 &&
                        _featLen > 0;
                }
            }
        }

        public (
            bool ok,
            byte[]? feature,
            int code,
            string message)
            ExtractFeatureFromImage(
                byte[] imageBytes)
        {
            if (imageBytes == null ||
                imageBytes.Length == 0)
            {
                return (
                    false,
                    null,
                    -3,
                    "imageBytes empty");
            }

            lock (_lock)
            {
                if (!_inited)
                {
                    return (
                        false,
                        null,
                        -50,
                        "SDK not initialized");
                }

                int capacity =
                    _featLen * 2;

                IntPtr buffer =
                    IntPtr.Zero;

                try
                {
                    buffer =
                        Marshal.AllocHGlobal(
                            capacity);

                    Marshal.Copy(
                        new byte[capacity],
                        0,
                        buffer,
                        capacity);

                    int ret =
                        _get_feat_from_image(
                            _hCtx,
                            imageBytes,
                            imageBytes.Length,
                            buffer);

                    if (ret <= 0)
                    {
                        return (
                            false,
                            null,
                            ret,
                            $"face_get_feature_from_image failed ret={ret}");
                    }

                    // Preserve current application's null-terminated feature
                    // representation used by Compare().
                    var feature =
                        new byte[ret + 1];

                    Marshal.Copy(
                        buffer,
                        feature,
                        0,
                        ret);

                    feature[ret] = 0;

                    return (
                        true,
                        feature,
                        ret,
                        $"Extract OK. sdkRet={ret}, cap={capacity}");
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(
                            buffer);
                    }
                }
            }
        }

        public (
            bool ok,
            int score,
            int code,
            string message)
            Compare(
                byte[] feat1,
                byte[] feat2)
        {
            if (feat1 == null ||
                feat1.Length == 0)
            {
                return (
                    false,
                    -1,
                    -3,
                    "feat1 empty");
            }

            if (feat2 == null ||
                feat2.Length == 0)
            {
                return (
                    false,
                    -1,
                    -3,
                    "feat2 empty");
            }

            lock (_lock)
            {
                if (!_inited)
                {
                    return (
                        false,
                        -1,
                        -50,
                        "SDK not initialized");
                }

                if (feat1[^1] != 0)
                {
                    return (
                        false,
                        -1,
                        -2,
                        "feat1 missing null terminator");
                }

                if (feat2[^1] != 0)
                {
                    return (
                        false,
                        -1,
                        -2,
                        "feat2 missing null terminator");
                }

                GCHandle handle1 = default;
                GCHandle handle2 = default;

                try
                {
                    handle1 =
                        GCHandle.Alloc(
                            feat1,
                            GCHandleType.Pinned);

                    handle2 =
                        GCHandle.Alloc(
                            feat2,
                            GCHandleType.Pinned);

                    int ret =
                        _comp_feature(
                            _hCtx,
                            handle1.AddrOfPinnedObject(),
                            handle2.AddrOfPinnedObject());

                    if (ret < 0)
                    {
                        return (
                            false,
                            -1,
                            ret,
                            $"face_comp_feature failed ret={ret}");
                    }

                    return (
                        true,
                        ret,
                        0,
                        "Compare OK");
                }
                finally
                {
                    if (handle1.IsAllocated)
                        handle1.Free();

                    if (handle2.IsAllocated)
                        handle2.Free();
                }
            }
        }

        /// <summary>
        /// Lightweight face-position check from encoded image bytes.
        /// TaiSDK manual: return value >0 is the number of detected faces.
        /// </summary>
        public (
            bool ok,
            FaceCoord[] faces,
            int code,
            string message)
            DetectFaces(
                byte[] imageBytes)
        {
            if (imageBytes == null ||
                imageBytes.Length == 0)
            {
                return (
                    false,
                    Array.Empty<FaceCoord>(),
                    -3,
                    "Image empty");
            }

            lock (_lock)
            {
                if (!_inited)
                {
                    return (
                        false,
                        Array.Empty<FaceCoord>(),
                        -50,
                        "SDK not initialized");
                }

                var faces =
                    new FaceCoord[15];

                int ret =
                    _get_pos_from_image(
                        _hCtx,
                        imageBytes,
                        imageBytes.Length,
                        faces);

                if (ret <= 0)
                {
                    return (
                        false,
                        Array.Empty<FaceCoord>(),
                        ret,
                        ret == -5
                            ? "No face"
                            : $"Face detection failed. ret={ret}");
                }

                int count =
                    Math.Min(
                        ret,
                        faces.Length);

                return (
                    true,
                    faces.Take(count).ToArray(),
                    ret,
                    $"{count} face(s) detected");
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    if (_inited &&
                        _hCtx != 0)
                    {
                        _face_exit(
                            _hCtx);

                        _hCtx = 0;
                        _featLen = 0;
                        _inited = false;
                    }
                }
                catch
                {
                }

                if (_hDll != IntPtr.Zero)
                {
                    Native.FreeLibrary(
                        _hDll);

                    _hDll =
                        IntPtr.Zero;
                }
            }
        }

        private T Load<T>(string name)
            where T : Delegate
        {
            IntPtr proc =
                Native.GetProcAddress(
                    _hDll,
                    name);

            if (proc == IntPtr.Zero)
            {
                throw new MissingMethodException(
                    $"Export not found: {name}");
            }

            return
                Marshal.GetDelegateForFunctionPointer<T>(
                    proc);
        }

        private static class Native
        {
            [DllImport(
                "kernel32",
                SetLastError = true,
                CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(
                string lpFileName);

            [DllImport(
                "kernel32",
                SetLastError = true)]
            public static extern bool FreeLibrary(
                IntPtr hModule);

            [DllImport(
                "kernel32",
                SetLastError = true,
                CharSet = CharSet.Ansi)]
            public static extern IntPtr GetProcAddress(
                IntPtr hModule,
                string procName);
        }
    }
}
