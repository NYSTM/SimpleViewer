using SimpleViewer.Models;
using SimpleViewer.Services;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Presenters;

/// <summary>
/// アプリケーションの主要な Presenter クラス。
/// View (IView) と Model (IImageSource 等) の橋渡しを行い、ページ遷移、キャッシュ管理、プリフェッチ等を担当します。
/// UI 更新は IView 経由で行われるため、Presenter は非 UI スレッドで画像読み込みを行い、
/// 最終的な UI 更新は View に委譲します。
/// </summary>
public class SimpleViewerPresenter(IView view)
{
    // 現在開いている画像ソース
    private IImageSource? _currentSource;

    // 現在のページインデックス（0 始まり）
    private int _currentPageIndex = 0;
    private int _totalPageCount = 0;

    // 表示モード（単一 or 見開き）
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Single;

    // メイン画像のキャッシュ（ページインデックス -> BitmapSource）
    private readonly ConcurrentDictionary<int, BitmapSource> _imageCache = new();
    private const int MaxCachePages = 12; // キャッシュ上限数
    private const long MemoryThresholdMB = 500; // メモリ閾値（MB）: ここを下回るとキャッシュ削除を試みる

    // サムネイル取得を担当するサービス（別スレッドでの生成やキャッシュを管理）
    private readonly ThumbnailService _thumbnailService = new();

