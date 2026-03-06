using SimpleViewer.Models.ImageSources;
using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Utils.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Services;

/// <summary>
/// サムネイル生成とキャッシュを担当するサービス。
/// - メモリキャッシュとディスクキャッシュを管理します
/// - 同時実行数を制限してリソースを制御します
/// - 同一キーに対する重複生成を抑制します
/// </summary>
public class ThumbnailService : IDisposable
{
    private readonly ConcurrentDictionary<string, BitmapSource> _memoryCache = new();
    private readonly ConcurrentQueue<string> _cacheOrder = new();
    
    // CPU コア数に基づいて同時実行数を設定（最小8、最大32）
    private readonly SemaphoreSlim _concurrencySemaphore;

    // 同時に生成中のキーを管理して重複生成を抑制する
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> _inFlightTasks = new();

    // メモリキャッシュ操作のためのロック
    private readonly object _memoryLock = new();

    // メモリキャッシュ上限（エントリ数）
    private const int MaxMemoryCacheEntries = 200;

    private readonly DiskCacheManager _diskCacheManager;
    private readonly BitmapFileHandler _fileHandler;
    private readonly bool _clearDiskOnClear;

    /// <summary>
    /// ThumbnailServiceを初期化します。
    /// </summary>
    /// <param name="imageDecoder">オプションのイメージデコーダ</param>
    public ThumbnailService(IImageDecoder? imageDecoder = null)
    {
        // 設定を読み込み
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var settingsManager = new SettingsManager(exeDir);
        var settings = settingsManager.LoadSettings();

        // 各コンポーネントを初期化
        var cacheDir = System.IO.Path.Combine(exeDir, "cache");
        _diskCacheManager = new DiskCacheManager(
            cacheDir,
            settings.ThumbnailCacheMaxMB,
            settings.ThumbnailUseSecureDelete);

        _fileHandler = new BitmapFileHandler(imageDecoder);
        _clearDiskOnClear = settings.ThumbnailClearDiskOnClear;

        // CPU コア数に基づいて同時実行数を設定（最小8、最大32）
        int maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 8, 32);
        _concurrencySemaphore = new SemaphoreSlim(maxDegreeOfParallelism);

