using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// スクロール操作を担当するヘルパークラス。
/// ScrollViewer の検索とスクロール処理を分離して再利用可能にします。
/// </summary>
public class ScrollHelper
{
    private readonly Dispatcher _dispatcher;
    private DateTime _lastScrollTime = DateTime.MinValue;
    private const int ScrollDebounceMs = 100;

    /// <summary>
    /// ScrollHelper を初期化します。
    /// </summary>
    /// <param name="dispatcher">UI スレッド用 Dispatcher</param>
    public ScrollHelper(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 指定のボタンを ScrollViewer 内にスクロールして可視化します。
    /// </summary>
    /// <param name="btn">可視化するボタン</param>
    /// <param name="container">スクロールコンテナ（ItemsControl など）</param>
    public void TryScrollIntoView(Button btn, DependencyObject container)
    {
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _lastScrollTime).TotalMilliseconds < ScrollDebounceMs)
            {
                return;
            }

            if (!btn.IsLoaded)
            {
                return;
            }

            var sv = FindScrollViewer(container);
            if (sv == null)
            {
                return;
            }

            if (!sv.CanContentScroll && sv.ViewportHeight < 50)
            {
                _ = RetryScrollIntoViewAsync(btn, sv, 0);
                return;
            }

            PerformScroll(btn, sv);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ScrollHelper.TryScrollIntoView] 例外: {ex.Message}");
        }
    }

    /// <summary>
    /// ViewportHeight が小さい場合にリトライする
    /// </summary>
    private async Task RetryScrollIntoViewAsync(Button btn, ScrollViewer sv, int retryCount)
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
            if (!btn.IsLoaded)
            {
                return;
            }

            if (sv.ViewportHeight < 50)
            {
                _ = RetryScrollIntoViewAsync(btn, sv, retryCount + 1);
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
            if (sv.CanContentScroll)
            {
                btn.BringIntoView();
                _lastScrollTime = DateTime.UtcNow;
                return;
            }

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
            Debug.WriteLine($"[ScrollHelper.PerformScroll] 例外: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定要素から親の ScrollViewer を探索します（再帰）。
    /// </summary>
    /// <param name="start">探索開始要素</param>
    /// <returns>見つかった ScrollViewer、なければ null</returns>
    public static ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        if (start == null) return null;
        if (start is ScrollViewer sv) return sv;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(start); i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }

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
    /// child が ancestor の子孫であるかをチェックします。
    /// </summary>
    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
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
