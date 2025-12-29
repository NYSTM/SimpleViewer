using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;

namespace SimpleViewer.Utils
{
    // サムネイル表示と操作を管理するコントローラ
    public class ThumbnailController
    {
        // Presenter（サムネイル取得や画像操作を担当）
        private readonly SimpleViewerPresenter _presenter;
        // サムネイルを表示する ItemsControl
        private readonly ItemsControl _thumbnailSidebar;
        // UI スレッド用 Dispatcher
        private readonly Dispatcher _dispatcher;
        // サムネイルやツリーからページ移動するためのコールバック
        private readonly Func<int, Task> _jumpToPageCallback;
        // 操作後にウィンドウへフォーカスを戻すコールバック
        private readonly Action _focusWindowCallback;
        // サムネイルボタンに適用するスタイル
        private readonly Style _thumbnailButtonStyle;

        // インデックス -> Button のマップ（サイドバー内の項目管理）
        private readonly Dictionary<int, Button> _sidebarItems = new();
        // 最後にハイライトしたインデックス（未ハイライト: -1）
        private int _lastHighlightedIndex = -1;

        // Build/Refresh のキャンセル制御用
        private CancellationTokenSource? _buildCts;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="presenter">サムネイル取得等を行う Presenter</param>
        /// <param name="thumbnailSidebar">サムネイルを表示する ItemsControl</param>
        /// <param name="dispatcher">UI 更新用の Dispatcher</param>
        /// <param name="jumpToPageCallback">ページジャンプ用のコールバック</param>
        /// <param name="focusWindowCallback">操作後にウィンドウへフォーカスを戻すコールバック</param>
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
        /// サムネイルを構築して ItemsControl に追加する非同期処理。
        /// 既存項目はクリアされ、新たに Presenter からサムネイルを取得して追加する。
        /// キャンセル可能。
        /// </summary>
        /// <param name="totalPages">総ページ数</param>
        /// <param name="currentPageIndex">現在のページインデックス（ハイライト対象）</param>
        /// <param name="desiredWidth">希望するサムネイル幅（ピクセル）</param>
        public async Task BuildAsync(int totalPages, int currentPageIndex, double desiredWidth)
        {
            Clear();

            _buildCts?.Cancel();
            _buildCts = new CancellationTokenSource();
            var token = _buildCts.Token;

            try
            {
                for (int i = 0; i < totalPages; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var thumb = await _presenter.GetThumbnailAsync(i, (int)Math.Round(desiredWidth), token);

                    token.ThrowIfCancellationRequested();

                    if (thumb != null)
                    {
                        var item = CreateThumbnailElement(thumb, i, desiredWidth);

                        _dispatcher.Invoke(() =>
                        {
                            _thumbnailSidebar.Items.Add(item);
                            _sidebarItems[i] = item;

                            if (i == currentPageIndex)
                            {
                                Highlight(i);
                            }
                        });
                    }

                    if (i % 5 == 0) await Task.Yield();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"ThumbnailController.BuildAsync Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 指定インデックスのサムネイルをハイライトする。
        /// 前回のハイライトを解除し、必要に応じてスクロールで表示領域へ持ってくる。
        /// </summary>
        /// <param name="index">ハイライトするインデックス</param>
        public void Highlight(int index)
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                // 前回のハイライト解除
                if (_lastHighlightedIndex != -1 && _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
                    oldBtn.BorderBrush = Brushes.Transparent;

                // レイアウトを更新して要素が生成されるのを待つ
                _thumbnailSidebar.UpdateLayout();
                if (_sidebarItems.TryGetValue(index, out var currentBtn))
                {
                    currentBtn.BorderBrush = SystemColors.HighlightBrush;

                    // 仮想化された ItemsControl 内では BringIntoView が期待どおりに動かないことがあるため
                    // 内部の ScrollViewer を取得して明示的にオフセットを調整する
                    TryScrollIntoView(currentBtn);

                    _lastHighlightedIndex = index;
                }
            }, DispatcherPriority.Render);
        }

        /// <summary>
        /// サイドバーをクリアする。構築中のタスクはキャンセルされ、UI 上の項目を消去する。
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
        }

