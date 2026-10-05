using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Pickets.Services;

using MessageBox = Pickets.ThemedMessageBox;
using WinForms = System.Windows.Forms;

namespace Pickets
{
    // Desktop profiles: named sets of fences ("Work", "Home") to switch between by menu,
    // shortcut (Ctrl+Alt+P cycles) or on a schedule.
    public partial class MainWindow
    {
        private DeskProfile? ActiveProfile =>
            _config.Profiles.FirstOrDefault(p => p.Name.Equals(_config.ActiveProfile, StringComparison.OrdinalIgnoreCase));

        private void SwitchProfile(DeskProfile target, bool automatic = false)
        {
            var current = ActiveProfile;
            if (current == target) return;

            // The fences on screen belong to the profile we're leaving.
            if (current != null) current.Fences = LayoutSnapshots.Clone(_fences);
            _config.ActiveProfile = target.Name;
            ReplaceFences(target.Fences);
            RefreshHome();

            if (automatic)
                _tray?.ShowBalloonTip(3000, "Pickets", $"Switched to your “{target.Name}” fences.", WinForms.ToolTipIcon.Info);
        }

        private void NextProfile()
        {
            if (_config.Profiles.Count < 2) return;
            int i = _config.Profiles.IndexOf(ActiveProfile!);
            SwitchProfile(_config.Profiles[(i + 1) % _config.Profiles.Count]);
        }

