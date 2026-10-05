[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
![WPF](https://img.shields.io/badge/WPF-Desktop-0A84FF?logo=windows&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![Arch](https://img.shields.io/badge/Arch-x64%20%7C%20ARM64-555)
[![License: MIT](https://img.shields.io/badge/License-MIT-34D058)](LICENSE)
[![Release](https://img.shields.io/github/v/release/chrisdfennell/Pickets?include_prereleases&label=release)](https://github.com/chrisdfennell/Pickets/releases)
[![GitHub stars](https://img.shields.io/github/stars/chrisdfennell/Pickets?style=social)](https://github.com/chrisdfennell/Pickets/stargazers)
[![Issues](https://img.shields.io/github/issues/chrisdfennell/Pickets)](https://github.com/chrisdfennell/Pickets/issues)

# Pickets

*Formerly **OpenFences**.* A free, open-source alternative to Stardock Fences.

**Website:** [chrisdfennell.github.io/Pickets](https://chrisdfennell.github.io/Pickets/) · [Privacy policy](https://chrisdfennell.github.io/Pickets/privacy.html) · [Download](https://github.com/chrisdfennell/Pickets/releases/latest)

Pickets organizes your Windows desktop into movable, resizable **fences**. Your real desktop items show up as tiles inside fences. Nothing is copied or moved and no shortcuts are created: a fence only decides where each desktop item appears, and quitting Pickets brings your normal desktop back.

![Pickets in action: fences with a picture and a looping video background that fade with the fence's transparency](Pickets/Docs/backgrounds-demo.gif "Pickets in action")

> Not affiliated with or endorsed by Stardock. “Fences” is a trademark of its respective owner. Pickets is an independent project, written from scratch in C#.

---

## 📦 Install

Requires Windows 10 or 11. No .NET installation is needed.

- **Installer (recommended):** download `Pickets-x64.msi` (most PCs) or `Pickets-arm64.msi` (ARM devices such as Snapdragon laptops) from the [latest release](https://github.com/chrisdfennell/Pickets/releases/latest).
- **Portable:** `Pickets-x64-portable.exe` or `Pickets-arm64-portable.exe`, a single file: put it anywhere and run it.
- **winget:** `winget install chrisdfennell.Pickets` (submitted to the Windows Package Manager; works once Microsoft approves it).

After that, Pickets checks GitHub for new versions and installs them when you say so.

**Upgrading from OpenFences?** Install Pickets over it (or accept the update OpenFences offers). Your fences, settings and saved layouts move over automatically the first time Pickets starts. Start it once from the Start menu after the update; older versions can't restart the app under its new name.

---

## ✨ Features

### Organize

- **Fences hold your real desktop items.** Each desktop item shows in exactly one fence; anything not in another fence lives in the **Desktop** fence.
- **Tabs inside fences.** Split a busy fence into tabs (fence menu → **Add tab…**). Click a tab or press **Ctrl+Tab** to switch, drag tiles onto a tab to move them, right-click a tab to rename or delete it.
- **Folder portals.** A fence that shows any folder live, including on USB drives and network shares: if the folder can't be reached it shows as unavailable and comes back by itself once it can. Double-click a subfolder to browse into it (with a back button and breadcrumbs); point it at another folder with **Change folder…** or by dropping a folder on it. **Filter…** narrows it to certain files (`.pdf`, `screenshot*`) and/or what changed recently, for a "Recent downloads" or "This week's screenshots" fence.
- **Recent files.** One click on the Home page adds a fence of the files you opened lately, newest first.
- **Auto-Import.** One click sorts your desktop into **Apps**, **Documents** and **System** fences.
- **Auto-organize rules.** New desktop files go straight to the right fence, by type (apps, folders, file extensions), name (`invoice*`), age (not changed in 30 days) or size (larger than 100 MB), and the Desktop fence can be re-sorted on demand.
- **Drag tiles anywhere.** Onto another fence to move them, within a fence to arrange your own order, or into Explorer, email or any app as real files. Files dragged in from elsewhere can be moved onto the desktop or added as a shortcut.
- **Move to fence.** Right-click tiles → **Move to fence…** (another fence, or one of its tabs) or **Move to new fence**, which creates a fence right beside the current one holding the selection.
- **Select across fences.** Left-drag on empty desktop to lasso items in several fences; Ctrl+click and rubber-band selection work inside a fence.
- **Undo.** **Ctrl+Z** in a fence undoes the last organizing step: moving or rearranging tiles, **Remove from fence**, tab changes, a deleted fence, Auto-Import. The tray menu's **Undo** says what it will undo. Up to 30 steps per session; files sent to the Recycle Bin come back from there.
- **Close or delete.** ✕ closes a fence and it stays closed (it keeps its items) until you reopen it from the Pickets window; **Delete Fence…** removes it and its items go back to the Desktop fence. Nothing on your disk is deleted either way.

### Find and open

- **Fences in front of your windows.** **Ctrl+Alt+Space** brings every fence above your open apps, so you can use them without minimizing anything. Click another window or press Esc and they go back.
- **Open all.** The fence menu's **Open all** opens everything in a fence (or tab) at once, e.g. a "Work" fence that starts your work apps. **Close all** is the other half: it closes the running apps the fence starts, the same as clicking each window's X, so apps still ask to save your work.
- **Search every fence.** **Ctrl+Alt+F** searches every fence and tab: matches are listed (Enter opens one), fences come to the front and everything else fades out.
- **Quick Look.** Press **Space** on a tile for a large preview: pictures, video and audio with sound, text and code, or file details. Arrow keys step through the fence; Enter opens.
- **The real Windows right-click menu.** Open with, Send to, Properties and menu entries other apps add, plus **Remove from fence**. **Rename** (or F2) renames the actual file, and it stays in its fence, as do files renamed in Explorer or saved by apps such as Office.
- **Keyboard friendly.** Arrow keys, Home/End and type-a-letter move through a fence; Space previews, Enter opens, Delete recycles, F2 renames, Esc clears the selection, Backspace goes up a folder in a portal.

### Make it yours

- **Light or dark.** Pickets follows Windows' light or dark app mode, or pick one in Settings. The Pickets window, menus, dialogs and fences all switch; fences keep any color you gave them, with text that stays readable.
- **Style each fence.** Colors (presets or any custom color), title size, transparency, **frosted glass**, or a **picture or looping video background** (fill, fit or stretch, optional darkening, sound on or off per video).
- **Thumbnails.** Pictures and videos show a preview instead of a generic icon; file extensions can be hidden. Hover a tile for its full name, type, size, date and folder.
- **Icons or a list.** Fence menu → **View → List** shows one compact row per item with its size and date, handy for long fences and portals.
- **Size things quickly.** **Ctrl + mouse wheel** over a fence changes its icon size; **Fit to contents** (fence menu) sizes the fence to its tiles, and **Keep fitted to contents** makes it grow and shrink with them. Rolled-up fences show how many items they hold.
- **Roll up and peek.** Double-click a title to roll a fence up; rest the mouse on it (or drag files over it) to open it until you move away.
- **Snap and align.** Fences snap to screen edges and to each other while you move or resize them, with an optional grid. Hold **Alt** to move freely. Lock a fence to stop accidental moves.
- **Stacks.** Fences snapped one under another stay together: drag any title bar to move the whole stack, and rolling one up pulls the ones below it up. Turn on **One open fence per stack** in Settings for an accordion that keeps a tall column compact. **Alt**-drag takes a fence out of its stack.

### Stays out of your way

- **One home for everything.** The Pickets window lists every fence with its state (shown, rolled up, closed) and buttons to open, close, locate or delete it, plus quick actions, a **Settings** page with simple switches, and About. It hides to the system tray and reopens where you left it.
- **Global shortcuts.** **Ctrl+Alt+H** hides or shows all fences from anywhere; all three shortcuts can be changed in Settings.
- **Multi-monitor aware.** Each fence remembers where you put it for every monitor setup (docked, undocked, projector) and is never left off-screen.
- **Desktop profiles.** Keep several fence arrangements, such as Work and Home, and switch from the Home page, the tray, **Ctrl+Alt+P**, or automatically at a time of day.
- **Layouts you can undo.** Save and restore whole layouts; one is saved automatically before big changes such as Auto-Import. **Export** and **Import** move your fences and rules to another PC.
- **Sharp on every monitor.** Fences and windows render at each monitor's own scaling, so a 150% laptop next to a 100% monitor looks crisp on both.
- **Updates itself.** Checks GitHub at startup and once a day, shows what's new and installs on request (one Windows permission prompt), keeping your fences. Can be turned off.
- **Safe settings.** Saved continuously with a backup copy that's restored automatically if the file is ever damaged.
- **Accessible.** Press **F1** for every keyboard shortcut. Screen readers such as Narrator read each fence and tile (with its position and whether it's selected), **F6** moves between fences, and rolling up, switching tabs and undo are announced. Under a Windows high-contrast theme, fences, menus and the Pickets window use the theme's own colors.

---

## 📷 Screenshots

![Fences on the desktop: Apps, a Documents fence with Work and Personal tabs, System, a Projects folder portal, a rolled-up Archive fence and a Recent files fence in list view](Pickets/Docs/screenshot-desktop.png "Fences on the desktop")

![The Pickets window: quick actions, desktop profiles and every fence with its state](Pickets/Docs/screenshot-app.png "The Pickets window")

![The Pickets window in the light theme](Pickets/Docs/screenshot-app-light.png "Light theme")

![Quick Look previewing a picture from a fence](Pickets/Docs/screenshot-quicklook.png "Quick Look")

![Ctrl+Alt+F search: matching items are listed and everything else fades out](Pickets/Docs/screenshot-search.png "Search every fence")

![The first-run welcome](Pickets/Docs/screenshot-welcome.png "Welcome")

<sub>Screenshots are rendered from the real UI with demo content by `tools/Screenshots` (`dotnet run --project tools/Screenshots -- Pickets/Docs`); the backgrounds clip is recorded by `tools/DemoVideo` (needs ffmpeg).</sub>

---

## 🚀 Using Pickets

- **Create a fence:** right-drag a rectangle on empty desktop, or **New fence** on the Pickets window's Home page. The right-drag menu can also put a **folder portal** in that box.
- **Fill it:** drag tiles in from other fences, drop files from Explorer, or use **Auto-import desktop** on the Home page.
- **Fence options:** right-click a fence's title bar (or click ✎) to rename it, sort, change icon size, add a tab, set a color, background, transparency or title size, lock it, or delete it.
- **Find something:** **Ctrl+Alt+F**, type, Enter. Or select a tile and press **Space** to preview it.
- **Use fences while apps are open:** **Ctrl+Alt+Space**; click another window or press Esc when you're done.
- **Hide or show all fences:** **Ctrl+Alt+H**, or double-click empty desktop if that's turned on in Settings.
- **Reopen a closed fence:** open the Pickets window; closed fences are listed on Home with an **Open** button.
- **Save or restore a layout:** **Settings → Layouts**. To move to another PC: **Export…** there, then **Import…** on the new PC.
- **Switch between Work and Home fences:** the profile button at the top of the Home page (**Make a profile…** the first time), or **Ctrl+Alt+P**.
- **Rules for new files:** **Settings → Organizing → Edit rules…**.
- **Start over:** **Settings → Start over → Delete all fences…** (the current layout is saved first).
- **Bring back the window:** double-click the tray icon, or start Pickets again.
- **See every shortcut:** press **F1** in a fence or in the Pickets window.
- **Desktop icons missing after a crash:** Start menu → **Pickets - Restore desktop icons**, or run `Pickets.exe --restore-icons`. Exiting Pickets normally always brings them back.
- **Report a problem:** **About → Copy diagnostics** copies your version, Windows, monitors, settings and recent errors (with your user and PC names replaced) to paste into an [issue](https://github.com/chrisdfennell/Pickets/issues).

---

## 📁 Where things go

- **Your files:** they stay where they are. Fences don't copy or move them.
- **App:** `C:\Program Files\Pickets\` (installer) or wherever you put the portable exe.
- **Settings and fences:** `%AppData%\Pickets\config.json` (previous version kept as `config.json.bak`).
- **Saved layouts:** `%AppData%\Pickets\layouts\`
- **Error log:** `%AppData%\Pickets\error.log`
- **Coming from OpenFences:** `%AppData%\OpenFences` is moved to `%AppData%\Pickets` on first start.
- **Uninstall:** quit Pickets (your desktop icons reappear), uninstall it from Windows Settings, and optionally delete `%AppData%\Pickets`.

Pickets collects no personal data and has no telemetry; see the [privacy policy](https://chrisdfennell.github.io/Pickets/privacy.html).

---

## 🔧 Build and run

You need Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download) (Visual Studio 2022 with the ".NET desktop development" workload is optional).

```bash
dotnet build
dotnet run --project Pickets/Pickets.csproj
dotnet test tests/Pickets.Tests
dotnet test tests/Pickets.UiTests   # opens real windows for a few seconds
```

**Try a build next to your installed Pickets:** set `PICKETS_SANDBOX` to a folder and start it. That copy keeps its settings in `<folder>\AppData`, treats `<folder>\Desktop` as the desktop, and leaves your real desktop, shortcuts and startup setting alone:

```powershell
$env:PICKETS_SANDBOX = "$env:TEMP\PicketsSandbox"; dotnet run --project Pickets/Pickets.csproj
```

In Visual Studio, open `Pickets.sln`, set **Pickets** as the startup project and press F5. The MSI installer is built with WiX (`Pickets.Installer`); CI builds it for x64 and ARM64 on every tagged release.

---

## 🧰 Tech

- .NET 8 and WPF, with a little WinForms for the tray icon.
- Windows Shell integration: the real context menu (`IContextMenu`), thumbnails (`IShellItemImageFactory`), icons (`SHGetFileInfo`), shortcuts (`WScript.Shell`).
- Win32: desktop-layer placement and stacking, `RegisterHotKey` for global shortcuts, accent-policy blur for frosted glass.
- No third-party packages in the app; tests use xUnit.

---

## 🧭 Roadmap (ideas)

- Different fences per virtual desktop
- Sticky-note fences
- Translations

Ideas and votes are welcome in [issues](https://github.com/chrisdfennell/Pickets/issues).

---

## ⚠️ Known limitations

- Z-order on the desktop can vary by Windows build; fences are kept in the desktop layer behind normal windows (search and Locate bring them to the front briefly).
- A folder portal whose folder is missing or on a disconnected drive shows as *(unavailable)* until it can be reached again (checked every few seconds), or until you point it at another folder with **Change folder…**.
- A global shortcut that another app already uses can't be registered; Settings flags it so you can pick another.
- Background videos need Windows' codecs: MP4 and WMV work out of the box; MKV and WebM may need the free extensions from the Microsoft Store. Animated GIFs show their first frame.

---

## 🤝 Contributing

PRs and issues welcome! If you're proposing a new feature, please include a quick mock or description of the UI.

- Fork and create a feature branch.
- Make sure `dotnet build` and `dotnet test tests/Pickets.Tests` pass (CI runs the tests on every push).
- Open a PR with a clear summary, and screenshots if the UI changes.
- Maintainers: see [docs/RELEASING.md](docs/RELEASING.md) for releases, code signing and winget.

---

## 📝 License

MIT © Christopher Fennell

---

## 🙏 Credits and trademarks

- Built with .NET, WPF and the Windows Shell APIs.
- Not affiliated with Stardock. “Fences” is a trademark of its respective owner.
