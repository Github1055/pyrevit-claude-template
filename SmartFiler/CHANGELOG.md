# Changelog

## [1.0.0] - 2026-03-31

Initial release.

### Features

- Batch file scanner across Downloads, Desktop, Documents, and OneDrive folders
- 25+ file type categories: Revit (.rvt, .rfa, .rte), Blender (.blend), AutoCAD (.dwg, .dxf, .bak), Word (.docx, .doc, .rtf), Excel (.xlsx, .xls, .csv), PowerPoint (.pptx, .ppt), PDF, images, shortcuts (.lnk), web links (.url), installers, archives, text files (.txt, .md, .json, .xml, .yaml)
- Automatic backup detection and deletion suggestions for .rvt.0001, .rfa.0001, .blend1, .bak, .sv$ files
- Project-aware fuzzy matching using Jaccard similarity with configurable threshold
- Revit library deep matching against D:\D CPL OFFICE\05 REVIT\000_Revit Families subfolder tree
- Acronym/alias table for project name expansion (e.g., SBH = Sworder Belcher Holt)
- SQLite learning engine -- suggestions improve from your approval history
- Multi-select triage with toggle-click selection (click to select, click again to deselect)
- Bulk actions: Approve, Defer, Delete, Select All, Deselect All buttons
- Group alternative destination -- select multiple files, browse for a folder, all get assigned
- M/X status indicators: M (green) = move pending, X (red) = delete pending, pause = deferred
- Sortable columns (File, Type, Location, Destination) with click-to-sort and arrow indicators
- Category filter bar with individual toggles, All/None buttons, persistent filter state
- File preview panel with image thumbnails, .url target display, and properties
- Alternative destination override per file or group (text input or folder browser)
- Auto-approve when setting an alternative destination
- Double-click to open files in default application
- Right-click context menu (Open, Open Folder, Approve, Set Alternative, Defer, Delete)
- Open and Delete buttons in the preview panel
- Batch move with confirmation dialog, active list removal, and completion summary
- Undo last batch -- reverses all moves from the most recent session
- Batch journaling for atomic move operations
- Source directory deduplication (prevents OneDrive redirect duplicates)
- File-level deduplication by resolved path
- Stale file detection on D: drive (90-180 days, 6 months-1 year, 1+ year buckets)
- Duplicate detection via SHA-256 hashing within source directories
- Dashboard with pending count, monthly stats, stale file summary, duplicate summary
- Dark and light Fluent themes with sun/moon toggle (persists across sessions)
- Custom color picker (accent, background, surface, text) with save/load presets
- Configurable scan source folders via Settings tab (7 defaults including OneDrive paths)
- All settings persisted to %APPDATA%\SmartFiler\settings.json
- System tray icon with right-click menu (Scan Now, Open, Exit)
- Minimize-to-tray behavior
- 80 unit tests (file categorization, fuzzy matching, SQLite repositories)
- Self-contained single-file publish support (win-x64)
- Long path support enabled via manifest
