using SimpleViewer.Models;
using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Presenters;

public class SimpleViewerPresenter
{
    private readonly IView _view;
    private IImageSource? _currentSource;
    private int _currentPageIndex = 0;
    private int _totalPageCount = 0;
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Single;

    // キャッシュ: SkiaSharpでデコード済みのBitmapSourceを保持
    private readonly ConcurrentDictionary<int, BitmapSource> _imageCache = new();
    private const int CacheRange = 4; // 前後4ページをキャッシュ

    // 非同期制御
    private CancellationTokenSource? _navigationCts;
    private CancellationTokenSource? _prefetchCts;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public SimpleViewerPresenter(IView view) => _view = view;

    /// <summary>
    /// 新しい画像ソース（フォルダ、ZIP、PDF）を開く
    /// </summary>
    public async Task OpenSourceAsync(string path)
    {
        CloseSource();

        try
        {
            _currentSource = await ImageSourceFactory.CreateSourceAsync(path);
            _totalPageCount = await _currentSource.GetPageCountAsync();

            if (_totalPageCount > 0)
            {
                await JumpToPageAsync(0);
            }
        }
        catch (Exception ex)
        {
            _view.ShowError($"ソースを開けません: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のソースを閉じ、リソースを解放する
    /// </summary>
    public void CloseSource()
    {
        _navigationCts?.Cancel();
        _prefetchCts?.Cancel();
        _imageCache.Clear();
        _currentSource?.Dispose();
        _currentSource = null;
        _currentPageIndex = 0;
        _totalPageCount = 0;
    }

    /// <summary>
    /// 指定されたページにジャンプし、表示を更新する
    /// </summary>
    public async Task JumpToPageAsync(int index)
    {
        if (_currentSource == null || _totalPageCount == 0) return;

        // 範囲外チェック
        if (index < 0) index = 0;
        if (index >= _totalPageCount) index = _totalPageCount - 1;

        _currentPageIndex = index;

        // 既存のナビゲーションをキャンセル
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
                // 見開きモード
                int firstIdx = _currentPageIndex;
                int secondIdx = _currentPageIndex + 1;

                // RTL（右から左）ならインデックスを入れ替えて表示
                if (CurrentDisplayMode == DisplayMode.SpreadRTL)
                {
                    left = (secondIdx < _totalPageCount) ? await GetOrLoadImageAsync(secondIdx, token) : null;
                    right = await GetOrLoadImageAsync(firstIdx, token);
                }
                else // SpreadLTR
                {
                    left = await GetOrLoadImageAsync(firstIdx, token);
                    right = (secondIdx < _totalPageCount) ? await GetOrLoadImageAsync(secondIdx, token) : null;
                }
            }

            if (!token.IsCancellationRequested)
            {
                _view.SetImages(left, right);
                _view.UpdateProgress(_currentPageIndex + 1, _totalPageCount);

                // 表示が完了してから周辺ページの先読みを開始
                StartPrefetching(_currentPageIndex);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// キャッシュにあればそれを返し、なければSkiaSharpでロードする
    /// </summary>
    private async Task<BitmapSource?> GetOrLoadImageAsync(int index, CancellationToken token)
    {
        if (index < 0 || index >= _totalPageCount || _currentSource == null) return null;

        if (_imageCache.TryGetValue(index, out var cached)) return cached;

        await _loadLock.WaitAsync(token);
        try
        {
            // 二重チェック
            if (_imageCache.TryGetValue(index, out var cachedAgain)) return cachedAgain;

            // IImageSourceの新しい高速メソッドを呼び出し
            var bitmap = await _currentSource.GetPageImageAsync(index);
            if (bitmap != null)
            {
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
    /// サイドバー等のサムネイル取得（デコード時リサイズを使用）
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (_currentSource == null) return null;
        return await _currentSource.GetThumbnailAsync(index, width);
    }

    /// <summary>
    /// 現在のページの前後をバックグラウンドでロードする
    /// </summary>
    private void StartPrefetching(int centerIndex)
    {
        _prefetchCts?.Cancel();
        _prefetchCts = new CancellationTokenSource();
        var token = _prefetchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // 現在地から遠い順にキャッシュを整理（簡易的なLRU）
                if (_imageCache.Count > CacheRange * 3)
                {
                    var keysToRemove = _imageCache.Keys
                        .Where(k => Math.Abs(k - centerIndex) > CacheRange * 2)
                        .ToList();
                    foreach (var k in keysToRemove) _imageCache.TryRemove(k, out _);
                }

                // 前後のページを順番にロード
                for (int i = 1; i <= CacheRange; i++)
                {
                    if (token.IsCancellationRequested) return;

                    foreach (int idx in new[] { centerIndex + i, centerIndex - i })
                    {
                        if (idx >= 0 && idx < _totalPageCount && !_imageCache.ContainsKey(idx))
                        {
                            await GetOrLoadImageAsync(idx, token);
                        }
                    }
                    // CPU負荷を下げ、UIスレッドに譲るための短い待機
                    await Task.Delay(10, token);
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