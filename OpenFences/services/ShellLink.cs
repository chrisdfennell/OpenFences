using System;

namespace OpenFences
{
    internal static class ShellLink
    {
        /// <summary>
        /// Create a Windows .lnk shortcut using WSH COM.
        /// </summary>
        public static void CreateShortcut(string shortcutPath, string targetPath, string? arguments = null, string? iconLocation = null, string? workingDirectory = null, string? description = null)
        {
            try
            {
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;

                dynamic shell = Activator.CreateInstance(t)!;
                dynamic link = shell.CreateShortcut(shortcutPath);
                link.TargetPath = targetPath;
                if (!string.IsNullOrWhiteSpace(arguments)) link.Arguments = arguments;
                link.WorkingDirectory = workingDirectory ?? System.IO.Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(iconLocation)) link.IconLocation = iconLocation;
                if (!string.IsNullOrWhiteSpace(description)) link.Description = description;
                link.Save();
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Resolve a .lnk file's target path (returns null on failure).
        /// </summary>
        public static string? GetShortcutTarget(string shortcutPath)
        {
            try
            {
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;

                dynamic shell = Activator.CreateInstance(t)!;
                dynamic lnk = shell.CreateShortcut(shortcutPath);
                string? tgt = lnk.TargetPath as string;
                return string.IsNullOrWhiteSpace(tgt) ? null : tgt;
            }
            catch
            {
                return null;
            }
        }
    }
}
