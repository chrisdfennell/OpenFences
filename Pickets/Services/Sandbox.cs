using System;
using System.IO;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// Sandbox mode, for tests and for trying a build next to the installed Pickets: set the
    /// PICKETS_SANDBOX environment variable to a folder and Pickets keeps its settings in
    /// &lt;folder&gt;\AppData and treats &lt;folder&gt;\Desktop as the desktop. It then leaves the real
    /// desktop alone: icons aren't hidden, no desktop clicks are watched, no global shortcuts,
    /// no run-at-startup, no update checks, and it runs alongside a normal copy.
    /// </summary>
    internal static class Sandbox
    {
        public const string Variable = "PICKETS_SANDBOX";

        public static string? Root { get; } = ReadRoot();

        public static bool IsActive => Root != null;

        /// <summary>Settings, layouts and the error log.</summary>
        public static string DataDir => IsActive
            ? Path.Combine(Root!, "AppData")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pickets");

        /// <summary>The desktop folder(s): the user's and the public one, or the sandbox's own.</summary>
        public static string[] DesktopRoots => IsActive
            ? new[] { Path.Combine(Root!, "Desktop") }
            : new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

        /// <summary>Where new desktop files go (moved or shortcut onto the desktop).</summary>
        public static string UserDesktop => DesktopRoots[0];

        /// <summary>Added to the single-instance names, so a sandbox runs next to the real Pickets
        /// (and next to sandboxes in other folders).</summary>
        public static string InstanceSuffix =>
            IsActive ? "." + Convert.ToHexString(BitConverter.GetBytes(StableHash(Root!.ToUpperInvariant()))) : "";

        private static string? ReadRoot()
        {
            var value = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                var root = Path.GetFullPath(value.Trim());
                Directory.CreateDirectory(Path.Combine(root, "AppData"));
                Directory.CreateDirectory(Path.Combine(root, "Desktop"));
                return root;
            }
            catch { return null; }
        }

        // string.GetHashCode differs per process; the instance name must not.
        private static uint StableHash(string s) =>
            s.Aggregate(2166136261u, (h, c) => (h ^ c) * 16777619u);
    }
}
