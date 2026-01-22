using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;

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
    // 最後にハイライトされたインデックス
    private int _lastHighlightedIndex = -1;

    // Build/Refresh 用のキャンセル制御
    private CancellationTokenSource? _buildCts;

    // 直近に構築したページ数と幅（部分更新判定用）
    private int _builtTotalPages = 0;
    private double _builtWidth = 0.0;

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
    /// </summary>
    /// <param name="totalPages">総ページ数</param>
    /// <param name="currentPageIndex">現在ページのインデックス（0 始まり）</param>
    /// <param name="desiredWidth">希望するサムネイル幅（ピクセル）</param>
    public async Task BuildAsync(int totalPages, int currentPageIndex, double desiredWidth)
    {
        _buildCts?.Cancel();
        _buildCts = new CancellationTokenSource();
        var token = _buildCts.Token;

        try
        {
            // 部分更新条件: 既に同じ総ページ数で構築済み
            if (_builtTotalPages == totalPages && _builtTotalPages > 0)
            {
                // 幅が変わった場合は既存アイテムの画像だけ差し替える
                // 既に生成済みのサムネイルが要求幅以上であれば再取得は不要。
                // デジカメやスキャナ画像は元画像幅が固定のことが多いため、
                // 小さい幅への変更は既存サムネイルで十分対応できる。
                // よって、要求幅がこれまでの構築幅より大きい場合のみ再取得する。
                if (desiredWidth > _builtWidth + 1.0)
                {
                    await RefreshAsync((int)Math.Round(desiredWidth), token).ConfigureAwait(false);
                    _builtWidth = desiredWidth;
                }

                // ハイライトだけ更新して終了
                Highlight(currentPageIndex);
                return;
            }

            // 既存アイテムがある場合は差分更新を行う
            if (_sidebarItems.Count > 0)
            {
                // 総数が減少した場合は余分なアイテムを削除
                if (_sidebarItems.Count > totalPages)
                {
                    var toRemove = _sidebarItems.Keys.Where(k => k >= totalPages).OrderByDescending(k => k).ToList();
                    foreach (var idx in toRemove)
                    {
                        token.ThrowIfCancellationRequested();
                        if (_sidebarItems.TryGetValue(idx, out var btn))
                        {
                            _dispatcher.Invoke(() => _thumbnailSidebar.Items.Remove(btn));
                            _sidebarItems.Remove(idx);
                        }
                    }
                }

                // 既存数から不足分を追加
                int startAdd = 0;
                if (_sidebarItems.Count > 0) startAdd = _sidebarItems.Keys.Max() + 1;
                startAdd = Math.Max(startAdd, 0);

                _buildCts = new CancellationTokenSource();
                token = _buildCts.Token;

                for (int i = startAdd; i < totalPages; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var thumb = await _presenter.GetThumbnailAsync(i, (int)Math.Round(desiredWidth), token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (thumb != null)
                    {
                        // UI 要素は UI スレッドで生成・追加する
                        _dispatcher.Invoke(() =>
                        {
                            var item = CreateThumbnailElement(thumb, i, desiredWidth);
                            _thumbnailSidebar.Items.Add(item);
                            _sidebarItems[i] = item;
                        });
                    }

                    if (i % 5 == 0) await Task.Yield();
                }

                _builtTotalPages = totalPages;
                _builtWidth = desiredWidth;
                Highlight(currentPageIndex);
                return;
            }

            // 初回フル構築
            Clear();

            _buildCts = new CancellationTokenSource();
            token = _buildCts.Token;

            for (int i = 0; i < totalPages; i++)
            {
                token.ThrowIfCancellationRequested();

                var thumb = await _presenter.GetThumbnailAsync(i, (int)Math.Round(desiredWidth), token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                if (thumb != null)
                {
                    // UI 要素は UI スレッドで生成・追加する
                    _dispatcher.Invoke(() =>
                    {
                        var item = CreateThumbnailElement(thumb, i, desiredWidth);
                        _thumbnailSidebar.Items.Add(item);
                        _sidebarItems[i] = item;

                        if (i == currentPageIndex)
                        {
                            Highlight(i);
                        }
                    });
                }

                // 大量ページの際に UI スレッドの応答性を保つ
                if (i % 5 == 0) await Task.Yield();
            }

            _builtTotalPages = totalPages;
            _builtWidth = desiredWidth;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"ThumbnailController.BuildAsync Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定インデックスのサムネイルをハイライト表示します。
    /// UI 更新は Dispatcher で実行されます。
    /// </summary>
    /// <param name="index">ハイライトするページのインデックス（0 始まり）</param>
    public void Highlight(int index)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            // 以前のハイライト解除
            if (_lastHighlightedIndex != -1 && _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
                oldBtn.BorderBrush = Brushes.Transparent;

            if (_sidebarItems.TryGetValue(index, out var currentBtn))
            {
                currentBtn.BorderBrush = SystemColors.HighlightBrush;

                // 可視化を試みる
                TryScrollIntoView(currentBtn);

                _lastHighlightedIndex = index;
            }

            // レイアウト更新を強制（ハイライト適用後に実行）
            _thumbnailSidebar.UpdateLayout();
        }, DispatcherPriority.Normal);
    }

    /// <summary>
    /// サムネイル領域をクリアします。ビルド中のタスクがあればキャンセルします。
    /// </summary>
    public void Clear()
    {
        _buildCts?.Cancel();
        _buildCts = null;

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
        btn.Click += async (s, _) =>
        {
            int idx = (int)((Button)s).Tag;
            Highlight(idx);
            await _jumpToPageCallback.Invoke(idx);
            _focusWindowCallback?.Invoke();
        };
        return btn;
    }

    /// <summary>
    /// 既存サムネイルの画像を希望幅に合わせて再取得します。
    /// 呼び出し元でキャンセル可能なトークンを渡してください。
    /// </summary>
    /// <param name="width">再取得する幅</param>
    /// <param name="token">キャンセルトークン</param>
    public async Task RefreshAsync(int width, CancellationToken token)
    {
        try
        {
            var indices = _sidebarItems.Keys.ToList();
            foreach (var idx in indices)
            {
                token.ThrowIfCancellationRequested();
                var thumb = await _presenter.GetThumbnailAsync(idx, width, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (thumb != null && _sidebarItems.TryGetValue(idx, out var btn))
                {
                    _dispatcher.Invoke(() =>
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
                    });
                }
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
    /// </summary>
    /// <param name="start">探索開始要素</param>
    /// <returns>見つかった ScrollViewer、なければ null</returns>
    private ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        if (start == null) return null;
        if (start is ScrollViewer sv) return sv;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(start); i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
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
    /// 指定ボタンを可能な限り可視領域にスクロールして表示します。
    /// 複数の手法を試行し、環境に依存する問題に対処します。
    /// </summary>
    /// <param name="btn">対象ボタン</param>
    private void TryScrollIntoView(Button btn)
    {
        int idx = -1;
        try
        {
            if (btn.Tag is int t) idx = t;
        }
        catch { }

        Debug.WriteLine($"TryScrollIntoView start idx={idx}");

        try
        {
            var sv = FindScrollViewer(_thumbnailSidebar);
            var panel = FindItemsPanel(_thumbnailSidebar);
            Debug.WriteLine($"Found sv={(sv!=null)}, panel={(panel!=null)}");

            // VirtualizingStackPanel を使える場合は MakeVisible を試す
            if (panel is VirtualizingStackPanel vsp)
            {
                try
                {
                    double rectHeight = btn.RenderSize.Height;
                    if (rectHeight <= 0)
                    {
                        var other = _sidebarItems.Values.FirstOrDefault(x => x != btn && x.RenderSize.Height > 0);
                        if (other != null) rectHeight = other.RenderSize.Height;
                        if (rectHeight <= 0) rectHeight = 48;
                    }

                    var rect = new Rect(new Point(0, 0), new Size(btn.RenderSize.Width > 0 ? btn.RenderSize.Width : _thumbnailSidebar.ActualWidth, rectHeight));
                    Debug.WriteLine($"Trying MakeVisible idx={idx} rect={rect}");
                    vsp.MakeVisible(btn, rect);
                    Debug.WriteLine($"MakeVisible done idx={idx}");

                    if (sv != null && sv.CanContentScroll && idx >= 0)
                    {
                        double logicalTarget = Math.Max(0, idx - 1);
                        Debug.WriteLine($"Forcing logical scroll to {logicalTarget} (idx={idx})");
                        sv.ScrollToVerticalOffset(logicalTarget);
                    }

                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var sv2 = FindScrollViewer(_thumbnailSidebar);
                            var panel2 = FindItemsPanel(_thumbnailSidebar);
                            if (sv2 == null) return;

                            Point topLeft2;
                            if (panel2 != null)
                            {
                                try { topLeft2 = btn.TranslatePoint(new Point(0, 0), panel2); }
                                catch { topLeft2 = new Point(double.NaN, double.NaN); }

                                if (!double.IsNaN(topLeft2.Y))
                                {
                                    double itemTop2 = topLeft2.Y;
                                    double itemBottom2 = itemTop2 + (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0 ? btn.RenderSize.Height : btn.ActualHeight);
                                    double viewTop2 = sv2.VerticalOffset;
                                    double viewBottom2 = sv2.VerticalOffset + sv2.ViewportHeight;

                                    const double margin = 4.0;
                                    if (!sv2.CanContentScroll)
                                    {
                                        if (itemTop2 < viewTop2)
                                        {
                                            sv2.ScrollToVerticalOffset(Math.Max(0, itemTop2 - margin));
                                            Debug.WriteLine($"Post-MakeVisible forced scroll up to {Math.Max(0, itemTop2 - margin)} idx={idx}");
                                            return;
                                        }
                                        if (itemBottom2 > viewBottom2)
                                        {
                                            sv2.ScrollToVerticalOffset(Math.Min(sv2.ExtentHeight - sv2.ViewportHeight, itemBottom2 - sv2.ViewportHeight + margin));
                                            Debug.WriteLine($"Post-MakeVisible forced scroll down idx={idx}");
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        try
                                        {
                                            double currentLogical = sv2.VerticalOffset;
                                            if (idx < (int)currentLogical)
                                            {
                                                sv2.ScrollToVerticalOffset(Math.Max(0, idx - 1));
                                                Debug.WriteLine($"Post-MakeVisible forced logical scroll up to {Math.Max(0, idx - 1)} idx={idx}");
                                                return;
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }

                            Point topLeftSv2;
                            try { topLeftSv2 = btn.TranslatePoint(new Point(0, 0), sv2); }
                            catch { topLeftSv2 = new Point(double.NaN, double.NaN); }

                            if (!double.IsNaN(topLeftSv2.Y))
                            {
                                double itemTopSv2 = topLeftSv2.Y;
                                if (itemTopSv2 < 0)
                                {
                                    double newOff = Math.Max(0, sv2.VerticalOffset + itemTopSv2 - 4.0);
                                    sv2.ScrollToVerticalOffset(newOff);
                                    Debug.WriteLine($"Post-MakeVisible fallback scroll up to {newOff} idx={idx}");
                                    return;
                                }
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"Post-MakeVisible verify failed: {ex.Message}"); }
                    }), DispatcherPriority.Background);

                    return;
                }
                catch (Exception ex) { Debug.WriteLine($"MakeVisible failed: {ex.Message}"); }
            }

            if (sv == null)
            {
                Debug.WriteLine("No ScrollViewer - calling BringIntoView");
                try { btn.BringIntoView(); } catch { }
                return;
            }

            sv.UpdateLayout();
            btn.UpdateLayout();

            double itemTop, itemBottom;

            if (panel != null)
            {
                Point topLeft;
                try
                {
                    topLeft = btn.TranslatePoint(new Point(0, 0), panel);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TranslatePoint to panel failed: {ex.Message}");
                    topLeft = new Point(double.NaN, double.NaN);
                }

                if (!double.IsNaN(topLeft.Y))
                {
                    itemTop = topLeft.Y;
                    itemBottom = itemTop + (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0 ? btn.RenderSize.Height : btn.ActualHeight);

                    double viewTop = sv.VerticalOffset;
                    double viewBottom = sv.VerticalOffset + sv.ViewportHeight;

                    const double margin = 4.0;

                    if (!sv.CanContentScroll)
                    {
                        if (itemTop < viewTop)
                        {
                            double newOffset = Math.Max(0, itemTop - margin);
                            sv.ScrollToVerticalOffset(newOffset);
                            return;
                        }
                        else if (itemBottom > viewBottom)
                        {
                            double newOffset = Math.Min(sv.ExtentHeight - sv.ViewportHeight, itemBottom - sv.ViewportHeight + margin);
                            sv.ScrollToVerticalOffset(newOffset);
                            return;
                        }

                        try { btn.BringIntoView(); } catch { }
                        return;
                    }
                    else
                    {
                        try
                        {
                            double currentLogical = sv.VerticalOffset;
                            if (idx < (int)currentLogical)
                            {
                                sv.ScrollToVerticalOffset(Math.Max(0, idx - 1));
                                return;
                            }
                            else if (idx > (int)(currentLogical + sv.ViewportHeight))
                            {
                                sv.ScrollToVerticalOffset(Math.Max(0, idx - (int)sv.ViewportHeight + 1));
                                return;
                            }

                            try { btn.BringIntoView(); } catch { }
                            return;
                        }
                        catch (Exception ex) { Debug.WriteLine($"Logical scroll branch failed: {ex.Message}"); }
                    }
                }
            }

            Point topLeftFallback;
            try
            {
                topLeftFallback = btn.TranslatePoint(new Point(0, 0), sv);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranslatePoint to sv failed: {ex.Message}");
                try { btn.BringIntoView(); } catch { }
                return;
            }

            itemTop = topLeftFallback.Y;
            itemBottom = itemTop + (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0 ? btn.RenderSize.Height : btn.RenderSize.Height);

            const double fallbackMargin = 4.0;

            if (!sv.CanContentScroll)
            {
                if (itemTop < 0)
                {
                    double newOffset = Math.Max(0, sv.VerticalOffset + itemTop - fallbackMargin);
                    sv.ScrollToVerticalOffset(newOffset);

                    try
                    {
                        if (btn.Tag is int idx2 && idx2 >= 0)
                        {
                            double approxItemHeight = (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0) ? btn.RenderSize.Height : btn.ActualHeight;
                            double spacing = btn.Margin.Top + btn.Margin.Bottom;
                            double targetOffset = Math.Max(0, idx2 * (approxItemHeight + spacing) - fallbackMargin);
                            if (Math.Abs(sv.VerticalOffset - targetOffset) > 1.0)
                                sv.ScrollToVerticalOffset(targetOffset);
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"Index-based fallback failed: {ex.Message}"); }
                }
                else if (itemBottom > sv.ViewportHeight)
                {
                    double delta = itemBottom - sv.ViewportHeight + fallbackMargin;
                    double newOffset = Math.Min(sv.ExtentHeight - sv.ViewportHeight, sv.VerticalOffset + delta);
                    sv.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    try { btn.BringIntoView(); } catch { }
                }
            }
            else
            {
                try
                {
                    if (btn.Tag is int idx3 && idx3 >= 0)
                    {
                        double currentLogical = sv.VerticalOffset;
                        if (idx3 < (int)currentLogical)
                        {
                            sv.ScrollToVerticalOffset(Math.Max(0, idx3 - 1));
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Logical fallback failed: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TryScrollIntoView exception: {ex.Message}");
            try { btn.BringIntoView(); } catch { }
        }
    }
}
