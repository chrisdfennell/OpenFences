using System;
using System.IO;
using Microsoft.Win32;

namespace Pickets.Services
{
    /// <summary>
    /// Pickets used to be called OpenFences. On the first start after the rename, carry the old
    /// app's settings over so nothing is lost: the %AppData% folder (fences, layouts, log) and
    /// the "start with Windows" entry.
    /// </summary>
    internal static class LegacyMigration
    {
        private const string OldName = "OpenFences";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Run()
        {
            MoveDataFolder();
            MoveStartupEntry();
        }

        // %AppData%\OpenFences → %AppData%\Pickets, only when Pickets has no data of its own yet.
        private static void MoveDataFolder()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var oldDir = Path.Combine(appData, OldName);
                var newDir = Path.Combine(appData, "Pickets");
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                    Directory.Move(oldDir, newDir);
            }
            catch { /* keep going with a fresh folder rather than fail to start */ }
        }

        // The old entry points at OpenFences.exe, which the upgrade removed; replace it.
        private static void MoveStartupEntry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key?.GetValue(OldName) == null) return;
                key.DeleteValue(OldName, throwOnMissingValue: false);
                StartupHelper.SetRunAtStartup(true);
            }
            catch { /* ignore */ }
        }
    }
}
