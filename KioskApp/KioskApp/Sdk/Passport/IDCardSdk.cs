using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniKiosk.Wpf.Sdk.Passport
{
    internal sealed class IDCardSdk : IDisposable
    {
        private IntPtr _h;

        public IDCardSdk(string dllPath)
        {
            _h = Native.LoadLibrary(dllPath);
            if (_h == IntPtr.Zero)
                throw new InvalidOperationException("LoadLibrary failed for: " + dllPath);

            InitIDCard_ = Load<InitIDCardDelegate>("InitIDCard");
            FreeIDCard_ = Load<FreeIDCardDelegate>("FreeIDCard");
            SetConfigByFile_ = Load<SetConfigByFileDelegate>("SetConfigByFile");
            SetLanguage_ = Load<SetLanguageDelegate>("SetLanguage");
            SetSaveImageType_ = Load<SetSaveImageTypeDelegate>("SetSaveImageType");
            SetRecogVIZ_ = Load<SetRecogVIZDelegate>("SetRecogVIZ");
            SetRecogDG_ = Load<SetRecogDGDelegate>("SetRecogDG");
            SetAnalyseMRZ_ = Load<SetAnalyseMRZDelegate>("SetAnalyseMRZ");
            ResetIDCardID_ = Load<ResetIDCardIDDelegate>("ResetIDCardID");
            AddIDCardID_ = Load<AddIDCardIDDelegate>("AddIDCardID");
            AutoProcessIDCard_ = Load<AutoProcessIDCardDelegate>("AutoProcessIDCard");
            GetRecogResultEx_ = Load<GetRecogResultExDelegate>("GetRecogResultEx");
            SaveImageEx_ = Load<SaveImageExDelegate>("SaveImageEx");
            CheckDeviceOnlineEx_ = Load<CheckDeviceOnlineExDelegate>("CheckDeviceOnlineEx");
        }

        // ✅ IMPORTANT: delegate TYPES renamed to *Delegate to avoid name conflicts
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int InitIDCardDelegate(string userId, int nType, string libPath);
        private readonly InitIDCardDelegate InitIDCard_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FreeIDCardDelegate();
        private readonly FreeIDCardDelegate FreeIDCard_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int SetConfigByFileDelegate(string cfgPath);
        private readonly SetConfigByFileDelegate SetConfigByFile_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetLanguageDelegate(int lang);
        private readonly SetLanguageDelegate SetLanguage_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetSaveImageTypeDelegate(int mask);
        private readonly SetSaveImageTypeDelegate SetSaveImageType_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetRecogVIZDelegate([MarshalAs(UnmanagedType.I1)] bool enable);
        private readonly SetRecogVIZDelegate SetRecogVIZ_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetRecogDGDelegate(int nDG);
        private readonly SetRecogDGDelegate SetRecogDG_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetAnalyseMRZDelegate([MarshalAs(UnmanagedType.I1)] bool enable);
        private readonly SetAnalyseMRZDelegate SetAnalyseMRZ_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ResetIDCardIDDelegate();
        private readonly ResetIDCardIDDelegate ResetIDCardID_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AddIDCardIDDelegate(int nMainId, int[] subIds, int subIdCount);
        private readonly AddIDCardIDDelegate AddIDCardID_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AutoProcessIDCardDelegate(ref int nCardType);
        private readonly AutoProcessIDCardDelegate AutoProcessIDCard_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int GetRecogResultExDelegate(int nAttribute, int nIndex, StringBuilder buffer, ref int bufferLen);
        private readonly GetRecogResultExDelegate GetRecogResultEx_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate int SaveImageExDelegate(string baseFilePath, int typeMask);
        private readonly SaveImageExDelegate SaveImageEx_;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CheckDeviceOnlineExDelegate();
        private readonly CheckDeviceOnlineExDelegate CheckDeviceOnlineEx_;

        private T Load<T>(string name) where T : Delegate
        {
            var p = Native.GetProcAddress(_h, name);
            if (p == IntPtr.Zero)
                throw new MissingMethodException("Export not found: " + name);

            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        // ✅ Public API (unchanged names)
        //public int InitIDCard(string userId, string libPath) => InitIDCard_(userId, libPath);
        public int InitIDCard(string userId, int nType, string libPath) => InitIDCard_(userId, nType, libPath);
        public void FreeIDCard() => FreeIDCard_();
        public int SetConfigByFile(string cfgPath) => SetConfigByFile_(cfgPath);
        public int SetLanguage(int lang) => SetLanguage_(lang);
        public void SetSaveImageType(int mask) => SetSaveImageType_(mask);
        public void SetRecogVIZ(bool enable) => SetRecogVIZ_(enable);
        public void SetRecogDG(int dgMask) => SetRecogDG_(dgMask);
        public void SetAnalyseMRZ(bool enable) => SetAnalyseMRZ_(enable);
        public void ResetIDCardID() => ResetIDCardID_();
        public int AddIDCardID(int mainId, int[] subIds, int subIdCount) => AddIDCardID_(mainId, subIds, subIdCount);
        public int AutoProcessIDCard(ref int cardType) => AutoProcessIDCard_(ref cardType);
        public int SaveImageEx(string baseFilePath, int mask) => SaveImageEx_(baseFilePath, mask);
        public int CheckDeviceOnlineEx() => CheckDeviceOnlineEx_();

        public string? GetRecogResultStr(int attr, int index)
        {
            int len = 1024;
            var sb = new StringBuilder(len);
            var ret = GetRecogResultEx_(attr, index, sb, ref len);
            return ret == 0 ? sb.ToString().Trim() : null;
        }

        public void Dispose()
        {
            if (_h != IntPtr.Zero)
            {
                Native.FreeLibrary(_h);
                _h = IntPtr.Zero;
            }
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