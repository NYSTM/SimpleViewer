using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer.Presenters.Controllers;

/// <summary>
/// サムネイル一覧の構築・更新を担当するコントローラークラス。
/// Presenter からサムネイル画像を取得して ItemsControl に Button を追加する責務を持ちます。
/// UI 更新は Dispatcher 経由で実行します。
/// </summary>
public class ThumbnailController
{
    // Presenter（サムネイル取得を行う主要ロジック）
    private readonly SimpleViewerPresenter _presenter;
    // サムネイルを表示する ItemsControl（XAML 側のコントロール）
    private readonly ItemsControl _thumbnailSidebar;
    // UI スレッドに対する Dispatcher
    private readonly Dispatcher _dispatcher;
    // サムネイルやボタンからページジャンプするためのコールバック
    private readonly Func<int, Task> _jumpToPageCallback;
    // 操作後にウィンドウへフォーカスを戻すコールバック
    private readonly Action _focusWindowCallback;
    // サムネイルボタンに適用するスタイル
    private readonly Style _thumbnailButtonStyle;

    // サイドバー内の Button を保持（インデックス -> Button）
    private readonly Dictionary<int, Button> _sidebarItems = new();
    // 現在ハイライト表示中のインデックス（UI スレッド専用）
    private int _lastHighlightedIndex = -1;
    // 最新のハイライト要求インデックス（Interlocked で書き込み、UIスレッドで読み込む）
    private volatile int _pendingHighlightIndex = -1;

    // Build/Refresh 用のキャンセル制御
    private CancellationTokenSource? _buildCts;

    // 直近に構築したページ数と幅（部分更新判定用）
    private int _builtTotalPages = 0;
    private double _builtWidth = 0.0;

    // 並列処理のバッチサイズ（より大きく設定してスループット向上）
    private const int BatchSize = 16;

    // スクロール処理のデバウンス用
    private DateTime _lastScrollTime = DateTime.MinValue;
    private const int ScrollDebounceMs = 100;

    /// <summary>
    /// コンストラクタ: 必要な依存を注入して初期化します。
    /// </summary>
    /// <param name="presenter">サムネイル取得等を行う Presenter</param>
    /// <param name="thumbnailSidebar">サムネイル表示用 ItemsControl</param>
    /// <param name="dispatcher">UI スレッド用 Dispatcher</param>
    /// <param name="jumpToPageCallback">ページジャンプ用コールバック</param>
    /// <param name="focusWindowCallback">フォーカス復帰用コールバック</param>
    /// <param name="thumbnailButtonStyle">サムネイルボタンに適用するスタイル</param>
    public ThumbnailController(SimpleViewerPresenter presenter, ItemsControl thumbnailSidebar, Dispatcher dispatcher, Func<int, Task> jumpToPageCallback, Action focusWindowCallback, Style thumbnailButtonStyle)
    {
        _presenter = presenter;
        _thumbnailSidebar = thumbnailSidebar;
        _dispatcher = dispatcher;
        _jumpToPageCallback = jumpToPageCallback;
        _focusWindowCallback = focusWindowCallback;
        _thumbnailButtonStyle = thumbnailButtonStyle;
    }

