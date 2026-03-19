using System;
using System.Runtime.InteropServices;

namespace AlwaysOnDisplay
{
    public static class IdleDetector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        public static TimeSpan GetIdleTime()
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);

            if (!GetLastInputInfo(ref info))
                return TimeSpan.Zero;

            uint tickCount = (uint)Environment.TickCount;
            uint idleTicks = tickCount - info.dwTime;

            return TimeSpan.FromMilliseconds(idleTicks);
        }
    }
}