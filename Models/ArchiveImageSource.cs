using SimpleViewer.Models;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

public class ArchiveImageSource : ImageSourceBase, IImageSource
{
    private ZipArchive? _archive;
    private readonly List<ZipArchiveEntry> _entries;
    private readonly object _zipLock = new();

    public ArchiveImageSource(string zipPath)
    {
        _archive = ZipFile.OpenRead(zipPath);
        _entries = _archive.Entries
            .Where(e => IsImageFile(e.FullName))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<int> GetPageCountAsync() => Task.FromResult(_entries.Count);

    private byte[]? GetEntryBytes(int index)
    {
        lock (_zipLock)
        {
            if (_archive == null || index < 0 || index >= _entries.Count) return null;
            using var entryStream = _entries[index].Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        var data = await Task.Run(() => GetEntryBytes(index));
        return data != null ? await Task.Run(() => SkiaImageLoader.LoadImage(data)) : null;
    }

    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        var data = await Task.Run(() => GetEntryBytes(index));
        return data != null ? await Task.Run(() => SkiaImageLoader.LoadThumbnail(data, width)) : null;
    }

    public void Dispose()
    {
        lock (_zipLock) { _archive?.Dispose(); _archive = null; }
    }
}