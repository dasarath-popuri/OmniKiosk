using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OmniKiosk.Wpf.Views.SDKTest
{
    public sealed class NativeVideoHost : HwndHost
    {
        public IntPtr HostHandle { get; private set; }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            HostHandle = CreateWindowEx(
                0, "static", "",
                WS_CHILD | WS_VISIBLE,
                0, 0, 0, 0,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return new HandleRef(this, HostHandle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            try { DestroyWindow(hwnd.Handle); } catch { }
            HostHandle = IntPtr.Zero;
        }

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);
    }
}