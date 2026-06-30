using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OmniKiosk.Wpf.Views
{
    public class NativeVideoHost : HwndHost
    {
        public IntPtr HostHandle { get; private set; }

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x, int y,
            int width, int height,
            IntPtr hwndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            HostHandle = CreateWindowEx(
                0,
                "STATIC",
                "",
                WS_CHILD | WS_VISIBLE,
                0, 0,
                (int)Width, (int)Height,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return new HandleRef(this, HostHandle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle);
        }
    }
}
