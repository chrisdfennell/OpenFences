[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
![WPF](https://img.shields.io/badge/WPF-Desktop-0A84FF?logo=windows&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![Arch](https://img.shields.io/badge/Arch-x64%20%7C%20ARM64-555)
[![License: MIT](https://img.shields.io/badge/License-MIT-34D058)](LICENSE)
[![Release](https://img.shields.io/github/v/release/chrisdfennell/OpenFences?include_prereleases&label=release)](https://github.com/chrisdfennell/OpenFences/releases)
[![GitHub stars](https://img.shields.io/github/stars/chrisdfennell/OpenFences?style=social)](https://github.com/chrisdfennell/OpenFences/stargazers)
[![Issues](https://img.shields.io/github/issues/chrisdfennell/OpenFences)](https://github.com/chrisdfennell/OpenFences/issues)

# OpenFences

OpenFences is a lightweight, open-source WPF app for Windows that lets you organize your desktop into movable, resizable “fences.”  
Your real desktop items are shown as tiles inside fences. Nothing is copied and no shortcuts are created; a fence just decides where each desktop item appears. Group items into fences, mirror any folder as a **Folder Portal**, lasso-select across fences, and use **Auto-Import** to sort your desktop into **Apps**, **Documents**, and **System** fences in one click.

> Not affiliated with or endorsed by Stardock. “Fences” is a trademark of its respective owner. This project is an educational/utility clone built from scratch in C#.

---

## ✨ Features

- **Fences on the desktop layer**  
  Movable/resizable fence windows that sit above the wallpaper (nudged to the desktop Z-layer).

- **Fences hold your real desktop items**  
  While OpenFences runs, the normal desktop icons are hidden and each desktop item is shown in exactly one fence. Anything not in another fence lives in the **Desktop** fence. Quitting OpenFences shows the normal desktop icons again.

- **Drag in from anywhere**  
  Drop files from outside the desktop onto a fence and choose whether to move them onto the desktop or create a desktop shortcut.

- **Folder Portals**  
  A portal fence is a live view of any folder you choose; it updates as files change.

- **Remove vs. Delete**  
  Right-click an item → **Remove from fence** moves it back to the Desktop fence (nothing is deleted). **Delete** sends the real file to the Recycle Bin, after a clear confirmation.

- **Selection across fences**  
  Left-drag on empty desktop to lasso items in several fences; Ctrl+click and rubber-band selection work inside a fence.

- **Quick create & peek**  
  Right-drag on empty desktop to draw a new fence. Optionally double-click the desktop to hide/show all fences.

- **Rename & style fences**  
  Right-click a fence title bar to rename it, change sort order, icon size, or transparency, or roll it up by double-clicking the title bar.

- **Safe, persisted layout**  
  Positions and sizes are saved automatically to `%AppData%\\OpenFences\\config.json`, with a backup of the previous version in `config.json.bak` that is restored automatically if the file is ever damaged.

- **Minimize to tray**  
  The main controller window hides to the system tray; double-click the tray icon to restore.

- **Dark UI**  
  Modern, semi-transparent main window + dark menus with slim scrollbars.

- **⚡ Auto-Import Desktop Icons**  
  One click creates (or reuses) three fences and sorts your desktop items into them:
  - **Apps**: shortcuts to apps, `.exe`, `.url`, `.bat/.cmd/.ps1/.msi`, etc.
  - **Documents**: everything else (documents, images, folders, zips…)
  - **System**: special items like *This PC*, *Network* and *Recycle Bin*.  
  Your files stay where they are on the desktop; only which fence shows them changes.

---

## 📷 Screenshots

![Main Window](https://github.com/chrisdfennell/OpenFences/blob/master/OpenFences/Docs/Screenshot-1.png "Main Window")  
![Main Fence](https://github.com/chrisdfennell/OpenFences/blob/master/OpenFences/Docs/Screenshot-2.png "Main Fence")

---

## 🧰 Tech

- .NET 8, WPF + a tiny bit of WinForms (`NotifyIcon` for the tray)
- Interop: `SHGetFileInfo` for icons, `WScript.Shell` COM to create/inspect `.lnk`
- No external packages

---

## 🔧 Build & Run

### Prerequisites
- Windows 10/11  
- [.NET 8 SDK](https://dotnet.microsoft.com/download)  
- (Optional) Visual Studio 2022 with “.NET desktop development” workload

### Via Visual Studio
1. Open the solution.  
2. Set **OpenFences** as the startup project.  
3. Build & Run (F5).

### Via CLI
```bash
dotnet build
dotnet run --project OpenFences/OpenFences.csproj
```

---

## 📁 Where things go

- **Your files:** they stay on your desktop. Fences don't copy or move them.  
- **Config:** `%AppData%\\OpenFences\\config.json` (previous version kept as `config.json.bak`)  
- **Error log:** `%AppData%\\OpenFences\\error.log`  
- **Uninstall:** quit OpenFences (your desktop icons reappear), uninstall it, and optionally delete `%AppData%\\OpenFences`.

---

## 🚀 Usage

- Create a fence: **File → New Fence**, or right-drag a rectangle on empty desktop.  
- Add items: drop files from Explorer onto a fence, or use **⚡ Auto-Import**.  
- Take an item out of a fence: right-click it → **Remove from fence**.  
- Rename: right-click a fence title bar → **Rename…**.  
- Auto-import: click **⚡ Auto-Import Desktop Icons** to populate *Apps*, *Documents*, *System* fences.  
- Hide controller: minimize the main window; restore via tray icon (launching OpenFences again also brings it back).

---

## 🧭 Roadmap (ideas)

- Acrylic/Mica effects for fences (Win11)  
- Roll-up/peek animation (title-bar only)  
- Snap-to-grid & alignment guides  
- Pages / quick layouts  
- Per-fence rules (e.g., only images/docs)  
- Global hotkeys (show/hide all, new fence)  
- Stronger desktop parenting (WorkerW reparent)

---

## ⚠️ Known limitations

- Z-order on the desktop can vary by Windows build; we nudge fences toward the desktop layer to keep them behind normal windows.  
- A Folder Portal whose folder is missing or on a disconnected drive shows as *(unavailable)* until you reconnect it and restart OpenFences.  
- Multi-monitor coordinates are persisted as absolute positions (future: per-monitor DPI/arrangement awareness).

---

## 🤝 Contributing

PRs and issues welcome! If you’re proposing a new feature, please include a quick mock or description of the UI/UX.

- Fork and create a feature branch  
- `dotnet build` to ensure it compiles  
- Open a PR with a clear summary and screenshots if there are UI changes

---

## 📝 License

MIT © Christopher Fennell

---

## 🙏 Credits & Trademarks

- Built with .NET, WPF, and portions of the Windows Shell APIs.  
- Not affiliated with Stardock. “Fences” is a trademark of its respective owner.
