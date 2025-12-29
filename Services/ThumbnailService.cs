using SimpleViewer.Models;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Services;

/// <summary>
/// サムネイル生成とキャッシュを担当するサービス。
/// - 内部で IImageSource の GetThumbnailAsync を呼び出してサムネイルを生成する。
/// - 同一インデックスに対する重複要求を避けるためキャッシュを保持する。
/// - 同時実行数を制限して IO/CPU の負荷を制御する（<see cref="_concurrencySemaphore"/>）。
/// </summary>
public class ThumbnailService
{
    // キャッシュ最大数。過剰なメモリ消費を防止するため上限を設ける。
    private const int MaxCacheCount = 200;

    // インデックス -> サムネイル BitmapSource のキャッシュ
    private readonly ConcurrentDictionary<int, BitmapSource> _thumbCache = new();

    // キャッシュ挿入順を記録するキュー（単純な LRU 代替）
    private readonly ConcurrentQueue<int> _cacheOrder = new();

    // 同時に実行するサムネイル生成の上限
    private readonly SemaphoreSlim _concurrencySemaphore = new(4);

    /// <summary>
    /// 指定のソース・インデックスからサムネイルを取得します。
    /// - 既にキャッシュに存在する場合は即時返却します。
    /// - 生成は並列上限（_concurrencySemaphore）で制御され、キャンセル可能です。
    /// - 生成成功時はキャッシュに追加し、古いエントリは FIFO で削除します（単純な容量管理）。
    /// </summary>
    /// <param name="source">サムネイルを取得する画像ソース</param>
    /// <param name="index">ページ/エントリのインデックス</param>
    /// <param name="width">サムネイル幅（ピクセル）</param>
    /// <param name="ct">キャンセル用トークン</param>
    /// <returns>生成された BitmapSource（失敗時は null）</returns>
    public async Task<BitmapSource?> GetThumbnailAsync(IImageSource source, int index, int width, CancellationToken ct)
    {
        // 既にキャッシュがあれば即時返却
        if (_thumbCache.TryGetValue(index, out var cached)) return cached;

        // 同時生成数を制御
        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            // 実際のサムネイル生成をソースに委譲
            var thumb = await source.GetThumbnailAsync(index, width);

            if (thumb != null)
            {
                // キャッシュ上限に達している場合は古いエントリを削除する
                if (_thumbCache.Count >= MaxCacheCount)
                {
                    if (_cacheOrder.TryDequeue(out int oldestKey))
                        _thumbCache.TryRemove(oldestKey, out _);
                }

                // キャッシュに追加に成功したら順序キューに登録
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

    /// <summary>
    /// キャッシュをクリアします。UI の表示変更やソース切替時に呼び出してください。
    /// </summary>
    public void ClearCache()
    {
        _thumbCache.Clear();
        // ConcurrentQueue に Clear が無いため、空になるまで Dequeue を試みる
        while (_cacheOrder.TryDequeue(out _)) { }
    }
}