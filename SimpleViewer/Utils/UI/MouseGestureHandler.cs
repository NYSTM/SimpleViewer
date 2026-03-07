using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// マウスジェスチャーを処理するクラス。
/// ドラッグスクロール、クリック領域判定によるページ遷移を担当します。
/// </summary>
public class MouseGestureHandler
{
    private readonly ScrollViewer _scrollViewer;
    private readonly Func<Task> _nextPageCallback;
    private readonly Func<Task> _previousPageCallback;

    // ドラッグスクロール用の状態
    private Point _dragStartPoint;
    private double _scrollHorizontalOffset;
    private double _scrollVerticalOffset;
    private bool _isDragging;

    // クリック判定のしきい値（ピクセル）
    private const double ClickThreshold = 5.0;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="scrollViewer">対象のScrollViewer</param>
    /// <param name="nextPageCallback">次ページへ移動するコールバック</param>
    /// <param name="previousPageCallback">前ページへ移動するコールバック</param>
    public MouseGestureHandler(
        ScrollViewer scrollViewer,
        Func<Task> nextPageCallback,
        Func<Task> previousPageCallback)
    {
        _scrollViewer = scrollViewer ?? throw new ArgumentNullException(nameof(scrollViewer));
        _nextPageCallback = nextPageCallback ?? throw new ArgumentNullException(nameof(nextPageCallback));
        _previousPageCallback = previousPageCallback ?? throw new ArgumentNullException(nameof(previousPageCallback));
    }

    /// <summary>
    /// マウス左ボタン押下時の処理を行います。
    /// </summary>
    /// <param name="e">マウスイベント引数</param>
    /// <param name="window">親ウィンドウ</param>
    public void HandleMouseLeftButtonDown(MouseButtonEventArgs e, Window window)
    {
        // ScrollBar上のクリックは無視
        if (e.OriginalSource is DependencyObject dep && IsDescendantOfScrollBar(dep))
        {
            return;
        }

        _dragStartPoint = e.GetPosition(window);
        _scrollHorizontalOffset = _scrollViewer.HorizontalOffset;
        _scrollVerticalOffset = _scrollViewer.VerticalOffset;
        _isDragging = false;

        _scrollViewer.CaptureMouse();
        window.Cursor = Cursors.SizeAll;
    }

    /// <summary>
    /// マウス移動時の処理を行います。
    /// </summary>
    /// <param name="e">マウスイベント引数</param>
    /// <param name="window">親ウィンドウ</param>
    public void HandleMouseMove(MouseEventArgs e, Window window)
    {
        if (!_scrollViewer.IsMouseCaptured)
        {
            return;
        }

        Vector delta = _dragStartPoint - e.GetPosition(window);

        // しきい値を超えたらドラッグ開始
        if (!_isDragging && delta.Length > ClickThreshold)
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            _scrollViewer.ScrollToHorizontalOffset(_scrollHorizontalOffset + delta.X);
            _scrollViewer.ScrollToVerticalOffset(_scrollVerticalOffset + delta.Y);
        }
    }

    /// <summary>
    /// マウス左ボタン離上時の処理を行います。
    /// </summary>
    /// <param name="e">マウスイベント引数</param>
    /// <param name="window">親ウィンドウ</param>
    public async Task HandleMouseLeftButtonUpAsync(MouseButtonEventArgs e, Window window)
    {
        try
        {
            // ScrollBar上のクリックは無視
            if (e.OriginalSource is DependencyObject dep && IsDescendantOfScrollBar(dep))
            {
                return;
            }

            bool wasCapturing = _scrollViewer.IsMouseCaptured;

            if (wasCapturing)
            {
                _scrollViewer.ReleaseMouseCapture();
                window.Cursor = Cursors.Arrow;
            }

            // 移動距離を計算
            var currentPosition = e.GetPosition(window);
            double movedDistance = (_dragStartPoint - currentPosition).Length;

            // クリックと判定
            if (movedDistance <= ClickThreshold)
            {
                await HandleClickAsync(e);
            }
        }
        catch
        {
            // エラーは無視
        }
        finally
        {
            _isDragging = false;
        }
    }

    /// <summary>
    /// クリック処理を行います。
    /// 左端20%でページ進む、右端20%でページ戻る
    /// </summary>
    /// <param name="e">マウスイベント引数</param>
    private async Task HandleClickAsync(MouseButtonEventArgs e)
    {
        var relativePosition = e.GetPosition(_scrollViewer);
        double x = relativePosition.X;
        double width = _scrollViewer.ActualWidth;

        if (width <= 0)
        {
            return;
        }

        double ratio = x / width;

        if (ratio <= 0.2)
        {
            // 左端20%: 次ページへ
            await _nextPageCallback();
        }
        else if (ratio >= 0.8)
        {
            // 右端20%: 前ページへ
            await _previousPageCallback();
        }
    }

    /// <summary>
    /// 指定要素がScrollBarの子孫かどうかを判定します。
    /// </summary>
    /// <param name="element">判定する要素</param>
    /// <returns>ScrollBarの子孫ならtrue</returns>
    private bool IsDescendantOfScrollBar(DependencyObject element)
    {
        while (element != null)
        {
            if (element is ScrollBar)
            {
                return true;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
}
