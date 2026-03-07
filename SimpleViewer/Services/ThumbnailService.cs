using SimpleViewer.Models.ImageSources;
using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Utils.Configuration;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Services;

/// <summary>
/// サムネイル生成とキャッシュを担当するサービス。
/// メモリキャッシュとディスクキャッシュを使用してパフォーマンスを最適化します。
/// </summary>
/// <remarks>
/// <para>主な機能:</para>
/// <list type="bullet">
/// <item><description>メモリキャッシュとディスクキャッシュの二段階キャッシュ</description></item>
/// <item><description>同時実行数の制限によるリソース制御</description></item>
/// <item><description>同一キーに対する重複生成の抑制</description></item>
/// <item><description>キャンセルトークンによる進行中タスクの管理</description></item>
/// </list>
/// </remarks>
public class ThumbnailService : IDisposable
{
    private readonly ConcurrentDictionary<string, BitmapSource> _memoryCache = new();
    private readonly ConcurrentQueue<string> _cacheOrder = new();
    
    // CPU コア数に基づいて同時実行数を設定（最小8、最大32）
    private readonly SemaphoreSlim _concurrencySemaphore;

    // 同時に生成中のキーを管理して重複生成を抑制する
    private readonly ConcurrentDictionary<string, Task<BitmapSource?>> _inFlightTasks = new();
    
    // サムネイル生成をキャンセルするためのトークンソース
    private CancellationTokenSource _generationCts = new();
    
    // サムネイル生成の一時停止状態を管理
    private volatile bool _isPaused = false;
    private readonly SemaphoreSlim _pauseSemaphore = new(0);

    // メモリキャッシュ操作のためのロック
    private readonly object _memoryLock = new();

    // メモリキャッシュ上限（エントリ数）
    private const int MaxMemoryCacheEntries = 200;

    private readonly DiskCacheManager _diskCacheManager;
    private readonly BitmapFileHandler _fileHandler;
    private readonly bool _clearDiskOnClear;

    /// <summary>
    /// ThumbnailService を初期化します。
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

        // CPU コア数に基づいて同時実行数を設定
        // UI 応答性を維持するため、控えめな並列度に設定（最小4、最大16）
        int maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);
        _concurrencySemaphore = new SemaphoreSlim(maxDegreeOfParallelism);
    }

    /// <summary>
    /// 指定のソース・インデックスからサムネイルを取得します。
    /// 同一キーに対する重複生成は内部で集約されます。
    /// </summary>
    /// <param name="source">画像ソース</param>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">要求幅（ピクセル）</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>サムネイル画像、失敗時は null</returns>
    public async Task<BitmapSource?> GetThumbnailAsync(
        IImageSource source,
        int index,
        int width,
        CancellationToken ct)
    {
        try
        {
            // ディスクキャッシュに現在のソースを設定
            _diskCacheManager.SetCurrentSource(source.SourceIdentifier);
            
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
            return null;
        }
        catch
        {
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
        // 外部キャンセルトークンと内部キャンセルトークンを組み合わせる
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _generationCts.Token);
        var linkedToken = linkedCts.Token;
        
        // ディスクキャッシュを確認
        var diskCached = await TryGetFromDiskCacheAsync(key, width, linkedToken).ConfigureAwait(false);
        if (diskCached != null)
        {
            AddToMemoryCache(key, diskCached);
            return diskCached;
        }

        // サムネイルを生成
        var result = await GenerateThumbnailAsync(source, index, width, key, linkedToken).ConfigureAwait(false);
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
        // 進行中のサムネイル生成をキャンセル
        _generationCts?.Cancel();
        _generationCts = new CancellationTokenSource();
        
        // 進行中のタスクをクリア
        _inFlightTasks.Clear();
        
        ClearMemoryCache();
        _diskCacheManager.ClearAllCache();
    }

    /// <summary>
    /// すべてのキャッシュ（メモリとディスク）を非同期で強制的にクリアします。
    /// </summary>
    public async Task ClearAllCacheAsync(CancellationToken ct = default)
    {
        // 進行中のサムネイル生成をキャンセル
        _generationCts?.Cancel();
        _generationCts = new CancellationTokenSource();
        
        // 進行中のタスクをクリア
        _inFlightTasks.Clear();
        
        ClearMemoryCache();
        await _diskCacheManager.ClearAllCacheAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// サムネイル生成を一時停止します。
    /// ページ移動中など、サムネイル生成のリソースを一時的に解放したい場合に使用します。
    /// 進行中のタスクは中断されず、新しいタスクの開始のみが停止されます。
    /// </summary>
    public void PauseThumbnailGeneration()
    {
        if (!_isPaused)
        {
            _isPaused = true;
            System.Diagnostics.Debug.WriteLine("[ThumbnailService] サムネイル生成を一時停止");
        }
    }

    /// <summary>
    /// 一時停止していたサムネイル生成を再開します。
    /// </summary>
    public void ResumeThumbnailGeneration()
    {
        if (_isPaused)
        {
            _isPaused = false;
            // 待機中のタスクを再開
            try
            {
                _pauseSemaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // すでに解放済みの場合は無視
            }
            System.Diagnostics.Debug.WriteLine("[ThumbnailService] サムネイル生成を再開");
        }
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
    /// UI 応答性を維持するため、バックグラウンド優先度で実行します。
    /// </summary>
    private async Task<BitmapSource?> GenerateThumbnailAsync(
        IImageSource source,
        int index,
        int width,
        string key,
        CancellationToken ct)
    {
        // 一時停止中の場合は待機
        if (_isPaused)
        {
            System.Diagnostics.Debug.WriteLine($"[GenerateThumbnailAsync] 一時停止中のため待機: index={index}");
            await _pauseSemaphore.WaitAsync(ct).ConfigureAwait(false);
        }

        // 同時生成数を制御
        await _concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();

            // UI 応答性を維持するため、短い遅延を入れる
            await Task.Delay(5, ct).ConfigureAwait(false);

            // 実際のサムネイル生成をソースに委譲
            var thumb = await source.GetThumbnailAsync(index, width).ConfigureAwait(false);
            if (thumb == null)
            {
                return null;
            }

            if (thumb.CanFreeze) thumb.Freeze();

            // キャッシュキーが現在のソースと一致する場合のみメモリキャッシュに登録
            // （フォルダ切り替え後に古いタスクが完了した場合を防ぐ）
            var currentKey = CacheKeyGenerator.MakeCacheKey(source, index);
            if (currentKey == key)
            {
                AddToMemoryCache(key, thumb);
            }

            // ディスクへ書き込む（非同期で実行）
            // ディスクキャッシュは保持しても問題ない（次回読み込み時に高速化）
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
        _generationCts?.Cancel();
        _generationCts?.Dispose();
        _concurrencySemaphore?.Dispose();
        _pauseSemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}
