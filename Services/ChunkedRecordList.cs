using FinderApp.Models;

namespace FinderApp.Services;

/// <summary>
/// Chunked array storage for FileRecords.
/// Keeps individual chunks at 64 KB (4096 items * 16 bytes), avoiding Large Object Heap (LOH) allocations.
/// Resizing never reallocates or copies old data, and parallel reads are completely thread-safe.
/// </summary>
public class ChunkedRecordList
{
    public const int ChunkShift = 12; // 2^12 = 4096
    public const int ChunkSize = 1 << ChunkShift; // 4096
    public const int ChunkMask = ChunkSize - 1;

    private readonly List<FileRecord[]> _chunks = new(128);
    private int _count = 0;
    private FileRecord[]? _currentChunk;
    private int _currentChunkIndex = 0;
    private readonly object _addLock = new();

    public int Count
    {
        get
        {
            lock (_addLock) return _count;
        }
    }

    public void Clear()
    {
        lock (_addLock)
        {
            _chunks.Clear();
            _currentChunk = null;
            _currentChunkIndex = 0;
            _count = 0;
        }
    }

    public bool MarkDeleted(int dirIndex, string name)
    {
        lock (_addLock)
        {
            int chunkCount = _chunks.Count;
            // Scan backwards: recently modified or added files are in later chunks
            for (int c = chunkCount - 1; c >= 0; c--)
            {
                var chunk = _chunks[c];
                int len = (c == chunkCount - 1) ? _currentChunkIndex : ChunkSize;
                for (int i = len - 1; i >= 0; i--)
                {
                    ref var record = ref chunk[i];
                    if (record.DirIndex == dirIndex && string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        chunk[i] = new FileRecord(dirIndex, null!, record.IsDirectory, record.ModifiedTime);
                        if (_count > 0) _count--;
                        return true;
                    }
                }
            }
            return false;
        }
    }

    public void MarkDirectoryDeleted(int dirIndex)
    {
        lock (_addLock)
        {
            int chunkCount = _chunks.Count;
            for (int c = 0; c < chunkCount; c++)
            {
                var chunk = _chunks[c];
                int len = (c == chunkCount - 1) ? _currentChunkIndex : ChunkSize;
                for (int i = 0; i < len; i++)
                {
                    ref var record = ref chunk[i];
                    if (record.DirIndex == dirIndex && !string.IsNullOrEmpty(record.Name))
                    {
                        chunk[i] = new FileRecord(dirIndex, null!, record.IsDirectory, record.ModifiedTime);
                        if (_count > 0) _count--;
                    }
                }
            }
        }
    }

    public void Add(in FileRecord record)
    {
        lock (_addLock)
        {
            if (_currentChunk == null || _currentChunkIndex >= ChunkSize)
            {
                _currentChunk = new FileRecord[ChunkSize];
                _chunks.Add(_currentChunk);
                _currentChunkIndex = 0;
            }

            _currentChunk[_currentChunkIndex++] = record;
            _count++;
        }
    }

    public int ChunkCount
    {
        get
        {
            lock (_addLock) return _chunks.Count;
        }
    }

    public (FileRecord[] Array, int Length) GetChunk(int chunkIndex)
    {
        lock (_addLock)
        {
            if (chunkIndex < 0 || chunkIndex >= _chunks.Count)
                return (Array.Empty<FileRecord>(), 0);

            var array = _chunks[chunkIndex];
            int len = (chunkIndex == _chunks.Count - 1) ? _currentChunkIndex : ChunkSize;
            return (array, len);
        }
    }

    public ref readonly FileRecord GetRecord(int globalIndex)
    {
        int chunkIdx = globalIndex >> ChunkShift;
        int itemIdx = globalIndex & ChunkMask;
        return ref _chunks[chunkIdx][itemIdx];
    }
}
