using System;
using System.Threading;
using System.Windows;
using Pickets.Services;

namespace Pickets
{
    public partial class UpdateDialog : Window
    {
        internal enum Result { Later, Skip, ReadyToInstall, OpenedReleasePage }

        private readonly UpdateInfo _update;
        private readonly bool _canInstall;
        private CancellationTokenSource? _download;

        internal Result Choice { get; private set; } = Result.Later;

        /// <summary>Path of the verified MSI when Choice is ReadyToInstall.</summary>
        internal string? MsiPath { get; private set; }

        internal UpdateDialog(UpdateInfo update)
        {
            InitializeComponent();
            _update = update;

            // The portable zip can't be upgraded by the installer; send those users to the
            // release page instead.
            _canInstall = UpdateService.IsInstalledCopy && update.MsiUrl != null;

            TitleText.Text = $"Pickets {update.Version.ToString(3)} is available";
            SubtitleText.Text = _canInstall
                ? $"You have {UpdateService.CurrentVersion.ToString(3)}. Installing closes Pickets, " +
                  "updates it (Windows will ask for permission), and starts it again. Your fences are kept."
                : $"You have {UpdateService.CurrentVersion.ToString(3)}. This copy wasn't installed with the " +
                  "installer, so download the new version from the release page.";
            NotesBox.Text = string.IsNullOrWhiteSpace(update.Notes) ? "No release notes." : update.Notes.Trim();
            InstallButton.Content = _canInstall ? "Install now" : "Open download page";

            Closing += (_, __) => _download?.Cancel();
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (!_canInstall)
            {
                UpdateService.OpenReleasePage(_update);
                Choice = Result.OpenedReleasePage;
                Close();
                return;
            }

            InstallButton.IsEnabled = SkipButton.IsEnabled = false;
            Progress.Visibility = StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Downloading update…";
            _download = new CancellationTokenSource();

            try
            {
                var progress = new Progress<double>(p => Progress.Value = p);
                MsiPath = await UpdateService.DownloadAsync(_update, progress, _download.Token);
                Choice = Result.ReadyToInstall;
                Close();
            }
            catch (OperationCanceledException)
            {
                // Dialog closed mid-download ("Later").
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("Update download", ex);
                StatusText.Text = "The update couldn't be downloaded: " + ex.Message;
                Progress.Visibility = Visibility.Collapsed;
                InstallButton.IsEnabled = SkipButton.IsEnabled = true;
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Choice = Result.Skip;
            Close();
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            Choice = Result.Later;
            Close();
        }
    }
}
