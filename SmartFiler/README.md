# Smart Filer

Personal file management app for architects. Scans your Downloads, Desktop, and Documents folders, classifies files by type (Revit, Blender, AutoCAD, Office, etc.), fuzzy-matches them to project folders on D:, and lets you approve where each file goes in a batch workflow.

Built with C# / .NET 8 / WPF. Dark and light Fluent themes.

## Features

- **Batch cleanup wizard** -- scan, review, approve, move. Weekly ritual, not background automation.
- **20+ file categories** -- Revit (.rvt, .rfa, .rte + backups), Blender (.blend + backups), AutoCAD (.dwg, .dxf + backups), Word, Excel, PowerPoint, PDF, images, shortcuts (.lnk), web links (.url), installers, archives, and more.
- **Project-aware fuzzy matching** -- tokenizes filenames and matches them to project folders on D: using Jaccard similarity. Learns from your approval history via SQLite.
- **Alias table** -- define acronyms (SBH = Sworder Belcher Holt) to improve matching accuracy.
- **Multi-select triage** -- Ctrl+click or Shift+click to select groups. Approve, defer, or delete in bulk.
- **Sortable columns** -- click column headers to sort by File, Type, Location, or Destination.
- **Category filters** -- toggle file types on/off with checkboxes. Select All / None buttons.
- **File preview** -- image thumbnails, .url target display, file properties panel.
- **Alternative destination** -- override the suggestion for any file via text input or folder browser.
- **Double-click to open** -- launches files in their default application.
- **Right-click context menu** -- Open, Open Folder, Approve, Defer, Delete, Set Alternative.
- **Undo** -- reverse the last batch of moves with one click.
- **Stale file detection** -- finds files on D: older than 90 days, grouped by age bucket.
- **Duplicate detection** -- SHA-256 hash comparison within source directories.
- **Dashboard** -- pending file count, files sorted this month, stale file summary, duplicate summary.
- **Dark / light theme** -- toggle with sun/moon button. Persists across sessions.
- **Color customization** -- hex color picker for accent, background, surface, text. Save/load presets.
- **System tray** -- minimizes to tray, right-click for Scan Now / Open / Exit.
- **Configurable scan sources** -- add/remove folders in Settings. OneDrive folders supported.

## Requirements

- Windows 10/11 (x64)
- .NET 8.0 SDK (for building from source)
- Or: self-contained publish (no SDK needed to run)

## Build

```bash
cd SmartFiler
dotnet build
```

## Run

```bash
dotnet run --project SmartFiler/SmartFiler.csproj
```

Or double-click the desktop shortcut.

## Publish (self-contained single file)

```bash
cd SmartFiler
dotnet publish SmartFiler/SmartFiler.csproj -c Release
```

Output: `SmartFiler/bin/Release/net8.0-windows/win-x64/publish/SmartFiler.exe`

Copy the publish folder to `D:\SmartFiler\` and pin to taskbar.

## Test

```bash
cd SmartFiler
dotnet test
```

80 tests covering file categorization, fuzzy matching, and SQLite repository operations.

## Project Structure

```
SmartFiler/
  SmartFiler/                   # Main WPF application
    Core/Theme/                 # Dark + light theme ResourceDictionaries
    Data/                       # Models, SQLite DB, repositories
    Services/                   # FileScanner, FileCategorizer, FuzzyMatcher,
                                # ProjectIndexer, SuggestionEngine, MoveService,
                                # StaleDetector, DuplicateDetector, PreviewService,
                                # ThemeManager, SettingsService
    ViewModels/                 # MainViewModel (MVVM with CommunityToolkit.Mvvm)
    Resources/                  # App icon, HTML preview
  SmartFiler.Tests/             # xUnit tests
```

## Default Scan Sources

- `C:\Users\{user}\Downloads`
- `C:\Users\{user}\Desktop`
- `C:\Users\{user}\Documents`
- `C:\Users\{user}\OneDrive\Desktop`
- `C:\Users\{user}\OneDrive\Documents`
- `C:\Users\{user}\OneDrive - {org}\Desktop`
- `C:\Users\{user}\OneDrive - {org}\Documents`

Configurable in Settings tab.

## Data Storage

- **SQLite database**: `%APPDATA%\SmartFiler\smartfiler.db` (WAL mode)
- **Settings**: `%APPDATA%\SmartFiler\settings.json`
- **Theme preference**: `%APPDATA%\SmartFiler\theme.txt`

## License

MIT