        Debug.WriteLine($"[ThumbnailService] 初期化完了: 並列度={maxDegreeOfParallelism}, CacheDir={cacheDir}, MaxMB={settings.ThumbnailCacheMaxMB}");
    }

    /// <summary>
    /// 指定のソース・インデックスからサムネイルを取得します。
    /// 同一キーに対する重複生成は内部で集約されます。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(
        IImageSource source,
        int index,
        int width,
        CancellationToken ct)
    {
        try
        {
            var key = CacheKeyGenerator.MakeCacheKey(source, index);

            // メモリキャッシュを確認
            if (TryGetFromMemoryCache(key, width, out var cached))
            {
                return cached;
            }

            // すでに同一キーの処理が進行中であればそれを待つ
            var task = _inFlightTasks.GetOrAdd(key, _ => InternalGetThumbnailAsync(source, index, width, key, ct));

            try
            {
                var result = await task.ConfigureAwait(false);
                return result;
            }
            finally
            {
                // 完了したら in-flight から除去
                _inFlightTasks.TryRemove(key, out _);
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常な動作なのでログ出力しない
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThumbnailService] エラー: index={index}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 内部処理: ディスクキャッシュ確認→生成の流れを行います。
    /// </summary>
    private async Task<BitmapSource?> InternalGetThumbnailAsync(
        IImageSource source,
        int index,
        int width,
        string key,
        CancellationToken ct)
    {
        // ディスクキャッシュを確認
        var diskCached = await TryGetFromDiskCacheAsync(key, width, ct).ConfigureAwait(false);
        if (diskCached != null)
        {
            AddToMemoryCache(key, diskCached);
            return diskCached;
        }

        // サムネイルを生成
        var result = await GenerateThumbnailAsync(source, index, width, key, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// キャッシュをクリアします。
    /// </summary>
    public void ClearCache()
    {
        ClearMemoryCache();

        if (_clearDiskOnClear)
        {
            _diskCacheManager.ClearAllCache();
        }
    }

    /// <summary>
    /// キャッシュを非同期でクリアします。
    /// </summary>
    public async Task ClearCacheAsync(CancellationToken ct = default)
    {
        ClearMemoryCache();

        if (_clearDiskOnClear)
        {
            await _diskCacheManager.ClearAllCacheAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// すべてのキャッシュ（メモリとディスク）を強制的にクリアします。
    /// EXIF設定変更時など、キャッシュを完全にクリアする必要がある場合に使用します。
    /// </summary>
    public void ClearAllCache()
    {
        ClearMemoryCache();
        _diskCacheManager.ClearAllCache();
    }

    /// <summary>
    /// すべてのキャッシュ（メモリとディスク）を非同期で強制的にクリアします。
    /// </summary>
    public async Task ClearAllCacheAsync(CancellationToken ct = default)
    {
        ClearMemoryCache();
        await _diskCacheManager.ClearAllCacheAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// メモリキャッシュから取得を試みます。
    /// </summary>
    private bool TryGetFromMemoryCache(string key, int width, out BitmapSource? cached)
    {
        if (_memoryCache.TryGetValue(key, out cached))
        {
            if (cached != null && cached.PixelWidth >= width)
            {
                return true;
            }
        }
        cached = null;
        return false;
    }

    /// <summary>
    /// ディスクキャッシュから取得を試みます。
    /// </summary>
    private async Task<BitmapSource?> TryGetFromDiskCacheAsync(
        string key,
        int width,
        CancellationToken ct)
    {
        try
        {
            var filePath = _diskCacheManager.GetFilePath(key);
            if (!_diskCacheManager.FileExists(filePath))
            {
                return null;
            }

            var bmp = await _fileHandler.LoadBitmapFromFileAsync(filePath, ct).ConfigureAwait(false);
            if (bmp == null)
            {
                return null;
            }

            if (bmp.CanFreeze) bmp.Freeze();

            // 要求幅以上ならそのまま返す
            if (bmp.PixelWidth >= width)
            {
                return bmp;
            }
        }
        catch { /* 読み込み失敗はキャッシュミス扱い */ }

        return null;
    }

    /// <summary>
    /// サムネイルを生成します。
    /// </summary>
    private async Task<BitmapSource?> GenerateThumbnailAsync(
        IImageSource source,
        int index,
        int width,
        string key,
        CancellationToken ct)
    {
        // 同時生成数を制御
        await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            // 実際のサムネイル生成をソースに委譲
            var thumb = await source.GetThumbnailAsync(index, width).ConfigureAwait(false);
            if (thumb == null)
            {
                return null;
            }

            if (thumb.CanFreeze) thumb.Freeze();

            // メモリキャッシュに登録
            AddToMemoryCache(key, thumb);

            // ディスクへ書き込む（非同期で実行）
            _ = Task.Run(() =>
            {
                try
                {
                    var filePath = _diskCacheManager.GetFilePath(key);
                    _fileHandler.SaveBitmapToFile(thumb, filePath);
                    _diskCacheManager.EnforceDiskCapacity();
                }
                catch { /* 非致命的なので無視 */ }
            });

            return thumb;
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    /// <summary>
    /// メモリキャッシュに追加します。
    /// 上限を超えた場合は古いエントリから削除します。
    /// </summary>
    private void AddToMemoryCache(string key, BitmapSource bitmap)
    {
        lock (_memoryLock)
        {
            _memoryCache[key] = bitmap;
            _cacheOrder.Enqueue(key);

            // 上限を超えたら古いものから削除
            while (_memoryCache.Count > MaxMemoryCacheEntries && _cacheOrder.TryDequeue(out var oldKey))
            {
                _memoryCache.TryRemove(oldKey, out _);
            }
        }
    }

    /// <summary>
    /// メモリキャッシュをクリアします。
    /// </summary>
    private void ClearMemoryCache()
    {
        lock (_memoryLock)
        {
            _memoryCache.Clear();
            while (_cacheOrder.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// リソースを破棄します。
    /// SemaphoreSlim等の破棄を行います。
    /// </summary>
    public void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}