        private string? AskProfileName(string title, string suggestion)
        {
            var prompt = new InputDialog(title, "Name:", suggestion);
            if (IsVisible) prompt.Owner = this;
            if (prompt.ShowDialog() != true) return null;
            var name = prompt.Value.Trim();
            if (name.Length == 0) return null;
            if (_config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show($"There's already a profile called “{name}”.", "Profiles",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
            return name;
        }

        // The first profile is the setup you have now; each new one starts as a copy of it.
        private void NewProfile()
        {
            if (_config.Profiles.Count == 0)
            {
                var first = AskProfileName("Name your current fences", "Main");
                if (first == null) return;
                _config.Profiles.Add(new DeskProfile { Name = first });
                _config.ActiveProfile = first;
            }

            var name = AskProfileName("New profile", _config.Profiles.Count == 1 ? "Work" : $"Profile {_config.Profiles.Count + 1}");
            if (name == null) { SaveConfig(); RefreshHome(); return; }

            var profile = new DeskProfile { Name = name, Fences = LayoutSnapshots.Clone(_fences) };
            _config.Profiles.Add(profile);
            SwitchProfile(profile);
            MessageBox.Show($"“{name}” starts as a copy of your current fences. Arrange it the way you want; " +
                            "switching profiles keeps each one's fences.", "Profiles",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RenameProfile(DeskProfile p)
        {
            var name = AskProfileName("Rename profile", p.Name);
            if (name == null) return;
            if (p == ActiveProfile) _config.ActiveProfile = name;
            p.Name = name;
            SaveConfig();
            RefreshHome();
        }

        private void ScheduleProfile(DeskProfile p)
        {
            var prompt = new InputDialog($"Switch to “{p.Name}” automatically",
                "Time of day, e.g. 9:00 or 17:30 (empty = never).\n" +
                "To skip weekends: 9:00 weekdays",
                (p.SwitchAt ?? "") + (p.WeekdaysOnly ? " weekdays" : ""));
            if (IsVisible) prompt.Owner = this;
            if (prompt.ShowDialog() != true) return;

            var text = prompt.Value.Trim();
            bool weekdays = text.Contains("weekday", StringComparison.OrdinalIgnoreCase);
            var time = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (time.Length == 0) { p.SwitchAt = null; p.WeekdaysOnly = false; }
            else if (ProfileSchedule.TryParseTime(time, out var t)) { p.SwitchAt = t.ToString(@"hh\:mm"); p.WeekdaysOnly = weekdays; }
            else
            {
                MessageBox.Show($"“{time}” isn't a time. Use 24-hour time like 9:00 or 17:30.", "Profiles",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SaveConfig();
        }

        private void DeleteProfile(DeskProfile p)
        {
            if (MessageBox.Show($"Delete the profile “{p.Name}” and its fence arrangement?\n\nNothing on your desktop is deleted.",
                                "Profiles", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _config.Profiles.Remove(p);
            if (_config.Profiles.Count == 1 && _config.ActiveProfile != null)
                _config.ActiveProfile = _config.Profiles[0].Name; // the one left is what's on screen
            SaveConfig();
            RefreshHome();
        }

        // ---------- Menus ----------
        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = ProfileButton, Placement = PlacementMode.Bottom };
            foreach (var p in _config.Profiles)
            {
                var when = ProfileSchedule.Describe(p);
                var item = new MenuItem
                {
                    Header = p.Name,
                    IsCheckable = true,
                    IsChecked = p == ActiveProfile,
                    InputGestureText = when.Length > 0 ? "switches " + when : ""
                };
                var target = p;
                item.Click += (_, __) => SwitchProfile(target);
                menu.Items.Add(item);
            }
            if (_config.Profiles.Count > 0) menu.Items.Add(new Separator());

            menu.Items.Add(MenuAction(_config.Profiles.Count == 0 ? "Make a profile…" : "New profile…", NewProfile));
            if (ActiveProfile is { } active)
            {
                menu.Items.Add(MenuAction($"Rename “{active.Name}”…", () => RenameProfile(active)));

                var schedule = new MenuItem { Header = "Switch automatically" };
                foreach (var p in _config.Profiles)
                {
                    var when = ProfileSchedule.Describe(p);
                    schedule.Items.Add(MenuAction($"{p.Name}{(when.Length > 0 ? " (" + when + ")" : "")}…", () => ScheduleProfile(p)));
                }
                menu.Items.Add(schedule);

                // The profile on screen can't be deleted; switch away from it first.
                var others = _config.Profiles.Where(p => p != active).ToList();
                if (others.Count > 0)
                {
                    var delete = new MenuItem { Header = "Delete" };
                    foreach (var p in others) delete.Items.Add(MenuAction($"{p.Name}…", () => DeleteProfile(p)));
                    menu.Items.Add(delete);
                }
            }
            menu.IsOpen = true;
        }

        private static MenuItem MenuAction(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, __) => action();
            return item;
        }

        private void RefreshProfileButton()
        {
            if (ProfileButton == null) return;
            ProfileButton.Content = (ActiveProfile?.Name ?? "Profiles") + "  ▾";
            ProfileButton.ToolTip = _config.Profiles.Count == 0
                ? "Keep several fence arrangements, like Work and Home, and switch between them"
                : "Switch between your fence arrangements" +
                  (_config.Options.GlobalHotkeys ? $" ({_config.Options.ProfileHotkey} goes to the next one)" : "");
        }

        // Tray: a submenu listing the profiles, rebuilt each time the tray menu opens.
        private WinForms.ToolStripMenuItem BuildTrayProfilesMenu()
        {
            var sub = new WinForms.ToolStripMenuItem("Profiles");
            sub.DropDownOpening += (_, __) =>
            {
                sub.DropDownItems.Clear();
                foreach (var p in _config.Profiles)
                {
                    var target = p;
                    sub.DropDownItems.Add(new WinForms.ToolStripMenuItem(p.Name, null, (_, __) => SwitchProfile(target))
                        { Checked = p == ActiveProfile });
                }
                if (_config.Profiles.Count > 0) sub.DropDownItems.Add(new WinForms.ToolStripSeparator());
                sub.DropDownItems.Add(new WinForms.ToolStripMenuItem("New profile…", null, (_, __) => NewProfile()));
            };
            sub.DropDownItems.Add("…"); // placeholder so the arrow shows before the first open
            return sub;
        }

        // ---------- Schedule ----------
        private System.Windows.Threading.DispatcherTimer? _profileTimer;
        private DateTime _profileCheckedUntil;

        // Checks twice a minute whether a profile's switch time has passed since the last check
        // (or while the PC slept). Times that passed while Pickets wasn't running don't count.
        private void StartProfileSchedule()
        {
            _profileCheckedUntil = DateTime.Now;
            _profileTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _profileTimer.Tick += (_, __) =>
            {
                var now = DateTime.Now;
                var due = ProfileSchedule.Due(_config.Profiles, _profileCheckedUntil, now);
                _profileCheckedUntil = now;
                if (due != null && due != ActiveProfile) SwitchProfile(due, automatic: true);
            };
            _profileTimer.Start();
        }
    }
}
