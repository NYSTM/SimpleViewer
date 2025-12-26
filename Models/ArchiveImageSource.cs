using SimpleViewer.Models;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SimpleViewer.Models;

public class ArchiveImageSource(string zipPath) : ImageSourceBase, IImageSource
{
    private readonly ZipArchive _archive = ZipFile.OpenRead(zipPath);
    private readonly List<ZipArchiveEntry> _entries = ZipFile.OpenRead(zipPath).Entries
            .Where(e => ImageSourceBase.IsStaticImageFile(e.FullName))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private readonly object _zipLock = new();

    public Task<int> GetPageCountAsync() => Task.FromResult(_entries.Count);

    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                byte[] data;
                lock (_zipLock)
                {
                    using var entryStream = _entries[index].Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    data = ms.ToArray();
                }

                var bitmap = SkiaImageLoader.LoadImage(data);
                bitmap?.Freeze(); // キャッシュ・スレッド間共有の最適化
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Archive Load Error: {ex.Message}");
                return null;
            }
        });
    }

    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _entries.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                byte[] data;
                lock (_zipLock)
                {
                    using var entryStream = _entries[index].Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    data = ms.ToArray();
                }
                var thumb = SkiaImageLoader.LoadThumbnail(data, width);
                thumb?.Freeze();
                return thumb;
            }
            catch { return null; }
        });
    }

    public override void Dispose()
    {
        _archive.Dispose();
        _entries.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}