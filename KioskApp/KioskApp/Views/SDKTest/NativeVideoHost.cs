using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public sealed class NativeVideoHost : HwndHost
    {
        public IntPtr HostHandle { get; private set; }

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        private const int DEFAULT_WIDTH = 640;
        private const int DEFAULT_HEIGHT = 480;

        protected override HandleRef BuildWindowCore(
            HandleRef hwndParent)
        {
            HostHandle = CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                WS_CHILD |
                WS_VISIBLE |
                WS_CLIPCHILDREN |
                WS_CLIPSIBLINGS,
                0,
                0,
                DEFAULT_WIDTH,
                DEFAULT_HEIGHT,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (HostHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();

                throw new InvalidOperationException(
                    $"Failed to create Eyecool native video host. Win32 error: {error}");
            }

            return new HandleRef(this, HostHandle);
        }

        protected override void DestroyWindowCore(
            HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
            {
                try
                {
                    DestroyWindow(hwnd.Handle);
                }
                catch
                {
                    // Never allow native cleanup to crash WPF shutdown.
                }
            }

            HostHandle = IntPtr.Zero;
        }

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern bool DestroyWindow(
            IntPtr hwnd);
    }
}

//using System;
//using System.Runtime.InteropServices;
//using System.Windows.Interop;

//namespace OmniKiosk.Wpf.Views.SDKTest
//{
//    public sealed class NativeVideoHost : HwndHost
//    {
//        public IntPtr HostHandle { get; private set; }

//        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
//        {
//            HostHandle = CreateWindowEx(
//                0, "static", "",
//                WS_CHILD | WS_VISIBLE,
//                0, 0, 0, 0,
//                hwndParent.Handle,
//                IntPtr.Zero,
//                IntPtr.Zero,
//                IntPtr.Zero);

//            return new HandleRef(this, HostHandle);
//        }

//        protected override void DestroyWindowCore(HandleRef hwnd)
//        {
//            try { DestroyWindow(hwnd.Handle); } catch { }
//            HostHandle = IntPtr.Zero;
//        }

//        private const int WS_CHILD = 0x40000000;
//        private const int WS_VISIBLE = 0x10000000;

//        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
//        private static extern IntPtr CreateWindowEx(
//            int dwExStyle,
//            string lpClassName,
//            string lpWindowName,
//            int dwStyle,
//            int x,
//            int y,
//            int nWidth,
//            int nHeight,
//            IntPtr hWndParent,
//            IntPtr hMenu,
//            IntPtr hInstance,
//            IntPtr lpParam);

//        [DllImport("user32.dll", SetLastError = true)]
//        private static extern bool DestroyWindow(IntPtr hwnd);
//    }
//}