        /// <summary>
        /// サムネイル要素を作成するユーティリティ
        /// </summary>
        /// <param name="source">サムネイル画像の BitmapSource</param>
        /// <param name="index">対応するページインデックス</param>
        /// <param name="width">サムネイルの幅</param>
        /// <returns>作成されたサムネイル用ボタン</returns>
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
        /// 現在表示されているサムネイルを高解像度版に置き換える。
        /// 幅を指定して Presenter から再取得する。キャンセル可能。
        /// </summary>
        /// <param name="width">再取得するサムネイル幅（ピクセル）</param>
        /// <param name="token">キャンセル用トークン</param>
        public async Task RefreshAsync(int width, CancellationToken token)
        {
            try
            {
                var indices = _sidebarItems.Keys.ToList();
                foreach (var idx in indices)
                {
                    token.ThrowIfCancellationRequested();
                    var thumb = await _presenter.GetThumbnailAsync(idx, width, token);
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"ThumbnailController.RefreshAsync Error: {ex.Message}");
            }
        }

        /// <summary>
        /// ItemsControl の内部にある ScrollViewer を再帰的に検索して返す。
        /// </summary>
        /// <param name="start">探索開始ノード</param>
        /// <returns>見つかった ScrollViewer、無ければ null</returns>
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
        /// ItemsControl 内の ItemsPanel を検索して返す（VirtualizingStackPanel または StackPanel を期待する）。
        /// </summary>
        /// <param name="start">探索開始ノード</param>
        /// <returns>見つかった Panel、無ければ null</returns>
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
        /// 指定したボタンがスクロール領域内に収まるように ScrollViewer のオフセットを調整する。
        /// 仮想化や論理スクロールに対応する複数のフォールバックを持つ。
        /// </summary>
        /// <param name="btn">対象のサムネイルボタン</param>
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

                // 1) 仮想化パネルがあれば IScrollInfo を使って MakeVisible を試す
                if (panel is VirtualizingStackPanel vsp)
                {
                    try
                    {
                        // MakeVisible がスクロールするためにゼロでない矩形高さを確保する
                        double rectHeight = btn.RenderSize.Height;
                        if (rectHeight <= 0)
                        {
                            // 実際にレンダリングされている他のアイテムの高さを探す
                            var other = _sidebarItems.Values.FirstOrDefault(x => x != btn && x.RenderSize.Height > 0);
                            if (other != null) rectHeight = other.RenderSize.Height;
                            if (rectHeight <= 0) rectHeight = 48; // 合理的なデフォルト
                        }

                        var rect = new Rect(new Point(0, 0), new Size(btn.RenderSize.Width > 0 ? btn.RenderSize.Width : _thumbnailSidebar.ActualWidth, rectHeight));
                        Debug.WriteLine($"Trying MakeVisible idx={idx} rect={rect}");
                        vsp.MakeVisible(btn, rect);
                        Debug.WriteLine($"MakeVisible done idx={idx}");

                        // ScrollViewer が論理スクロールを使用している場合 (CanContentScroll==true)、VerticalOffset はアイテム単位になる。
                        // MakeVisible が直ちに ScrollViewer のオフセットを更新しないことがあるため、必要なら論理オフセットを強制する
                        if (sv != null && sv.CanContentScroll && idx >= 0)
                        {
                            // 項目をできるだけ上部に表示するように調整（少し余裕をもたせる）
                            double logicalTarget = Math.Max(0, idx - 1);
                            Debug.WriteLine($"Forcing logical scroll to {logicalTarget} (idx={idx})");
                            sv.ScrollToVerticalOffset(logicalTarget);
                        }

                        // レイアウトや仮想化が完了した後に表示状態を検証し、必要なら強制スクロールを行う
                        _dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                var sv2 = FindScrollViewer(_thumbnailSidebar);
                                var panel2 = FindItemsPanel(_thumbnailSidebar);
                                if (sv2 == null) return;

                                // panel2 基準でのアイテム位置を計算する
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

                                        Debug.WriteLine($"Post-MakeVisible panel check idx={idx} itemTop={itemTop2} viewTop={viewTop2} viewBottom={viewBottom2}");

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
                                            // 論理スクロールの場合: インデックスが現在の表示範囲内かを確認する
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

                                // フォールバック: ScrollViewer に対する座標変換で位置を確認
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

                // レイアウトを最新化してから計算する
                sv.UpdateLayout();
                btn.UpdateLayout();

                double itemTop, itemBottom;

                // まずは items panel（content）基準で座標を取得して、ScrollViewer の VerticalOffset と比較する
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

                        // content 基準でスクロールオフセットと比較
                        double viewTop = sv.VerticalOffset;
                        double viewBottom = sv.VerticalOffset + sv.ViewportHeight;

                        Debug.WriteLine($"panel-based idx={idx} itemTop={itemTop} itemBottom={itemBottom} viewTop={viewTop} viewBottom={viewBottom} extent={sv.ExtentHeight}");

                        const double margin = 4.0;

                        if (!sv.CanContentScroll)
                        {
                            if (itemTop < viewTop)
                            {
                                // 上にはみ出している -> スクロールアップ
                                double newOffset = Math.Max(0, itemTop - margin);
                                Debug.WriteLine($"Scrolling up to {newOffset} (panel-based)");
                                sv.ScrollToVerticalOffset(newOffset);
                                return;
                            }
                            else if (itemBottom > viewBottom)
                            {
                                // 下にはみ出している -> スクロールダウン
                                double newOffset = Math.Min(sv.ExtentHeight - sv.ViewportHeight, itemBottom - sv.ViewportHeight + margin);
                                Debug.WriteLine($"Scrolling down to {newOffset} (panel-based)");
                                sv.ScrollToVerticalOffset(newOffset);
                                return;
                            }

                            Debug.WriteLine("Item already visible (panel-based)");
                            try { btn.BringIntoView(); } catch { }
                            return;
                        }
                        else
                        {
                            // 論理スクロール: インデックスで比較して表示範囲かどうかを判断する
                            try
                            {
                                double currentLogical = sv.VerticalOffset;
                                Debug.WriteLine($"Logical scrolling: currentLogical={currentLogical} idx={idx}");
                                if (idx < (int)currentLogical)
                                {
                                    sv.ScrollToVerticalOffset(Math.Max(0, idx - 1));
                                    Debug.WriteLine($"Logical scroll up to {Math.Max(0, idx - 1)} idx={idx}");
                                    return;
                                }
                                else if (idx > (int)(currentLogical + sv.ViewportHeight))
                                {
                                    // 表示範囲の下にある -> 下方向へスクロール
                                    sv.ScrollToVerticalOffset(Math.Max(0, idx - (int)sv.ViewportHeight + 1));
                                    Debug.WriteLine($"Logical scroll down idx={idx}");
                                    return;
                                }

                                Debug.WriteLine("Item already visible (logical)");
                                try { btn.BringIntoView(); } catch { }
                                return;
                            }
                            catch (Exception ex) { Debug.WriteLine($"Logical scroll branch failed: {ex.Message}"); }
                        }
                    }
                }

