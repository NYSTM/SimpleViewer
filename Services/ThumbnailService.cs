using SimpleViewer.Models;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Services;

public class ThumbnailService
{
    // メモリ管理のため、保持するキャッシュ数を制限
    private const int MaxCacheCount = 200;
    private readonly ConcurrentDictionary<int, BitmapSource> _thumbCache = new();
    // キャッシュ順序を記録するためのキュー
    private readonly ConcurrentQueue<int> _cacheOrder = new();
    private readonly SemaphoreSlim _concurrencySemaphore = new(4);

    public async Task<BitmapSource?> GetThumbnailAsync(IImageSource source, int index, int width, CancellationToken ct)
    {
        if (_thumbCache.TryGetValue(index, out var cached)) return cached;

        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var thumb = await source.GetThumbnailAsync(index, width);

            if (thumb != null)
            {
                // キャッシュ溢れ防止ロジック
                if (_thumbCache.Count >= MaxCacheCount)
                {
                    if (_cacheOrder.TryDequeue(out int oldestKey))
                        _thumbCache.TryRemove(oldestKey, out _);
                }

                if (_thumbCache.TryAdd(index, thumb))
                {
                    _cacheOrder.Enqueue(index);
                }
            }
            return thumb;
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public void ClearCache()
    {
        _thumbCache.Clear();
        _cacheOrder.Clear();
    }
}