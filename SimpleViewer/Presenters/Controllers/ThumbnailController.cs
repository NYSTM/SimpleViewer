using SimpleViewer.Utils.UI;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer.Presenters.Controllers;

/// <summary>
/// サムネイル一覧の構築・更新を担当するコントローラークラス。
/// </summary>
public class ThumbnailController
{
    private static readonly Brush SelectedThumbnailBorderBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22));
    private static readonly Brush SelectedThumbnailBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 237, 213));

    private readonly SimpleViewerPresenter _presenter;
    private readonly ItemsControl _thumbnailSidebar;
    private readonly Dispatcher _dispatcher;
    private readonly ThumbnailElementFactory _elementFactory;
    private readonly ScrollHelper _scrollHelper;

    private readonly Dictionary<int, Button> _sidebarItems = new();
    private int _lastHighlightedIndex = -1;
    private volatile int _pendingHighlightIndex = -1;

    private CancellationTokenSource? _buildCts;
    private int _builtTotalPages = 0;
    private double _builtWidth = 0.0;

    private const int BatchSize = 16;
    private const int InitialSequentialCount = 8;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ThumbnailController(
        SimpleViewerPresenter presenter,
        ItemsControl thumbnailSidebar,
        Dispatcher dispatcher,
        Func<int, Task> jumpToPageCallback,
        Action focusWindowCallback,
        Style thumbnailButtonStyle)
    {
        _presenter = presenter;
        _thumbnailSidebar = thumbnailSidebar;
        _dispatcher = dispatcher;
        _scrollHelper = new ScrollHelper(dispatcher);
        _elementFactory = new ThumbnailElementFactory(
            thumbnailButtonStyle,
            jumpToPageCallback,
            focusWindowCallback,
            Highlight);
    }

    /// <summary>
    /// サムネイル領域を構築します（部分更新対応）。
    /// </summary>
    public async Task BuildAsync(int totalPages, int currentPageIndex, double desiredWidth)
    {
        try
        {
            if (_builtTotalPages == totalPages && _builtTotalPages > 0)
            {
                if (desiredWidth > _builtWidth + 1.0)
                {
                    _buildCts?.Cancel();
                    _buildCts = new CancellationTokenSource();
                    await RefreshAsync((int)Math.Round(desiredWidth), _buildCts.Token).ConfigureAwait(false);
                    _builtWidth = desiredWidth;
                }
                Highlight(currentPageIndex);
                return;
            }

            _buildCts?.Cancel();
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;

            if (_sidebarItems.Count > 0)
            {
                await HandleExistingItemsAsync(totalPages, token);
                int startAdd = _sidebarItems.Count > 0 ? _sidebarItems.Keys.Max() + 1 : 0;
                await BuildThumbnailsBatchAsync(Math.Max(startAdd, 0), totalPages, (int)Math.Round(desiredWidth), token).ConfigureAwait(false);
                _builtTotalPages = totalPages;
                _builtWidth = desiredWidth;
                Highlight(currentPageIndex);
                return;
            }

            Clear();
            _buildCts = new CancellationTokenSource();
            await BuildThumbnailsBatchAsync(0, totalPages, (int)Math.Round(desiredWidth), _buildCts.Token).ConfigureAwait(false);
            _builtTotalPages = totalPages;
            _builtWidth = desiredWidth;
        }
        catch (OperationCanceledException) { Debug.WriteLine("[BuildAsync] キャンセルされました"); }
        catch (Exception ex) { Debug.WriteLine($"[BuildAsync] エラー: {ex.Message}"); }
    }

    private async Task HandleExistingItemsAsync(int totalPages, CancellationToken token)
    {
        if (_sidebarItems.Count > totalPages)
        {
            var toRemove = _sidebarItems.Keys.Where(k => k >= totalPages).OrderByDescending(k => k).ToList();
            foreach (var idx in toRemove)
            {
                token.ThrowIfCancellationRequested();
                if (_sidebarItems.TryGetValue(idx, out var btn))
                {
                    await _dispatcher.InvokeAsync(() => _thumbnailSidebar.Items.Remove(btn), DispatcherPriority.Background);
                    _sidebarItems.Remove(idx);
                }
            }
        }
    }

    private async Task BuildThumbnailsBatchAsync(int startIndex, int endIndex, int width, CancellationToken token)
    {
        if (startIndex < InitialSequentialCount)
        {
            int sequentialEnd = Math.Min(InitialSequentialCount, endIndex);
            for (int i = startIndex; i < sequentialEnd; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var thumb = await _presenter.GetThumbnailAsync(i, width, token).ConfigureAwait(false);
                    if (thumb != null) await AddThumbnailButtonAsync(i, thumb, token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Debug.WriteLine($"[BuildThumbnailsBatchAsync] エラー index={i}: {ex.Message}"); }
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            startIndex = sequentialEnd;
        }

        for (int batchStart = startIndex; batchStart < endIndex; batchStart += BatchSize)
        {
            token.ThrowIfCancellationRequested();
            int batchEnd = Math.Min(batchStart + BatchSize, endIndex);
            var results = await GetBatchThumbnailsAsync(batchStart, batchEnd, width, token).ConfigureAwait(false);
            var validResults = results.Where(r => r.Success && r.Thumbnail != null).ToList();
            if (validResults.Count == 0 && results.Any(r => !r.Success)) break;
            if (validResults.Count > 0) await UpdateUIWithBatchAsync(validResults, token);
            await Task.Delay(20, token).ConfigureAwait(false);
        }
    }

    private async Task<(int Index, BitmapSource? Thumbnail, bool Success)[]> GetBatchThumbnailsAsync(
        int batchStart, int batchEnd, int width, CancellationToken token)
    {
        var batchIndices = Enumerable.Range(batchStart, batchEnd - batchStart).ToArray();
        var tasks = batchIndices.Select(async i =>
        {
            try
            {
                var thumb = await _presenter.GetThumbnailAsync(i, width, token).ConfigureAwait(false);
                return (Index: i, Thumbnail: thumb, Success: true);
            }
            catch (OperationCanceledException) { return (i, null, false); }
            catch (Exception ex) { Debug.WriteLine($"[GetBatchThumbnailsAsync] エラー index={i}: {ex.Message}"); return (i, null, true); }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task AddThumbnailButtonAsync(int index, BitmapSource thumb, CancellationToken token)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var btn = _elementFactory.CreateSimpleButton(thumb, index);
            _sidebarItems[index] = btn;
            _thumbnailSidebar.Items.Add(btn);
        }, DispatcherPriority.Normal, token);
    }

    private async Task UpdateUIWithBatchAsync(IEnumerable<(int Index, BitmapSource? Thumbnail, bool Success)> validResults, CancellationToken token)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var (index, thumbnail, _) in validResults)
            {
                if (_sidebarItems.TryGetValue(index, out var existingBtn))
                {
                    ThumbnailElementFactory.UpdateButtonImage(existingBtn, thumbnail);
                }
                else
                {
                    var btn = _elementFactory.CreateSimpleButton(thumbnail, index);
                    _sidebarItems[index] = btn;
                    _thumbnailSidebar.Items.Add(btn);
                }
            }
        }, DispatcherPriority.Normal, token);
    }

    /// <summary>
    /// 指定インデックスのサムネイルをハイライト表示します。
    /// </summary>
    public void Highlight(int index)
    {
        _pendingHighlightIndex = index;
        if (_dispatcher.CheckAccess())
            PerformHighlight(index);
        else
            _ = _dispatcher.InvokeAsync(() => PerformHighlight(index), DispatcherPriority.Normal);
    }

    private void PerformHighlight(int index)
    {
        int target = _pendingHighlightIndex;
        if (target != index) return;

        if (_lastHighlightedIndex != target && _lastHighlightedIndex != -1 && _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
        {
            oldBtn.BorderBrush = Brushes.Transparent;
            oldBtn.Background = Brushes.Transparent;
        }

        if (_sidebarItems.TryGetValue(target, out var currentBtn))
        {
            currentBtn.BorderBrush = SelectedThumbnailBorderBrush;
            currentBtn.Background = SelectedThumbnailBackgroundBrush;
            _lastHighlightedIndex = target;
            _ = _dispatcher.InvokeAsync(() =>
            {
                if (_pendingHighlightIndex == target && currentBtn.IsLoaded)
                    _scrollHelper.TryScrollIntoView(currentBtn, _thumbnailSidebar);
            }, DispatcherPriority.Loaded);
        }
        else
        {
            _lastHighlightedIndex = target;
            if (_sidebarItems.Count > 0)
            {
                var nearestIndex = _sidebarItems.Keys.OrderBy(k => Math.Abs(k - target)).FirstOrDefault();
                if (_sidebarItems.TryGetValue(nearestIndex, out var nearestBtn))
                    _ = _dispatcher.InvokeAsync(() =>
                    {
                        if (_pendingHighlightIndex == target && nearestBtn.IsLoaded)
                            _scrollHelper.TryScrollIntoView(nearestBtn, _thumbnailSidebar);
                    }, DispatcherPriority.Loaded);
            }
        }
    }

    /// <summary>
    /// サムネイル領域をクリアします。
    /// </summary>
    public void Clear()
    {
        _buildCts?.Cancel();
        _buildCts = null;
        _pendingHighlightIndex = -1;
        _dispatcher.Invoke(() => { _sidebarItems.Clear(); _lastHighlightedIndex = -1; _thumbnailSidebar.Items.Clear(); });
        _builtTotalPages = 0;
        _builtWidth = 0.0;
    }

    /// <summary>
    /// 既存サムネイルの画像を希望幅に合わせて再取得します。
    /// </summary>
    public async Task RefreshAsync(int width, CancellationToken token)
    {
        try
        {
            var indices = _sidebarItems.Keys.ToList();
            for (int batchStart = 0; batchStart < indices.Count; batchStart += BatchSize)
            {
                token.ThrowIfCancellationRequested();
                var batchIndices = indices.Skip(batchStart).Take(BatchSize).ToArray();
                var tasks = batchIndices.Select(async idx =>
                {
                    try
                    {
                        var thumb = await _presenter.GetThumbnailAsync(idx, width, token).ConfigureAwait(false);
                        return (Index: idx, Thumbnail: thumb, Success: true);
                    }
                    catch (OperationCanceledException) { return (idx, null, false); }
                    catch (Exception ex) { Debug.WriteLine($"[RefreshAsync] エラー index={idx}: {ex.Message}"); return (idx, null, true); }
                });
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var (index, thumbnail, success) in results)
                    {
                        if (!success) continue;
                        if (_sidebarItems.TryGetValue(index, out var btn))
                        {
                            ThumbnailElementFactory.UpdateButtonImageAndWidth(btn, thumbnail, width);
                        }
                    }
                }, DispatcherPriority.Background, token);
                await Task.Delay(10, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[RefreshAsync] 全体エラー: {ex.Message}"); }
    }

    /// <summary>
    /// 既に生成済みのサムネイルのインデックス一覧を取得します。
    /// </summary>
    public IEnumerable<int> GetExistingThumbnailIndices() => _sidebarItems.Keys.OrderBy(x => x);

    /// <summary>
    /// 指定インデックスの既存サムネイル画像を取得します。
    /// </summary>
    public BitmapSource? GetExistingThumbnail(int index)
    {
        if (_sidebarItems.TryGetValue(index, out var btn) && btn.Content is Image img)
        {
            return img.Source as BitmapSource;
        }
        return null;
    }
}
