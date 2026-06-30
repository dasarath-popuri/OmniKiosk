using System;
using System.Runtime.InteropServices;

namespace OmniKiosk.Wpf.Sdk.IC
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct CardHolderInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 81)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string Nric;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)] public string OldIC;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)] public string DateOfBirth;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 26)] public string PlaceOfBirth;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)] public string Gender;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 26)] public string Race;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string Address1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string Address2;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string Address3;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 6)] public string Postcode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 26)] public string City;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 31)] public string State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)] public string SocsoNo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 19)] public string Citizenship;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)] public string Religion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)] public string EastMsiaOrigin;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)] public string OtherIDNo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)] public string DateIssued;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)] public string GreenCardHolderNational;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 11)] public string GreenCardExpiryDate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)] public string CardVersionNo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 2)] public string CardCategoryType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3)] public string CriminalRecord;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 3)] public string RestrictedResident;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)] public string HandCode;
    }

    public class MyKadApi
    {
        [DllImport("MyKadCore.dll", EntryPoint = "MyKad_GetReaders", ExactSpelling = false, CallingConvention = CallingConvention.StdCall)]
        public static extern int MyKad_GetReaders([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] readers, ref IntPtr size);

        [DllImport("MyKadCore.dll", EntryPoint = "MyKad_Open", ExactSpelling = false, CallingConvention = CallingConvention.StdCall)]
        public static extern int MyKad_Open(byte[] readers);

        [DllImport("MyKadCore.dll", EntryPoint = "MyKad_Close", ExactSpelling = false, CallingConvention = CallingConvention.StdCall)]
        public static extern int MyKad_Close();

        [DllImport("MyKadCore.dll", EntryPoint = "MyKad_GetPhoto", ExactSpelling = false, CallingConvention = CallingConvention.StdCall)]
        public static extern int MyKad_GetPhoto([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] photo, ref IntPtr size);

        [DllImport("MyKadCore.dll", EntryPoint = "MyKad_GetCardHolderInfo", ExactSpelling = false, CallingConvention = CallingConvention.StdCall)]
        public static extern int MyKad_GetCardHolderInfo(out CardHolderInfo cardHolderInfo);
    }
}