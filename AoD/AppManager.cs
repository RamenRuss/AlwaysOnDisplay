using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace AlwaysOnDisplay
{
    public class AppManager
    {
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        private readonly List<ManagedProcessState> _managed = new();

        // Укажи имена exe БЕЗ .exe
        private readonly string[] _targetProcesses =
        {
            "chrome",
            "msedge",
            "telegram",
            "discord",
            "explorer"
        };

        public void EnterSemiSleep()
        {
            _managed.Clear();

            var processes = Process.GetProcesses()
                .Where(p => _targetProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));

            foreach (var process in processes)
            {
                try
                {
                    var state = new ManagedProcessState
                    {
                        ProcessId = process.Id,
                        MainWindowHandle = process.MainWindowHandle,
                        OriginalPriority = process.PriorityClass
                    };

                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_MINIMIZE);
                    }

                    process.PriorityClass = ProcessPriorityClass.Idle;
                    _managed.Add(state);
                }
                catch
                {
                }
            }
        }

        public void ExitSemiSleep()
        {
            foreach (var item in _managed)
            {
                try
                {
                    var process = Process.GetProcessById(item.ProcessId);

                    if (item.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(item.MainWindowHandle, SW_RESTORE);
                    }

                    process.PriorityClass = item.OriginalPriority;
                }
                catch
                {
                }
            }

            _managed.Clear();
        }

        private class ManagedProcessState
        {
            public int ProcessId { get; set; }
            public IntPtr MainWindowHandle { get; set; }
            public ProcessPriorityClass OriginalPriority { get; set; }
        }
    }
}