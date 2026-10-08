# AGENTS.md — Instructions & Architecture Guide for AI Agents

Welcome! This document provides complete architectural context, established patterns, and critical constraints for the **Finder** project. Any AI assistant or developer working on this codebase should read and respect these rules to ensure seamless updates and prevent regressions.

---

## 1. Project Overview
- **Project Name:** Finder (`FinderApp`)
- **Technology Stack:** C# / .NET 8 (Windows Desktop, WPF)
- **Primary Goal:** Ultra-fast, intelligent system-wide file and application search (similar to macOS Spotlight / Alfred / Raycast), with instant hotkey invocation (`Alt + Space`), sleek dark UI, and minimal memory footprint (< 60 MB RAM).

---

## 2. Core Architecture & File Structure

```
Finder/
├── App.xaml / App.xaml.cs       # Application lifecycle & global resources
├── MainWindow.xaml / .cs        # Main floating search window, slidable category bar & results list
├── FinderApp.csproj             # .NET 8 WPF project file
├── Models/
│   ├── FileRecord.cs            # Ultra-compact in-memory struct (DirIndex, Name, IsDirectory)
│   ├── CategoryCount.cs         # Category filter pill model with count & styling
│   ├── SearchResultItem.cs      # Bound to results list (name, path, size, lazy icon)
│   └── SearchResponse.cs        # Search payload containing Items and Categories
├── Services/
│   ├── FastDirectoryScanner.cs  # Native Win32 FindFirstFileExW high-speed disk scanner
│   ├── FileIndexService.cs      # Background indexing, memory management & search coordinator
│   ├── FuzzySearchEngine.cs     # Typo tolerance, strict extensions & acronym matching
│   ├── FileCategoryHelper.cs    # File extension to category classifier
│   ├── HotKeyManager.cs         # Win32 RegisterHotKey (Alt+Space / Ctrl+Space fallback)
│   └── IconHelper.cs            # Win32 SHGetFileInfo on-demand icon resolution with cache
```

---

## 3. Strict Rules & Constraints (DO NOT VIOLATE!)

### ⚠️ RULE 1: Stop Running Processes Before Building
If `FinderApp.exe` is running, running `dotnet build` or `dotnet publish` **WILL FAIL** with file lock error (`MSB3026`).
**Always run:**
```powershell
powershell -Command "Stop-Process -Name FinderApp, Finder -Force -ErrorAction SilentlyContinue"
```
before building or publishing.

### ⚠️ RULE 2: Ultra-Low Memory Budget (< 50 MB RAM)
- **ChunkedRecordList:** File records are stored in 64 KB non-LOH contiguous chunks (4096 records per chunk), preventing Large Object Heap fragmentation and eliminating array duplication.
- **StringPool:** Filenames are deduplicated across folders using a zero-allocation 128 KB direct-mapped pool.
- **SearchCandidate Value Structs:** Matches are collected as ~40-byte structs in contiguous arrays, never allocating thousands of `SearchResultItem` heap class objects.
- **On-Demand Resolution:** Full paths, icons, sizes, and highlight indices are resolved strictly on-demand for the visible 40 items (`PageSize = 40`) in `LoadNextBatch()`.
- **Zero-Allocation Search:** `FuzzySearchEngine.QuickMatch` performs case-insensitive comparisons without allocating lowercase strings or 2D arrays.
- **Periodic Trimming:** Working set is compacted via `psapi.dll`'s `EmptyWorkingSet`.

### ⚠️ RULE 3: Floating Window Behavior
- `WindowStyle="None"`, `AllowsTransparency="True"`, `Topmost="True"`, `ShowInTaskbar="False"`.
- **Draggability:** Draggable anywhere across the screen via `Window_MouseLeftButtonDown` on the outer border (ignoring clicks on textboxes).
- **Auto-Hide:** Automatically hides when clicking anywhere outside the window via `OnDeactivated`.
- **Hotkey:** Toggles visibility via global hotkey (`Alt + Space`).

### ⚠️ RULE 4: Slidable Category Filter Bar
- Category filter pills (`All`, `Code`, `Folder`, `Document`, `Image`, etc.) are placed inside `CategoryScrollViewer`.
- **Sliding methods supported:**
  1. **Mouse Drag / Swipe:** User clicks and drags horizontally.
  2. **Mouse Scroll Wheel:** Scrolling the wheel over the category bar scrolls horizontally (`CategoryScrollViewer_PreviewMouseWheel`).
  3. **Arrow Buttons:** `‹` and `›` chevron buttons slide left and right by 120px.
  4. **Touchpad:** Two-finger horizontal scroll gestures.
- **Drag vs. Click Collision Guard:** `_isCategoryDragging` and `_categoryDragSuppressUntil` ensure that dragging to slide **NEVER** accidentally triggers a category button click. Do NOT remove this suppression logic.

### ⚠️ RULE 5: Infinite Scroll / Pagination
- Initial search results load 40 items (`PageSize = 40`).
- Scrolling down near the bottom (`ResultsList_ScrollChanged`) or pressing the Down arrow near the end triggers `LoadNextBatch()` seamlessly.

### ⚠️ RULE 6: Search Accuracy & Quality
- Strict extension queries (e.g. searching `.pdf`) MUST match `.pdf` files, never unrelated extensions like `.svg`.
- Acronym matching (e.g. `vsc` -> `Visual Studio Code`) is supported via word-boundary acronym matching in `FuzzySearchEngine.cs`.

### ⚠️ RULE 7: Manual Re-Indexing Support
- Manual re-indexing can be triggered by the user via the `[↻ Reindex]` button in the status footer or via `Ctrl + R` / `F5` shortcuts.
- Re-indexing cancels any current scan, resets in-memory chunked records and directories via `ChunkedRecordList.Clear()`, rescans all priority folders and drives in the background, trims memory, and updates any active search view.

### ⚠️ RULE 8: Real-Time Live File Sync (FileSystemWatcher)
- `FileIndexService.cs` maintains active `FileSystemWatcher` instances across all ready fixed/removable drives.
- Files or folders created, deleted, or renamed anywhere on the PC are automatically captured and synchronized with the in-memory chunked records in real-time.
- Tombstones (`record.Name == null`) are used for zero-allocation deletions, and active search views refresh automatically. Manual re-indexing is never required for normal file changes.

---

## 4. Useful Development Commands

- **Kill running app:**
  ```powershell
  Stop-Process -Name FinderApp, Finder -Force -ErrorAction SilentlyContinue
  ```
- **Build (Debug):**
  ```powershell
  dotnet build
  ```
- **Build (Release):**
  ```powershell
  dotnet build -c Release
  ```
- **Run Debug App:**
  ```powershell
  dotnet run
  ```
- **Publish Standalone Single-File Portable Executable & Installer:**
  ```powershell
  Stop-Process -Name FinderApp, Finder, FinderSetup -Force -ErrorAction SilentlyContinue
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./Portable
  ```

---

## 5. Coding & UI Style Guidelines
- **UI Colors:**
  - Background: `#161821` (Outer window), `#12141C` (Category bar)
  - Borders: `#2C3246`, `#212533`
  - Accent / Primary: `#38BDF8` (Sky blue)
  - Text Primary: `#FFFFFF`
  - Text Muted: `#94A3B8`, `#64748B`
- **Typography:** Segoe UI / Segoe UI Variable Text.
- **Micro-Interactions:** Smooth hover effects with border highlights and subtle background brightening.
