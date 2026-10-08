using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FinderApp.Models;

namespace FinderApp.Services;

public class FileIndexService
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    private readonly List<string> _directories = new(8192);
    private readonly Dictionary<string, int> _dirToIndex = new(8192, StringComparer.OrdinalIgnoreCase);
    private readonly ChunkedRecordList _files = new();
    private readonly object _dirLock = new();

    public int TotalFilesIndexed => _files.Count;

    public event Action<int>? IndexCountChanged;
    public event Action? IndexFinished;
    public event Action? LiveIndexUpdated;

    private readonly List<FileSystemWatcher> _watchers = new();
    private long _lastLiveUpdateTick = 0;
    private readonly object _liveUpdateLock = new();

    private CancellationTokenSource? _scanCts;
    private Task? _currentScanTask;
    private readonly object _scanStartLock = new();
    public bool IsScanning { get; private set; }

    public string GetDirectoryPath(int dirIndex)
    {
        lock (_dirLock)
        {
            return (dirIndex >= 0 && dirIndex < _directories.Count) ? _directories[dirIndex] : "";
        }
    }

    public int GetOrAddDirectoryIndex(string dir)
    {
        lock (_dirLock)
        {
            if (_dirToIndex.TryGetValue(dir, out int index))
                return index;

            index = _directories.Count;
            _directories.Add(dir);
            _dirToIndex[dir] = index;
            return index;
        }
    }

    public int GetDirectoryIndex(string dir)
    {
        lock (_dirLock)
        {
            return _dirToIndex.TryGetValue(dir, out int index) ? index : -1;
        }
    }

    public void StartBackgroundIndexing(bool clearExisting = false)
    {
        lock (_scanStartLock)
        {
            _scanCts?.Cancel();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            var prevTask = _currentScanTask;

            _currentScanTask = Task.Run(async () =>
            {
                if (prevTask != null && !prevTask.IsCompleted)
                {
                    try
                    {
                        await prevTask;
                    }
                    catch { }
                }

                if (token.IsCancellationRequested) return;

                if (clearExisting)
                {
                    lock (_dirLock)
                    {
                        _directories.Clear();
                        _dirToIndex.Clear();
                    }
                    _files.Clear();
                    IndexCountChanged?.Invoke(0);
                }

                RunIndexPipeline(token);
            }, token);
        }
    }

    public void TriggerReindex()
    {
        StartBackgroundIndexing(clearExisting: true);
    }

    private void RunIndexPipeline(CancellationToken token)
    {
        IsScanning = true;

        // Start real-time watchers immediately so new files created during or after scan are caught!
        StartWatchers();

        try
        {
            // PHASE 1: Priority user folders (Instant readiness for user files)
            var priorityFolders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            };

            var skipRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scannedDirIndices = new HashSet<int>(32768);

            foreach (var folder in priorityFolders)
            {
                if (token.IsCancellationRequested) return;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    try
                    {
                        string normalized = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        skipRoots.Add(normalized);
                        IndexDirectoryRecursive(normalized, token, maxDepth: 12, skipRoots: null, scannedDirIndices: scannedDirIndices);
                    }
                    catch { }
                }
            }

            IndexCountChanged?.Invoke(_files.Count);

            // PHASE 2: FULL SYSTEM SCAN (All available drives on the PC: C:\, D:\, E:\, etc.)
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (token.IsCancellationRequested) return;
                if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                {
                    try
                    {
                        IndexDirectoryRecursive(drive.RootDirectory.FullName, token, maxDepth: 16, skipRoots: skipRoots, scannedDirIndices: scannedDirIndices);
                        IndexCountChanged?.Invoke(_files.Count);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Drive scan error {drive.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Indexing error: {ex.Message}");
        }
        finally
        {
            IsScanning = false;

            if (!token.IsCancellationRequested)
            {
                lock (_dirLock)
                {
                    _directories.TrimExcess();
                }

                // Force aggressive garbage collection and trim working set
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                try
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                }
                catch { }

                IndexCountChanged?.Invoke(_files.Count);
                IndexFinished?.Invoke();
            }
        }
    }

    private static readonly HashSet<string> BlacklistedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin",
        "System Volume Information",
        "WinSxS",
        "SystemApps",
        "WinMetadata",
        "Temp",
        "CrashDumps",
        "INetCache",
        ".git"
    };

    private void IndexDirectoryRecursive(string path, CancellationToken token, int maxDepth, HashSet<string>? skipRoots, HashSet<int> scannedDirIndices)
    {
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((path, 0));

        int batchCounter = 0;

        while (stack.Count > 0)
        {
            if (token.IsCancellationRequested) return;

            var (currentDir, depth) = stack.Pop();
            string dirName = Path.GetFileName(currentDir);

            if (BlacklistedFolderNames.Contains(dirName))
                continue;

            if (skipRoots != null && skipRoots.Count > 0)
            {
                try
                {
                    string normalized = Path.GetFullPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (skipRoots.Contains(normalized))
                        continue; // Already indexed in Phase 1!
                }
                catch { }
            }

            int dirIndex = GetOrAddDirectoryIndex(currentDir);
            if (!scannedDirIndices.Add(dirIndex))
                continue; // Already visited in this scan run!

            try
            {
                FastDirectoryScanner.ScanDirectory(currentDir, (name, isDir) =>
                {
                    if (isDir)
                    {
                        if (depth < maxDepth && !BlacklistedFolderNames.Contains(name))
                        {
                            string subDirPath = Path.Combine(currentDir, name);
                            stack.Push((subDirPath, depth + 1));
                            _files.Add(new FileRecord(dirIndex, name, true));
                        }
                    }
                    else
                    {
                        _files.Add(new FileRecord(dirIndex, name, false));
                        batchCounter++;
                        if (batchCounter % 15000 == 0)
                        {
                            IndexCountChanged?.Invoke(_files.Count);
                        }
                    }
                });
            }
            catch { }
        }
    }

    #region Live File System Watching (Real-Time Index Sync)

    public void StartWatchers()
    {
        StopWatchers();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                {
                    WatchDirectory(drive.RootDirectory.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting drive watchers: {ex.Message}");
        }
    }

    public void StopWatchers()
    {
        lock (_watchers)
        {
            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch { }
            }
            _watchers.Clear();
        }
    }

    private void WatchDirectory(string rootPath)
    {
        try
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 65536 // 64 KB buffer for high-burst changes
            };

            watcher.Created += OnFileSystemCreated;
            watcher.Deleted += OnFileSystemDeleted;
            watcher.Renamed += OnFileSystemRenamed;
            watcher.Error += (s, e) => { };

            watcher.EnableRaisingEvents = true;

            lock (_watchers)
            {
                _watchers.Add(watcher);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cannot watch root {rootPath}: {ex.Message}");
        }
    }

    private static bool ShouldIgnorePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return true;

        if (fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) ||
            fullPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return true;

        string[] parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (BlacklistedFolderNames.Contains(part))
                return true;
        }

        return false;
    }

    private void OnFileSystemCreated(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        try
        {
            string dir = Path.GetDirectoryName(e.FullPath) ?? "";
            string name = Path.GetFileName(e.FullPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

            bool isDir = Directory.Exists(e.FullPath);
            int dirIndex = GetOrAddDirectoryIndex(dir);

            _files.Add(new FileRecord(dirIndex, name, isDir));

            if (isDir)
            {
                ScanNewDirectory(e.FullPath, maxDepth: 4);
            }

            NotifyLiveUpdate();
        }
        catch { }
    }

    private void OnFileSystemDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnorePath(e.FullPath)) return;

        try
        {
            string dir = Path.GetDirectoryName(e.FullPath) ?? "";
            string name = Path.GetFileName(e.FullPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;

            int dirIndex = GetDirectoryIndex(dir);
            if (dirIndex >= 0)
            {
                _files.MarkDeleted(dirIndex, name);
            }

            int deletedDirIndex = GetDirectoryIndex(e.FullPath);
            if (deletedDirIndex >= 0)
            {
                _files.MarkDirectoryDeleted(deletedDirIndex);
            }

            NotifyLiveUpdate();
        }
        catch { }
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldIgnorePath(e.OldFullPath))
        {
            string oldDir = Path.GetDirectoryName(e.OldFullPath) ?? "";
            string oldName = Path.GetFileName(e.OldFullPath);
            int oldDirIndex = GetDirectoryIndex(oldDir);
            if (oldDirIndex >= 0 && !string.IsNullOrEmpty(oldName))
            {
                _files.MarkDeleted(oldDirIndex, oldName);
            }
        }

        if (!ShouldIgnorePath(e.FullPath))
        {
            string newDir = Path.GetDirectoryName(e.FullPath) ?? "";
            string newName = Path.GetFileName(e.FullPath);
            if (!string.IsNullOrEmpty(newDir) && !string.IsNullOrEmpty(newName))
            {
                bool isDir = Directory.Exists(e.FullPath);
                int newDirIndex = GetOrAddDirectoryIndex(newDir);
                _files.Add(new FileRecord(newDirIndex, newName, isDir));

                if (isDir)
                {
                    ScanNewDirectory(e.FullPath, maxDepth: 4);
                }
            }
        }

        NotifyLiveUpdate();
    }

    private void ScanNewDirectory(string root, int maxDepth)
    {
        try
        {
            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (currentDir, depth) = stack.Pop();
                if (depth > maxDepth || ShouldIgnorePath(currentDir)) continue;

                int dirIndex = GetOrAddDirectoryIndex(currentDir);

                FastDirectoryScanner.ScanDirectory(currentDir, (name, isDir) =>
                {
                    if (isDir)
                    {
                        if (depth < maxDepth && !BlacklistedFolderNames.Contains(name))
                        {
                            string sub = Path.Combine(currentDir, name);
                            stack.Push((sub, depth + 1));
                            _files.Add(new FileRecord(dirIndex, name, true));
                        }
                    }
                    else
                    {
                        _files.Add(new FileRecord(dirIndex, name, false));
                    }
                });
            }
        }
        catch { }
    }

    private void NotifyLiveUpdate()
    {
        lock (_liveUpdateLock)
        {
            long now = Environment.TickCount64;
            if (now - _lastLiveUpdateTick > 300)
            {
                _lastLiveUpdateTick = now;
                IndexCountChanged?.Invoke(_files.Count);
                LiveIndexUpdated?.Invoke();
            }
        }
    }

    #endregion

    public SearchResponse Search(string query, string selectedCategory = "All")
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResponse();
        }

        int chunkCount = _files.ChunkCount;
        if (chunkCount == 0)
        {
            return new SearchResponse();
        }

        var globalCandidates = new List<SearchCandidate>();
        var categoryCounts = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var combineLock = new object();

        // Parallel scan across chunks with thread-local buffers
        Parallel.For(
            0,
            chunkCount,
            () => (LocalList: new List<SearchCandidate>(256), LocalCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)),
            (chunkIdx, _, state) =>
            {
                var (chunk, length) = _files.GetChunk(chunkIdx);
                for (int i = 0; i < length; i++)
                {
                    ref readonly var record = ref chunk[i];
                    if (string.IsNullOrEmpty(record.Name)) continue; // Skip tombstones/deleted files
                    var match = FuzzySearchEngine.QuickMatch(record.Name, query);

                    if (match.IsMatch)
                    {
                        var (category, catIcon) = FileCategoryHelper.GetCategory(record.Name, record.IsDirectory);

                        state.LocalCounts[category] = state.LocalCounts.GetValueOrDefault(category) + 1;

                        if (selectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                            category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            state.LocalList.Add(new SearchCandidate(
                                record.DirIndex,
                                record.Name,
                                record.IsDirectory,
                                match.Score,
                                match.MatchType,
                                category,
                                catIcon
                            ));
                        }
                    }
                }
                return state;
            },
            state =>
            {
                lock (combineLock)
                {
                    globalCandidates.AddRange(state.LocalList);
                    foreach (var kvp in state.LocalCounts)
                    {
                        categoryCounts.AddOrUpdate(kvp.Key, kvp.Value, (_, existing) => existing + kvp.Value);
                    }
                }
            }
        );

        int totalMatches = 0;
        foreach (var val in categoryCounts.Values)
        {
            totalMatches += val;
        }

        var categories = new List<CategoryCount>
        {
            new CategoryCount
            {
                Name = "All",
                Icon = "✨",
                Count = totalMatches,
                IsSelected = selectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase)
            }
        };

        foreach (var kvp in categoryCounts.OrderByDescending(k => k.Value))
        {
            string iconStr = kvp.Key switch
            {
                "Folder" => "📁",
                "Image" => "🖼️",
                "Document" => "📄",
                "App" => "⚡",
                "Code" => "💻",
                "Video" => "🎬",
                "Audio" => "🎵",
                "Archive" => "📦",
                _ => "📄"
            };

            categories.Add(new CategoryCount
            {
                Name = kvp.Key,
                Icon = iconStr,
                Count = kvp.Value,
                IsSelected = selectedCategory.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)
            });
        }

        // Fast in-place sort: ZERO allocations
        globalCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        return new SearchResponse
        {
            Candidates = globalCandidates,
            Categories = categories
        };
    }
}
