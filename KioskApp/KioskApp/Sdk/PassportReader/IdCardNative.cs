using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniKiosk.Wpf.Sdk.PassportReader
{
    internal static class IdCardNative
    {
        // IDCard.dll is the SDK export library (Unicode). :contentReference[oaicite:5]{index=5}

        [DllImport("IDCard.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int InitIDCard(string lpUserID, int nType, string lpDirectory);

        [DllImport("IDCard.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FreeIDCard();

        [DllImport("IDCard.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int DetectDocument();

        [DllImport("IDCard.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int AutoProcessIDCard(ref int nCardType);

        [DllImport("IDCard.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetFieldNameEx(int nAttribute, int nIndex, StringBuilder lpBuffer, ref int nBufferLen);

        [DllImport("IDCard.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetRecogResultEx(int nAttribute, int nIndex, StringBuilder lpBuffer, ref int nBufferLen);

        [DllImport("IDCard.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetIDCardName(StringBuilder lpBuffer, ref int nBufferLen);

        [DllImport("IDCard.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetSubID();

        [DllImport("IDCard.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        public static extern int SaveImageEx(string lpFileName, int nType);
    }
}