                // panel 基準の取得ができなければ従来の sv 基準へフォールバック
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
                itemBottom = itemTop + (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0 ? btn.RenderSize.Height : btn.ActualHeight);

                Debug.WriteLine($"sv-based idx={idx} itemTop={itemTop} itemBottom={itemBottom} viewportHeight={sv.ViewportHeight} offset={sv.VerticalOffset} extent={sv.ExtentHeight}");

                const double fallbackMargin = 4.0;

                if (!sv.CanContentScroll)
                {
                    if (itemTop < 0)
                    {
                        double newOffset = Math.Max(0, sv.VerticalOffset + itemTop - fallbackMargin);
                        Debug.WriteLine($"Scrolling up to {newOffset} (sv-based)");
                        sv.ScrollToVerticalOffset(newOffset);

                        // それでもだめなら index ベースで強制スクロール
                        try
                        {
                            if (btn.Tag is int idx2 && idx2 >= 0)
                            {
                                double approxItemHeight = (double.IsNaN(btn.ActualHeight) || btn.ActualHeight == 0) ? btn.RenderSize.Height : btn.ActualHeight;
                                double spacing = btn.Margin.Top + btn.Margin.Bottom;
                                double targetOffset = Math.Max(0, idx2 * (approxItemHeight + spacing) - fallbackMargin);
                                Debug.WriteLine($"Index-based fallback scrolling to {targetOffset}");
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
                        Debug.WriteLine($"Scrolling down to {newOffset} (sv-based)");
                        sv.ScrollToVerticalOffset(newOffset);
                    }
                    else
                    {
                        Debug.WriteLine("Item already visible (sv-based)");
                        try { btn.BringIntoView(); } catch { }
                    }
                }
                else
                {
                    // 論理スクロールのフォールバック
                    try
                    {
                        if (btn.Tag is int idx3 && idx3 >= 0)
                        {
                            double currentLogical = sv.VerticalOffset;
                            if (idx3 < (int)currentLogical)
                            {
                                sv.ScrollToVerticalOffset(Math.Max(0, idx3 - 1));
                                Debug.WriteLine($"Fallback logical scroll up to {Math.Max(0, idx3 - 1)} idx={idx3}");
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
}
