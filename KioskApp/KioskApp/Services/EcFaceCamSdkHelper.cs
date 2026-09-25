using System;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Services
{
    public enum CallBackEvent
    {
        CALLBACK_EVENT_GOODFACE = 0,
        CALLBACK_EVENT_NOFACE = 1,
        CALLBACK_EVENT_MULTIFACE = 2,
        CALLBACK_EVENT_HEADPOS = 3,
        CALLBACK_EVENT_BIGFACE = 4,
        CALLBACK_EVENT_SMALLFACE = 5,
        CALLBACK_EVENT_EMOTION = 6,
        CALLBACK_EVENT_MOTIVE = 7,
        CALLBACK_EVENT_BRIGHT = 8,
        CALLBACK_EVENT_NOTCENTER = 9,
        CALLBACK_EVENT_EYEOCC = 10,
        CALLBACK_EVENT_MOCC = 11,
        CALLBACK_EVENT_NOTINROI = 12,
        CALLBACK_EVENT_SUNGLASSES = 13,
        CALLBACK_EVENT_MASK = 14,
        CALLBACK_EVENT_GLASSES = 15,
        CALLBACK_EVENT_BEARD = 16,
        CALLBACK_EVENT_PHONE = 17,
        CALLBACK_EVENT_HAT = 18,
        CALLBACK_EVENT_LEFTCHEEK = 19,
        CALLBACK_EVENT_RIGHTCHEEK = 20,
        CALLBACK_EVENT_FOREHEAD = 21,
        CALLBACK_EVENT_CHIN = 22,
        CALLBACK_EVENT_PREVIEW = 50,
        CALLBACK_EVENT_SUCC = 100,
        CALLBACK_EVENT_FAIL = 101,
        CALLBACK_EVENT_TIMEOUT = 102,
        CALLBACK_EVENT_SNAP = 103,
        CALLBACK_EVENT_CANCEL = 104
    }

    public enum ImageType
    {
        IMAGE_TYPE_VIS = 0,
        IMAGE_TYPE_NIR = 1,
        IMAGE_TYPE_VIS_RC = 2,
        IMAGE_TYPE_NIR_RC = 3,
        IMAGE_TYPE_CROP_VIS = 4,
        IMAGE_TYPE_CROP_NIR = 5
    }

    public static class EcFaceCamSdkHelper
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void CallbackDelegate(int eventId, IntPtr context);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Init", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Init();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Exit", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Exit();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Open", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Open([MarshalAs(UnmanagedType.LPStr)] string strParams);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Close", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Close();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_SetDisplayWindowEx", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_SetDisplayWindowEx(int nWndType, IntPtr hWnd, int left, int top, int right, int bottom);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_SetCallBack", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_SetCallBack(CallbackDelegate callback, IntPtr context);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_StartDetectAsyn", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_StartDetectAsyn();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Stop", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Stop();

        // Existing signatures kept so SDK test/older code does not break.
        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_GetImageData", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_GetImageData(int nType, byte[]? dataBuf, ref int dataLen);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_CopyFrameWithAlpha", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_CopyFrameWithAlpha(int nImageType, byte[] pImgJpg, ref int pnJpgLen, int[]? pFaceRect);

        // These two overloads intentionally match the provider's JpegPull_net7
        // sample P/Invoke shape exactly (int[1] for the output length pointer).
        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_GetImageData", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_GetImageDataProvider(int nType, byte[] dataBuf, int[] dataLen);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_CopyFrameWithAlpha", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_CopyFrameWithAlphaProvider(int nImageType, byte[] pImgJpg, int[] pnJpgLen, int[]? pFaceRect);

        private static readonly object _initLock = new();
        private static bool _sdkInitialized;

        public static bool IsInitialized
        {
            get
            {
                lock (_initLock)
                    return _sdkInitialized;
            }
        }

        public static int EnsureInitialized()
        {
            lock (_initLock)
            {
                if (_sdkInitialized)
                    return 0;

                int result = ECF_Init();
                if (result == 0)
                    _sdkInitialized = true;

                return result;
            }
        }

        public static void ShutdownSdk()
        {
            lock (_initLock)
            {
                if (!_sdkInitialized)
                    return;

                try
                {
                    ECF_Exit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Eyecool] ECF_Exit error: " + ex.Message);
                }
                finally
                {
                    _sdkInitialized = false;
                }
            }
        }

        public static byte[]? GetCroppedVisFace()
        {
            try
            {
                byte[] buffer = new byte[200 * 1024];
                int[] len = new int[1];
                int ret = ECF_GetImageDataProvider((int)ImageType.IMAGE_TYPE_CROP_VIS, buffer, len);

                if (ret != 0 || len[0] <= 0 || len[0] > buffer.Length)
                    return null;

                byte[] result = new byte[len[0]];
                Buffer.BlockCopy(buffer, 0, result, 0, len[0]);
                return result;
            }
            catch
            {
                return null;
            }
        }

        public static string MapFailReason(CallBackEvent evt)
        {
            return evt switch
            {
                CallBackEvent.CALLBACK_EVENT_TIMEOUT => "Timeout – please face the camera",
                CallBackEvent.CALLBACK_EVENT_FAIL => "Liveness check failed",
                CallBackEvent.CALLBACK_EVENT_CANCEL => "Operation cancelled",
                CallBackEvent.CALLBACK_EVENT_MOTIVE => "Please keep your face steady",
                _ => "Unknown failure"
            };
        }
    }
}
