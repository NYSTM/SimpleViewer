using SimpleViewer.Models;
using System.IO;
using System.IO.Compression;

public class ArchiveImageSource : ImageSourceBase, IImageSource
{
    private ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _entries;
    private readonly object _zipLock = new(); // Zipアクセス排他用

    public ArchiveImageSource(string zipPath)
    {
        _archive = ZipFile.OpenRead(zipPath);
        _entries = _archive.Entries
            .Where(e => IsImageFile(e.FullName))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<int> GetPageCountAsync() => Task.FromResult(_entries.Count);

    public Stream? GetPageStream(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;

        lock (_zipLock) // 複数スレッドからの同時展開を防止
        {
            if (_archive == null) return null;
            var ms = new MemoryStream();
            using (var entryStream = _entries[index].Open())
            {
                entryStream.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
        }
    }

    public void Dispose()
    {
        lock (_zipLock)
        {
            _archive?.Dispose();
            _archive = null!;
        }
    }
}