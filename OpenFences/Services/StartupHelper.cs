using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace OpenFences
{
    internal static class StartupHelper
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VALUE_NAME = "OpenFences";

        public static void SetRunAtStartup(bool enable)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RUN_KEY, true);
            if (key == null) return;

            if (enable)
            {
                var exe = GetExecutablePath();
                key.SetValue(VALUE_NAME, $"\"{exe}\"");
            }
            else
            {
                if (key.GetValue(VALUE_NAME) != null)
                    key.DeleteValue(VALUE_NAME, throwOnMissingValue: false);
            }
        }

        public static bool IsRunAtStartupEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
            var val = key?.GetValue(VALUE_NAME) as string;
            return !string.IsNullOrEmpty(val);
        }

        private static string GetExecutablePath()
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe!;
            exe = Assembly.GetEntryAssembly()?.Location;
            return exe ?? "";
        }
    }
}