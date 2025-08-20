# OpenFences

OpenFences is a lightweight, open-source WPF app for Windows that lets you organize your desktop into movable, resizable “fences.”  
Create and rename fences, drag files in to auto-create shortcuts, minimize the controller to the system tray, toggle desktop icons, and use the **Auto-Import** button to sort your existing desktop into **Apps**, **Documents**, and **System** fences.

> Not affiliated with or endorsed by Stardock. “Fences” is a trademark of its respective owner. This project is an educational/utility clone built from scratch in C#.

---

## ✨ Features

- **Fences on the desktop layer**  
  Movable/resizable fence windows that sit above the wallpaper (nudged to the desktop Z-layer).

- **Drag-in shortcuts**  
  Drag files or folders onto a fence to create a `.lnk` shortcut inside that fence’s backing folder.

- **Rename fences**  
  Click the ✎ on a fence titlebar (or right-click) to rename, e.g., “Apps”, “Games”, etc.

- **Persisted layout**  
  Sizes/positions saved to `%AppData%\OpenFences\config.json`.

- **Minimize to tray**  
  The main controller window hides to the system tray; double-click the tray icon to restore.

- **Dark UI**  
  Modern, semi-transparent main window + dark menus with slim scrollbars.

- **Desktop icons toggle**  
  Hide/show default Windows desktop icons from **View → Toggle Desktop Icons**.

- **⚡ Auto-Import Desktop Icons**  
  One click creates (or reuses) three fences and populates them:
  - **Apps** – `.lnk` that point to apps, `.exe`, `.url`, `.bat/.cmd/.ps1/.msi`, etc.
  - **Documents** – everything else (documents, images, folders, zips…)
  - **System** – shortcuts to *This PC*, *Control Panel*, *Network*, *Recycle Bin*, and your home folder.  
  Import **does not move** your desktop items; it creates shortcuts in fences.

---

## 📷 Screenshots

![Main Window](/docs/Screenshot-1 "Main Window")
![Main Fence](/docs/Screenshot-2 "Main Fence")

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
- (Optional) Visual Studio 2022 with “.NET desktop development”

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

- Fence backing folders:C:\Users\<you>\Desktop\Fences\<FenceName>
- Config:%AppData%\OpenFences\config.json
- Uninstall = close the app, delete the exe/folder, and (optionally) delete the config + Desktop\Fences if you don’t want to keep your shortcuts.

---

## 🚀 Usage

- Create a fence: File → New Fence or the ➕ New Fence button.
- Drag files/folders onto the fence to add shortcuts inside it.
- Rename: click ✎ on a fence title bar → enter a new name.
- Auto‑import: click ⚡ Auto‑Import Desktop Icons to populate Apps, Documents, System fences.
- Hide desktop icons: View → Toggle Desktop Icons.
- Hide controller: minimize the main window; restore via tray icon.

---

## 🧭 Roadmap (ideas)
- Acrylic/Mica effects for fences (Win11)
- Roll‑up/peek animation (title‑bar only)
- Snap‑to‑grid & alignment guides
- Pages / quick layouts
- Per‑fence rules (e.g., only images/docs)
- Global hotkeys (show/hide all, new fence)
- Stronger desktop parenting (WorkerW reparent)

---

## ⚠️ Known limitations
- Z‑order on the desktop can vary by Windows build; we nudge fences toward the desktop layer to keep them behind normal windows.
- Auto‑import creates shortcuts; it does not move or delete your actual desktop items.
- Multi‑monitor coordinates are persisted as absolute positions (future: per‑monitor DPI/arrangement awareness).

---

## 🤝 Contributing

- PRs and issues welcome!If you’re proposing a new feature, please include a quick mock or description of the UI/UX.
- Fork, create a feature branch
- dotnet build to ensure it compiles
- Open a PR with a clear summary and screenshots if UI changes

---

## 📝 License

MIT © Christopehr Fennell

---

## 🙏 Credits & Trademarks

- Built with .NET, WPF, and portions of the Windows Shell APIs.
- Not affiliated with Stardock. “Fences” is a trademark of its respective owner.
