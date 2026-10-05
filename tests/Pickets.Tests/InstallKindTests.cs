using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class InstallKindTests
    {
        private static readonly string[] ProgramFiles = { @"C:\Program Files", @"C:\Program Files (x86)" };
        private const string UserPrograms = @"C:\Users\me\AppData\Local\Programs";

        [Theory]
        [InlineData(@"C:\Program Files\Pickets\", InstallKind.AllUsers)]
        [InlineData(@"c:\program files (x86)\Pickets\", InstallKind.AllUsers)]
        [InlineData(@"C:\Users\me\AppData\Local\Programs\Pickets\", InstallKind.JustMe)]
        [InlineData(@"C:\Users\me\Downloads\", InstallKind.Portable)]
        [InlineData(@"C:\Program FilesX\Pickets\", InstallKind.Portable)] // not inside Program Files
        [InlineData(@"C:\Users\me\AppData\Local\ProgramsOld\Pickets\", InstallKind.Portable)]
        public void The_install_kind_comes_from_where_Pickets_runs(string dir, InstallKind expected)
        {
            Assert.Equal(expected, UpdateService.KindOf(dir, ProgramFiles, UserPrograms));
        }

        [Theory]
        [InlineData("203.0.113.7\n", "203.0.113.7")]
        [InlineData("  2001:db8::1 ", "2001:db8::1")]
        [InlineData("123", null)]                 // shorthand IPAddress would otherwise accept
        [InlineData("<html>error</html>", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Public_ip_answers_must_be_real_addresses(string? body, string? expected)
        {
            Assert.Equal(expected, SystemMonitor.ParsePublicIp(body));
        }

        [Fact]
        public void Public_ip_is_asked_again_on_a_new_connection_or_after_15_minutes()
        {
            var t = new System.DateTime(2026, 10, 5, 12, 0, 0, System.DateTimeKind.Utc);
            Assert.True(SystemMonitor.PublicIpDue(t, default, null, "192.168.1.5"));            // never asked
            Assert.False(SystemMonitor.PublicIpDue(t.AddMinutes(5), t, "192.168.1.5", "192.168.1.5"));
            Assert.True(SystemMonitor.PublicIpDue(t.AddMinutes(5), t, "192.168.1.5", "10.0.0.8")); // new network
            Assert.True(SystemMonitor.PublicIpDue(t.AddMinutes(15), t, "192.168.1.5", "192.168.1.5"));
        }

        [Fact]
        public void A_public_ip_only_counts_for_the_connection_it_was_asked_on()
        {
            // Same network: the answer (or the failure) shows.
            Assert.Equal(("203.0.113.7", false), SystemMonitor.PublicIpToShow("192.168.1.5", "192.168.1.5", "203.0.113.7", false));
            Assert.Equal(((string?)null, true), SystemMonitor.PublicIpToShow("192.168.1.5", "192.168.1.5", null, true));
            // A failed re-check keeps the address it already had.
            Assert.Equal(("203.0.113.7", false), SystemMonitor.PublicIpToShow("192.168.1.5", "192.168.1.5", "203.0.113.7", true));
            // Switched networks: nothing until the new lookup answers ("looking up…"), not the old address.
            Assert.Equal(((string?)null, false), SystemMonitor.PublicIpToShow("10.0.0.8", "192.168.1.5", "203.0.113.7", false));
            Assert.Equal(((string?)null, false), SystemMonitor.PublicIpToShow(null, "192.168.1.5", "203.0.113.7", false));
        }

        [Fact]
        public void Each_kind_updates_from_its_own_installer()
        {
            Assert.Equal("Pickets-x64.msi", UpdateService.MsiAssetName(InstallKind.AllUsers, "x64"));
            Assert.Equal("Pickets-arm64-user.msi", UpdateService.MsiAssetName(InstallKind.JustMe, "arm64"));
        }
    }
}
