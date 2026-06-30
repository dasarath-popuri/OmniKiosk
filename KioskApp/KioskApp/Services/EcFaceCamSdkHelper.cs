using System;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Services
{
    // ==================================================
    // Callback Event Types (from SDK docs)
    // ==================================================
    public enum CallBackEvent
    {
        CALLBACK_EVENT_SUCC = 100,     // Liveness success
        CALLBACK_EVENT_FAIL = 101,     // Liveness fail (spoof / pose)
        CALLBACK_EVENT_TIMEOUT = 102,  // Timeout
        CALLBACK_EVENT_SNAP = 103,     // Snapshot captured
        CALLBACK_EVENT_CANCEL = 104,   // Cancelled by user

        CALLBACK_EVENT_PREVIEW = 50    // Preview frame
    }

    // ==================================================
    // Image Types (used with ECF_GetImageData)
    // ==================================================
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
        // ==================================================
        // Callback Delegate
        // ==================================================
        public delegate void CallbackDelegate(int eventId, IntPtr context);

        // ==================================================
        // Native SDK APIs
        // ==================================================

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Open", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Open(string strParams);

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Close", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Close();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_SetDisplayWindowEx", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_SetDisplayWindowEx(
            int nWndType,
            IntPtr hWnd,
            int left,
            int top,
            int right,
            int bottom
        );

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_SetCallBack", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_SetCallBack(
            CallbackDelegate callback,
            IntPtr context
        );

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_StartDetectAsyn", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_StartDetectAsyn();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_Stop", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_Stop();

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_GetImageData", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_GetImageData(
            int nType,
            byte[] dataBuf,
            ref int dataLen
        );

        [DllImport("EcFaceCamSDK.dll", EntryPoint = "ECF_CopyFrameWithAlpha", CallingConvention = CallingConvention.StdCall)]
        public static extern int ECF_CopyFrameWithAlpha(
            int nImageType,
            byte[] pImgJpg,
            ref int pnJpgLen,
            int[] pFaceRect
        );

        // ==================================================
        // Helper: Get Cropped VIS Face Image
        // ==================================================
        public static byte[] GetCroppedVisFace()
        {
            int len = 0;
            ECF_GetImageData((int)ImageType.IMAGE_TYPE_CROP_VIS, null, ref len);

            if (len <= 0)
                return null;

            byte[] buffer = new byte[len];
            int ret = ECF_GetImageData((int)ImageType.IMAGE_TYPE_CROP_VIS, buffer, ref len);

            return ret == 0 ? buffer : null;
        }

        // ==================================================
        // Helper: Map failure reason for UI
        // ==================================================
        public static string MapFailReason(CallBackEvent evt)
        {
            return evt switch
            {
                CallBackEvent.CALLBACK_EVENT_TIMEOUT => "Timeout – please face the camera",
                CallBackEvent.CALLBACK_EVENT_FAIL => "Liveness check failed (spoof / pose)",
                CallBackEvent.CALLBACK_EVENT_CANCEL => "Operation cancelled",
                _ => "Unknown failure"
            };
        }

    }
}