    // 非同期制御: ナビゲーション・プリフェッチのキャンセル用およびデコード排他制御
    private CancellationTokenSource? _navigationCts;
    private CancellationTokenSource? _prefetchCts;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <summary>
    /// 指定パスのソースを開き、ページ数を取得して最初のページを表示します。
    /// 既に開かれているソースがあれば CloseSource() で閉じてから開き直します。
    /// </summary>
    public async Task OpenSourceAsync(string path)
    {
        CloseSource();
        try
        {
            _currentSource = await ImageSourceFactory.CreateSourceAsync(path);
            _totalPageCount = await _currentSource.GetPageCountAsync();

            if (_totalPageCount > 0) await JumpToPageAsync(0);
        }
        catch (Exception ex)
        {
            // View 側でエラーを表示する
            view.ShowError($"ソースを開けません: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のソースを閉じ、関連キャッシュやタスクをクリアします。
    /// </summary>
    public void CloseSource()
    {
        _navigationCts?.Cancel();
        _prefetchCts?.Cancel();
        _imageCache.Clear();
        _thumbnailService.ClearCache();
        _currentSource?.Dispose();
        _currentSource = null;
        _currentPageIndex = 0;
        _totalPageCount = 0;
        GC.Collect(); // 明示的な GC 呼び出し（頻度には注意）
    }

    /// <summary>
    /// 指定インデックスへジャンプして画像を取得・表示します。
    /// 表示モードに応じて左/右画像を決定します。
    /// </summary>
    public async Task JumpToPageAsync(int index)
    {
        if (_currentSource == null || _totalPageCount == 0) return;

        int targetIndex = Math.Clamp(index, 0, _totalPageCount - 1);
        bool isForward = targetIndex >= _currentPageIndex;
        _currentPageIndex = targetIndex;

        _navigationCts?.Cancel();
        _navigationCts = new CancellationTokenSource();
        var token = _navigationCts.Token;

        try
        {
            BitmapSource? left = null;
            BitmapSource? right = null;

            if (CurrentDisplayMode == DisplayMode.Single)
            {
                // 単一表示では現在ページのみ取得
                left = await GetOrLoadImageAsync(_currentPageIndex, token);
            }
            else
            {
                // 見開き: 表示モードにより左右の割当が変わる
                int firstIdx = _currentPageIndex;
                int secondIdx = _currentPageIndex + 1;

                if (CurrentDisplayMode == DisplayMode.SpreadRTL)
                {
                    left = (secondIdx < _totalPageCount) ? await GetOrLoadImageAsync(secondIdx, token) : null;
                    right = await GetOrLoadImageAsync(firstIdx, token);
                }
                else
                {
                    left = await GetOrLoadImageAsync(firstIdx, token);
                    right = (secondIdx < _totalPageCount) ? await GetOrLoadImageAsync(secondIdx, token) : null;
                }
            }

            if (!token.IsCancellationRequested)
            {
                // UI 更新は View 側に委譲
                view.SetImages(left, right);
                view.UpdateProgress(_currentPageIndex + 1, _totalPageCount);
                StartSmartPrefetching(_currentPageIndex, isForward);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// キャッシュを参照し、存在すれば返す。なければソースからロードしてキャッシュに格納する。
    /// 同時アクセスを SemaphoreSlim で制御する。
    /// </summary>
    private async Task<BitmapSource?> GetOrLoadImageAsync(int index, CancellationToken token)
    {
        if (index < 0 || index >= _totalPageCount || _currentSource == null) return null;
        if (_imageCache.TryGetValue(index, out var cached)) return cached;

        await _loadLock.WaitAsync(token);
        try
        {
            // ロック取得後に再確認
            if (_imageCache.TryGetValue(index, out var cachedAgain)) return cachedAgain;

            // メモリ状況に応じて古いキャッシュを削除
            CheckMemoryAndPurge();

            var bitmap = await _currentSource.GetPageImageAsync(index);
            if (bitmap != null)
            {
                if (bitmap.CanFreeze) bitmap.Freeze(); // 再度 Freeze（安全側）
                _imageCache[index] = bitmap;
                return bitmap;
            }
        }
        finally
        {
            _loadLock.Release();
        }
        return null;
    }

    /// <summary>
    /// メモリ状況をチェックし、必要ならキャッシュの一部を削除してメモリを確保する。
    /// GC.GetGCMemoryInfo を利用しておおよその使用可能メモリ量を参照する。
    /// </summary>
    private void CheckMemoryAndPurge()
    {
        // PerformanceCounter を毎回生成する設計はコストとリソースリークの原因となるため
        // GC の情報を用いて代替判定を行う。
        long availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long thresholdBytes = MemoryThresholdMB * 1024 * 1024;

        if (availableBytes < thresholdBytes || _imageCache.Count > MaxCachePages)
        {
            // 現在ページから遠い順に半分を目安に削除
            var keysToRemove = _imageCache.Keys
                .OrderByDescending(k => Math.Abs(k - _currentPageIndex))
                .Take(Math.Max(1, _imageCache.Count / 2))
                .ToList();

            foreach (var k in keysToRemove) _imageCache.TryRemove(k, out _);

            // 明示的に GC を促す。ただし頻繁な呼び出しは避けること。
            GC.Collect();
        }
    }

    /// <summary>
    /// スマートなプリフェッチを行う。進行方向を優先しつつ近傍のページを順次読み込む。
    /// バックグラウンドで実行し、キャンセル可能。
    /// </summary>
    private void StartSmartPrefetching(int centerIndex, bool isForward)
    {
        _prefetchCts?.Cancel();
        _prefetchCts = new CancellationTokenSource();
        var token = _prefetchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // 優先順位リストの作成
                List<int> queue = isForward
                    ? new List<int> { centerIndex + 2, centerIndex + 3, centerIndex - 1, centerIndex + 4 }
                    : new List<int> { centerIndex - 1, centerIndex - 2, centerIndex + 2, centerIndex - 3 };

                foreach (int idx in queue)
                {
                    if (token.IsCancellationRequested) return;
                    if (idx >= 0 && idx < _totalPageCount && !_imageCache.ContainsKey(idx))
                    {
                        await GetOrLoadImageAsync(idx, token);
                        await Task.Delay(50, token); // UI への負荷を下げるため短い遅延を挟む
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    // ページ移動ユーティリティ
    public async Task NextPageAsync() => await JumpToPageAsync(_currentPageIndex + (CurrentDisplayMode == DisplayMode.Single ? 1 : 2));
    public async Task PreviousPageAsync() => await JumpToPageAsync(_currentPageIndex - (CurrentDisplayMode == DisplayMode.Single ? 1 : 2));

    /// <summary>
    /// 表示モードを切り替え、キャッシュをクリアして現在ページを再表示する。
    /// </summary>
    public async Task ToggleDisplayModeAsync()
    {
        CurrentDisplayMode = CurrentDisplayMode switch
        {
            DisplayMode.Single => DisplayMode.SpreadRTL,
            DisplayMode.SpreadRTL => DisplayMode.SpreadLTR,
            _ => DisplayMode.Single
        };
        _imageCache.Clear();
        _thumbnailService.ClearCache();
        await JumpToPageAsync(_currentPageIndex);
    }

    public void SetDisplayMode(DisplayMode mode)
    {
        CurrentDisplayMode = mode;
    }

    /// <summary>
    /// 指定インデックスのサムネイルを取得します。ThumbnailService を利用します。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width, CancellationToken? token = null)
    {
        if (_currentSource == null) return null;
        return await _thumbnailService.GetThumbnailAsync(_currentSource, index, width, token ?? CancellationToken.None);
    }

    // UI コンポーネントに総ページ数を提供するヘルパー
    public int GetTotalPageCount() => _totalPageCount;

    /// <summary>
    /// ツリー表示用のファイル一覧を取得して返します。
    /// ソースがない場合は空のリストを返します。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetFileListAsync()
    {
        if (_currentSource == null) return Array.Empty<string>();
        try
        {
            return await _currentSource.GetFileListAsync();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}