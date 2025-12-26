using SimpleViewer.Models;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Services;

/// <summary>
/// カタログ表示用のサムネイル生成とキャッシュを専門に管理するサービス。
/// </summary>
public class ThumbnailService
{
    private readonly ConcurrentDictionary<int, BitmapSource> _thumbCache = new();
    private readonly SemaphoreSlim _concurrencySemaphore = new(4); // 同時実行数をCPUコア数程度に制限

    /// <summary>
    /// 指定されたインデックスのサムネイルを取得します。
    /// キャッシュにあれば即座に返し、なければ生成キューに入ります。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(IImageSource source, int index, int width, CancellationToken ct)
    {
        // 1. キャッシュチェック
        if (_thumbCache.TryGetValue(index, out var cached)) return cached;

        // 2. 同時実行数の制御
        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            // キャンセルチェック
            ct.ThrowIfCancellationRequested();

            // 3. 生成（ImageSource経由でSkiaImageLoaderを呼び出す）
            var thumb = await source.GetThumbnailAsync(index, width);

            if (thumb != null)
            {
                thumb.Freeze(); // スレッド間共有のために必須
                _thumbCache.TryAdd(index, thumb);
            }

            return thumb;
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public void ClearCache() => _thumbCache.Clear();
}