    /// <summary>
    /// サムネイル領域を構築します（部分更新対応）。
    /// 既に同じ総ページ数で構築済みであれば再構築を避け、必要な差分のみ更新します。
    /// バッチ処理と非同期 UI 更新により、大量の画像でも応答性を維持します。
    /// </summary>
    /// <param name="totalPages">総ページ数</param>
    /// <param name="currentPageIndex">現在ページのインデックス（0 始まり）</param>
    /// <param name="desiredWidth">希望するサムネイル幅（ピクセル）</param>
    public async Task BuildAsync(int totalPages, int currentPageIndex, double desiredWidth)
    {
        try
        {
            // 部分更新条件: 既に同じ総ページ数で構築済み
            if (_builtTotalPages == totalPages && _builtTotalPages > 0)
            {
                if (desiredWidth > _builtWidth + 1.0)
                {
                    // 幅が変わった場合のみキャンセルして再構築
                    _buildCts?.Cancel();
                    _buildCts = new CancellationTokenSource();
                    var token = _buildCts.Token;
                    
                    await RefreshAsync((int)Math.Round(desiredWidth), token).ConfigureAwait(false);
                    _builtWidth = desiredWidth;
                }

                // ページ移動だけの場合はハイライトのみ更新（キャンセルしない）
                Highlight(currentPageIndex);
                return;
            }

            // 初回構築または総ページ数変更時のみキャンセル
            _buildCts?.Cancel();
            _buildCts = new CancellationTokenSource();
            var token2 = _buildCts.Token;

            // 既存アイテムがある場合は差分更新を行う
            if (_sidebarItems.Count > 0)
            {
                // 総数が減少した場合は余分なアイテムを削除
                if (_sidebarItems.Count > totalPages)
                {
                    var toRemove = _sidebarItems.Keys.Where(k => k >= totalPages).OrderByDescending(k => k).ToList();
                    foreach (var idx in toRemove)
                    {
                        token2.ThrowIfCancellationRequested();
                        if (_sidebarItems.TryGetValue(idx, out var btn))
                        {
                            await _dispatcher.InvokeAsync(() => _thumbnailSidebar.Items.Remove(btn), DispatcherPriority.Background);
                            _sidebarItems.Remove(idx);
                        }
                    }
                }

                // 既存数から不足分を追加
                int startAdd = _sidebarItems.Count > 0 ? _sidebarItems.Keys.Max() + 1 : 0;
                startAdd = Math.Max(startAdd, 0);

                await BuildThumbnailsBatchAsync(startAdd, totalPages, (int)Math.Round(desiredWidth), currentPageIndex, token2).ConfigureAwait(false);

                _builtTotalPages = totalPages;
                _builtWidth = desiredWidth;
                Highlight(currentPageIndex);
                return;
            }

            // 初回フル構築
            Clear();

            _buildCts = new CancellationTokenSource();
            var token3 = _buildCts.Token;

            await BuildThumbnailsBatchAsync(0, totalPages, (int)Math.Round(desiredWidth), currentPageIndex, token3).ConfigureAwait(false);

            _builtTotalPages = totalPages;
            _builtWidth = desiredWidth;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[BuildAsync] キャンセルされました");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuildAsync] エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// サムネイルをバッチ処理で並列生成します。
    /// UI の応答性を維持しながら効率的にサムネイルを生成します。
    /// UI 更新はバッチ単位でまとめて行い、ユーザー入力の応答性を維持します。
    /// </summary>
    private async Task BuildThumbnailsBatchAsync(int startIndex, int endIndex, int width, int currentPageIndex, CancellationToken token)
    {
        for (int batchStart = startIndex; batchStart < endIndex; batchStart += BatchSize)
        {
            token.ThrowIfCancellationRequested();

            int batchEnd = Math.Min(batchStart + BatchSize, endIndex);
            var batchIndices = Enumerable.Range(batchStart, batchEnd - batchStart).ToArray();

            // バッチ内で並列にサムネイルを取得
            var tasks = batchIndices.Select(async i =>
            {
                try
                {
                    var thumb = await _presenter.GetThumbnailAsync(i, width, token).ConfigureAwait(false);
                    return (Index: i, Thumbnail: thumb, Success: true);
                }
                catch (OperationCanceledException)
                {
                    // キャンセル時は Success=false でマーク（ログ出力なし）
                    return (Index: i, Thumbnail: (BitmapSource?)null, Success: false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BuildThumbnailsBatchAsync] サムネイル取得エラー index={i}: {ex.Message}");
                    return (Index: i, Thumbnail: (BitmapSource?)null, Success: true);
                }
            });

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // キャンセルされたタスクがある場合は中断
            if (results.Any(r => !r.Success))
            {
                token.ThrowIfCancellationRequested();
            }

            // バッチ結果をまとめて UI に追加（1回の Invoke でバッチ全体を処理）
            var validResults = results
                .Where(r => r.Thumbnail != null)
                .OrderBy(r => r.Index)
                .ToList();

            if (validResults.Count > 0)
            {
                try
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        Button? highlightedBtn = null;
                        
                        foreach (var (index, thumb, _) in validResults)
                        {
                            var item = CreateThumbnailElement(thumb!, index, width);
                            _thumbnailSidebar.Items.Add(item);
                            _sidebarItems[index] = item;

                            // アイテム追加時点での最新ハイライト要求を参照して適用
                            if (index == _pendingHighlightIndex)
                            {
                                // 以前のハイライトを解除
                                if (_lastHighlightedIndex != index &&
                                    _lastHighlightedIndex != -1 &&
                                    _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
                                {
                                    oldBtn.BorderBrush = Brushes.Transparent;
                                }
                                item.BorderBrush = SystemColors.HighlightBrush;
                                _lastHighlightedIndex = index;
                                highlightedBtn = item;
                            }
                        }
                        
                        // ハイライトしたアイテムがあればスクロール
                        if (highlightedBtn != null)
                        {
                            var btnToScroll = highlightedBtn;
                            _ = _dispatcher.InvokeAsync(() => 
                            {
                                // スクロール時点でも最新要求と一致し、ボタンがロード済みの場合のみ実行
                                if (_lastHighlightedIndex == (int)btnToScroll.Tag && btnToScroll.IsLoaded)
                                {
                                    TryScrollIntoView(btnToScroll);
                                }
                            }, DispatcherPriority.Loaded);
                        }
                    }, DispatcherPriority.Normal, token);
                }
                catch (OperationCanceledException)
                {
                    // UI更新中にキャンセルされた場合は静かに終了
                    return;
                }
            }

            // バッチごとに少し待機して UI スレッドに処理時間を譲る
            try
            {
                await Task.Delay(5, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 待機中のキャンセルは静かに終了
                return;
            }
        }
    }

    /// <summary>
    /// 指定インデックスのサムネイルをハイライト表示します。
    /// 高速スクロール中に連続して呼ばれた場合でも、最後の要求が確実に反映されます。
    /// アイテムがまだ追加されていない場合は _pendingHighlightIndex に記録し、
    /// BuildThumbnailsBatchAsync 内でアイテム追加時に適用されます。
    /// </summary>
    /// <param name="index">ハイライトするページのインデックス（0 始まり）</param>
    public void Highlight(int index)
    {
        // 最新の要求を記録（volatile により UI スレッド外からの書き込みも安全）
        _pendingHighlightIndex = index;

        // UI スレッドから呼ばれた場合は、即座にハイライトを更新
        if (_dispatcher.CheckAccess())
        {
            PerformHighlight(index);
        }
        else
        {
            // UI スレッド外から呼ばれた場合は InvokeAsync
            _ = _dispatcher.InvokeAsync(() => PerformHighlight(index), DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// ハイライト更新の実際の処理（UI スレッドで実行される前提）
    /// </summary>
    private void PerformHighlight(int index)
    {
        // Invoke 到達時点での最新要求を取得
        int target = _pendingHighlightIndex;

        // 要求が変わっている場合は何もしない（より新しい要求が後から処理される）
        if (target != index) return;

        // 以前のハイライト解除（対象が変わっている場合のみ）
        if (_lastHighlightedIndex != target &&
            _lastHighlightedIndex != -1 &&
            _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
        {
            oldBtn.BorderBrush = Brushes.Transparent;
        }

        if (_sidebarItems.TryGetValue(target, out var currentBtn))
        {
            // ターゲットのアイテムが既に存在する場合
            currentBtn.BorderBrush = SystemColors.HighlightBrush;
            _lastHighlightedIndex = target;

            // スクロールは低優先度で後から実行
            var btnForScroll = currentBtn;
            _ = _dispatcher.InvokeAsync(() => 
            {
                if (_pendingHighlightIndex == target && btnForScroll.IsLoaded)
                {
                    TryScrollIntoView(btnForScroll);
                }
            }, DispatcherPriority.Loaded);
        }
        else
        {
            // アイテムがまだ作成されていない場合、最も近い既存アイテムにスクロール
            _lastHighlightedIndex = target;

            if (_sidebarItems.Count > 0)
            {
                var nearestIndex = _sidebarItems.Keys
                    .OrderBy(k => Math.Abs(k - target))
                    .FirstOrDefault();

                if (_sidebarItems.TryGetValue(nearestIndex, out var nearestBtn))
                {
                    var btnForScroll = nearestBtn;
                    _ = _dispatcher.InvokeAsync(() =>
                    {
                        if (_pendingHighlightIndex == target && btnForScroll.IsLoaded)
                        {
                            TryScrollIntoView(btnForScroll);
                        }
                    }, DispatcherPriority.Loaded);
                }
            }
        }
    }

    /// <summary>
    /// サムネイル領域をクリアします。ビルド中のタスクがあればキャンセルします。
    /// UI 操作は同期的に実行してタイミング問題を回避します。
    /// </summary>
    public void Clear()
    {
        _buildCts?.Cancel();
        _buildCts = null;
        _pendingHighlightIndex = -1;

        _dispatcher.Invoke(() =>
        {
            _sidebarItems.Clear();
            _lastHighlightedIndex = -1;
            _thumbnailSidebar.Items.Clear();
        });

        _builtTotalPages = 0;
        _builtWidth = 0.0;
    }

    /// <summary>
    /// サムネイル要素（Button）を生成します。
    /// ボタンクリックでページジャンプを行うようイベントを登録します。
    /// </summary>
    /// <param name="source">サムネイル画像ソース</param>
    /// <param name="index">サムネイルのインデックス</param>
    /// <param name="width">サムネイル幅</param>
    /// <returns>生成した Button 要素</returns>
    private Button CreateThumbnailElement(BitmapSource source, int index, double width)
    {
        var btn = new Button
        {
            Content = new Image { Source = source, Width = width },
            Tag = index,
            Margin = new Thickness(4),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Transparent,
            Focusable = false,
            Style = _thumbnailButtonStyle
        };
        
        // クリックイベント: 即座にハイライトを更新し、ページジャンプは非同期で実行
        btn.Click += async (s, e) =>
        {
            int idx = (int)((Button)s!).Tag;
            
            // 即座にハイライトを更新（UI フィードバック）
            Highlight(idx);
            
            // ページジャンプを非同期で実行
            try
            {
                await _jumpToPageCallback.Invoke(idx);
                _focusWindowCallback?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CreateThumbnailElement] ページジャンプエラー: {ex.Message}");
            }
        };
        
        return btn;
    }

    /// <summary>
    /// 既存サムネイルの画像を希望幅に合わせて再取得します。
    /// バッチ処理で並列に取得して応答性を維持します。
    /// UI 更新はバッチ単位でまとめて行い、ユーザー入力の応答性を維持します。
    /// </summary>
    /// <param name="width">再取得する幅</param>
    /// <param name="token">キャンセルトークン</param>
    public async Task RefreshAsync(int width, CancellationToken token)
    {
        try
        {
            var indices = _sidebarItems.Keys.ToList();
            
            // バッチ処理で並列に取得
            for (int batchStart = 0; batchStart < indices.Count; batchStart += BatchSize)
            {
                token.ThrowIfCancellationRequested();

                var batchIndices = indices.Skip(batchStart).Take(BatchSize).ToArray();
                
                var tasks = batchIndices.Select(async idx =>
                {
                    try
                    {
                        var thumb = await _presenter.GetThumbnailAsync(idx, width, token).ConfigureAwait(false);
                        return (Index: idx, Thumbnail: thumb);
                    }
                    catch (OperationCanceledException)
                    {
                        return (Index: idx, Thumbnail: (BitmapSource?)null);
                    }
                });

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                // バッチ結果をまとめて UI に反映（1回の Invoke でバッチ全体を処理）
                var validResults = results.Where(r => r.Thumbnail != null).ToList();
                
                if (validResults.Count > 0)
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        foreach (var (idx, thumb) in validResults)
                        {
                            if (_sidebarItems.TryGetValue(idx, out var btn))
                            {
                                if (btn.Content is Image img)
                                {
                                    img.Source = thumb;
                                    img.Width = width;
                                }
                                else
                                {
                                    btn.Content = new Image { Source = thumb, Width = width };
                                }
                            }
                        }
                    }, DispatcherPriority.Normal);
                }

                // バッチごとに少し待機して UI スレッドに処理時間を譲る
                await Task.Delay(5, token).ConfigureAwait(false);
            }

            _builtWidth = width;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThumbnailController.RefreshAsync Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定要素から親の ScrollViewer を探索します（再帰）。
    /// Template 内の ScrollViewer も探索対象に含めます。
    /// </summary>
    /// <param name="start">探索開始要素</param>
    /// <returns>見つかった ScrollViewer、なければ null</returns>
    private ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        if (start == null) return null;
        if (start is ScrollViewer sv) return sv;

        // 子要素を探索
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(start); i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }

        // ItemsControl の場合、Template 内も探索
        if (start is ItemsControl ic && ic.Template != null)
        {
            if (ic.ApplyTemplate() && VisualTreeHelper.GetChildrenCount(ic) > 0)
            {
                var child = VisualTreeHelper.GetChild(ic, 0);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
        }

        return null;
    }

    /// <summary>
    /// ItemsControl の ItemsPanel を探索して VirtualizingStackPanel や StackPanel を返します。
    /// </summary>
    /// <param name="start">探索開始要素</param>
    /// <returns>見つかった Panel、なければ null</returns>
    private Panel? FindItemsPanel(DependencyObject start)
    {
        if (start == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(start); i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is Panel p && (p is VirtualizingStackPanel || p is StackPanel)) return p;
            var nested = FindItemsPanel(child);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>
    /// 指定のボタンを ScrollViewer 内にスクロールして可視化します。
    /// </summary>
    /// <param name="btn">可視化するボタン</param>
    private void TryScrollIntoView(Button btn)
    {
        try
        {
            // デバウンス: 短時間での連続呼び出しをスキップ
            var now = DateTime.UtcNow;
            if ((now - _lastScrollTime).TotalMilliseconds < ScrollDebounceMs)
            {
                return;
            }
            
            if (!btn.IsLoaded)
            {
                return;
            }

            var sv = FindScrollViewer(_thumbnailSidebar);
            if (sv == null)
            {
                return;
            }

            // CanContentScroll=True の論理スクロールの場合は ViewportHeight チェックをスキップ
            if (!sv.CanContentScroll && sv.ViewportHeight < 50)
            {
                _ = RetryScrollIntoViewAsync(btn, 0);
                return;
            }

            // 実際のスクロール処理を実行
            PerformScroll(btn, sv);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TryScrollIntoView] 例外: {ex.Message}");
        }
    }

    /// <summary>
    /// ViewportHeight が小さい場合にリトライする
    /// </summary>
    private async Task RetryScrollIntoViewAsync(Button btn, int retryCount)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 50;

        if (retryCount >= maxRetries)
        {
            return;
        }

        await Task.Delay(retryDelayMs);

        await _dispatcher.InvokeAsync(() =>
        {
            var sv = FindScrollViewer(_thumbnailSidebar);
            if (sv == null || !btn.IsLoaded)
            {
                return;
            }

            if (sv.ViewportHeight < 50)
            {
                _ = RetryScrollIntoViewAsync(btn, retryCount + 1);
                return;
            }

            PerformScroll(btn, sv);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 実際のスクロール処理を実行
    /// </summary>
    private void PerformScroll(Button btn, ScrollViewer sv)
    {
        try
        {
            // CanContentScroll=True の場合は、BringIntoView を使用
            if (sv.CanContentScroll)
            {
                btn.BringIntoView();
                _lastScrollTime = DateTime.UtcNow;
                return;
            }

            // 物理スクロールモード（CanContentScroll=False）の場合
            if (!IsDescendantOf(btn, sv))
            {
                return;
            }

            var transform = btn.TransformToAncestor(sv);
            var rect = transform.TransformBounds(new Rect(0, 0, btn.ActualWidth, btn.ActualHeight));

            bool needsScroll = false;
            double newOffset = sv.VerticalOffset;

            if (rect.Top < 0)
            {
                newOffset = Math.Max(0, sv.VerticalOffset + rect.Top - 10);
                needsScroll = true;
            }
            else if (rect.Bottom > sv.ViewportHeight)
            {
                newOffset = sv.VerticalOffset + rect.Bottom - sv.ViewportHeight + 10;
                needsScroll = true;
            }

            if (needsScroll)
            {
                sv.ScrollToVerticalOffset(newOffset);
                _lastScrollTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PerformScroll] 例外: {ex.Message}");
        }
    }

    /// <summary>
    /// child が ancestor の子孫であるかをチェックします。
    /// </summary>
    private bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent == ancestor)
                return true;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return false;
    }
}
