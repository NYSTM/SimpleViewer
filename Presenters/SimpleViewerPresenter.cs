using SimpleViewer.Models;
using SimpleViewer.Services;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SimpleViewer.Presenters;

public class SimpleViewerPresenter(IView view)
{
    private IImageSource? _currentSource;
    private int _currentPageIndex = 0;
    private int _totalPageCount = 0;
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Single;

    // キャッシュ管理
    private readonly ConcurrentDictionary<int, BitmapSource> _imageCache = new();
    private const int MaxCachePages = 12; // 最大保持数
    private const long MemoryThresholdMB = 500; // 空きメモリ閾値 (MB)

    // サムネイルサービス
    private readonly ThumbnailService _thumbnailService = new();

    // 非同期制御
    private CancellationTokenSource? _navigationCts;
    private CancellationTokenSource? _prefetchCts;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

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
            view.ShowError($"ソースを開けません: {ex.Message}");
        }
    }

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
        GC.Collect(); // 明示的な回収
    }

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
                left = await GetOrLoadImageAsync(_currentPageIndex, token);
            }
            else
            {
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
                view.SetImages(left, right);
                view.UpdateProgress(_currentPageIndex + 1, _totalPageCount);
                StartSmartPrefetching(_currentPageIndex, isForward);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task<BitmapSource?> GetOrLoadImageAsync(int index, CancellationToken token)
    {
        if (index < 0 || index >= _totalPageCount || _currentSource == null) return null;
        if (_imageCache.TryGetValue(index, out var cached)) return cached;

        await _loadLock.WaitAsync(token);
        try
        {
            if (_imageCache.TryGetValue(index, out var cachedAgain)) return cachedAgain;

            // メモリ監視: 空きが少ない場合はキャッシュを半分捨てる
            CheckMemoryAndPurge();

            var bitmap = await _currentSource.GetPageImageAsync(index);
            if (bitmap != null)
            {
                if (bitmap.CanFreeze) bitmap.Freeze(); // スレッド間共有とメモリ最適化
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

    private void CheckMemoryAndPurge()
    {
        var pc = new PerformanceCounter("Memory", "Available MBytes");
        if (pc.NextValue() < MemoryThresholdMB || _imageCache.Count > MaxCachePages)
        {
            // 現在地から遠い順に削除
            var keysToRemove = _imageCache.Keys
                .OrderByDescending(k => Math.Abs(k - _currentPageIndex))
                .Take(_imageCache.Count / 2);

            foreach (var k in keysToRemove) _imageCache.TryRemove(k, out _);
        }
    }

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
                // 進行方向の2ページを最優先、次に逆方向、その次にさらに先を読み込む
                List<int> queue = isForward
                    ? [centerIndex + 2, centerIndex + 3, centerIndex - 1, centerIndex + 4]
                    : [centerIndex - 1, centerIndex - 2, centerIndex + 2, centerIndex - 3];

                foreach (int idx in queue)
                {
                    if (token.IsCancellationRequested) return;
                    if (idx >= 0 && idx < _totalPageCount && !_imageCache.ContainsKey(idx))
                    {
                        await GetOrLoadImageAsync(idx, token);
                        await Task.Delay(50, token); // UIへの負荷軽減
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public async Task NextPageAsync() => await JumpToPageAsync(_currentPageIndex + (CurrentDisplayMode == DisplayMode.Single ? 1 : 2));
    public async Task PreviousPageAsync() => await JumpToPageAsync(_currentPageIndex - (CurrentDisplayMode == DisplayMode.Single ? 1 : 2));

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

    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width, CancellationToken? token = null)
    {
        if (_currentSource == null) return null;
        return await _thumbnailService.GetThumbnailAsync(_currentSource, index, width, token ?? CancellationToken.None);
    }
}