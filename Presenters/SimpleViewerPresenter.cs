using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using SimpleViewer.Models;
using System.Threading;

namespace SimpleViewer.Presenters;

public class SimpleViewerPresenter
{
    private readonly IView _view;
    private IImageSource? _currentSource;
    private int _currentPageIndex = 0;
    private int _totalPageCount = 0;
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Single;

    private readonly ConcurrentDictionary<int, BitmapSource> _imageCache = new();
    private const int CacheRange = 4;

    // 排他制御とキャンセル用
    private CancellationTokenSource? _navigationCts;
    private CancellationTokenSource? _prefetchCts;
    private readonly SemaphoreSlim _loadLock = new(1, 1); // 同時に1つのデコードのみ許可

    public SimpleViewerPresenter(IView view) => _view = view;

    public async Task OpenSourceAsync(string path)
    {
        CloseSource(); // 既存の処理を完全に止める

        try
        {
            _currentSource = await ImageSourceFactory.CreateSourceAsync(path);
            _totalPageCount = await _currentSource.GetPageCountAsync();
            if (_totalPageCount > 0) await JumpToPageAsync(0);
        }
        catch (Exception ex) { _view.ShowError($"ソースを開けません: {ex.Message}"); }
    }

    public void CloseSource()
    {
        // すべての非同期処理に停止命令を出す
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;

        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = null;

        _imageCache.Clear();
        _currentSource?.Dispose();
        _currentSource = null;
    }

    /// <summary>
    /// サムネイル取得メソッド（ビルドエラー解消用）
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (_currentSource == null) return null;

        return await Task.Run(() =>
        {
            try
            {
                // サムネイル生成はメインの表示と競合しないよう、
                // lockを使わずに、あるいは必要最小限の期間のみlockして処理
                using var stream = _currentSource.GetPageStream(index);
                if (stream == null) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.DecodePixelWidth = width; // 高速化の鍵：必要なサイズでデコード
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        });
    }

    public async Task JumpToPageAsync(int index)
    {
        if (_currentSource == null) return;

        // 進行中の古いページ移動タスクをキャンセル
        _navigationCts?.Cancel();
        _navigationCts = new CancellationTokenSource();
        var token = _navigationCts.Token;

        // 見開き時の偶数丸め込み
        if (CurrentDisplayMode != DisplayMode.Single && index > 0 && index < _totalPageCount - 1)
        {
            if (index % 2 != 0) index--;
        }
        _currentPageIndex = Math.Clamp(index, 0, _totalPageCount - 1);

        try
        {
            // 画像取得
            var images = await GetLayoutImagesAsync(_currentPageIndex, token);

            if (token.IsCancellationRequested) return;

            _view.SetImages(images.Left, images.Right);
            _view.UpdateProgress(_currentPageIndex + 1, _totalPageCount);

            // 先読み開始
            StartPrefetch(_currentPageIndex);
        }
        catch (OperationCanceledException) { /* 無視 */ }
    }

    private async Task<(BitmapSource? Left, BitmapSource? Right)> GetLayoutImagesAsync(int index, CancellationToken token)
    {
        if (CurrentDisplayMode == DisplayMode.Single)
        {
            var img = await GetCachedOrLoadImageAsync(index, token);
            return (img, null);
        }

        bool isRTL = CurrentDisplayMode == DisplayMode.SpreadRTL;
        var img1 = await GetCachedOrLoadImageAsync(index, token);
        var img2 = (index + 1 < _totalPageCount) ? await GetCachedOrLoadImageAsync(index + 1, token) : null;

        return isRTL ? (img2, img1) : (img1, img2);
    }

    private async Task<BitmapSource?> GetCachedOrLoadImageAsync(int index, CancellationToken token)
    {
        if (_imageCache.TryGetValue(index, out var cached)) return cached;

        return await Task.Run(async () => {
            await _loadLock.WaitAsync(token);
            try
            {
                return LoadAndFreezeImage(index, token);
            }
            finally { _loadLock.Release(); }
        }, token);
    }

    private BitmapSource? LoadAndFreezeImage(int index, CancellationToken token)
    {
        if (_currentSource == null || token.IsCancellationRequested) return null;

        try
        {
            using var stream = _currentSource.GetPageStream(index);
            if (stream == null || token.IsCancellationRequested) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            _imageCache.TryAdd(index, bitmap);
            return bitmap;
        }
        catch { return null; }
    }

    private void StartPrefetch(int centerIndex)
    {
        _prefetchCts?.Cancel();
        _prefetchCts = new CancellationTokenSource();
        var token = _prefetchCts.Token;

        Task.Run(async () =>
        {
            try
            {
                var keysToRemove = _imageCache.Keys.Where(k => Math.Abs(k - centerIndex) > CacheRange).ToList();
                foreach (var k in keysToRemove) _imageCache.TryRemove(k, out _);

                for (int i = 1; i <= CacheRange; i++)
                {
                    if (token.IsCancellationRequested) return;
                    foreach (int idx in new[] { centerIndex + i, centerIndex - i })
                    {
                        if (idx >= 0 && idx < _totalPageCount && !_imageCache.ContainsKey(idx))
                        {
                            await Task.Run(async () => {
                                await _loadLock.WaitAsync(token);
                                try { LoadAndFreezeImage(idx, token); }
                                finally { _loadLock.Release(); }
                            }, token);
                        }
                    }
                    await Task.Delay(5, token);
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
        await JumpToPageAsync(_currentPageIndex);
    }

    public void SetDisplayMode(DisplayMode mode) => CurrentDisplayMode = mode;
}

