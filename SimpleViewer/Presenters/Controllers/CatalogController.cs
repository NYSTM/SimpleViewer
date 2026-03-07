using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer.Presenters.Controllers;

/// <summary>
/// カタログ表示（サムネイル一覧）の生成と表示制御を担当するコントローラークラス。
/// ThumbnailController で既に生成されたサムネイルを再利用して高速化します。
/// </summary>
public class CatalogController : IDisposable
{
    private static readonly Brush SelectedThumbnailBorderBrush = new SolidColorBrush(Color.FromRgb(249, 115, 22));
    private static readonly Brush SelectedThumbnailBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 237, 213));
    private static readonly Brush HoverThumbnailBorderBrush = new SolidColorBrush(Color.FromRgb(251, 146, 60));
    private static readonly Brush HoverThumbnailBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 247, 237));

    private readonly Panel _catalogPanel;
    private readonly FrameworkElement _catalogOverlay;
    private readonly Func<int> _getPageMaximum;
    private readonly Func<int> _getCurrentPageIndex;
    private readonly Func<int, int, CancellationToken, Task<BitmapSource?>> _getThumbnailAsync;
    private readonly Func<int, Task> _jumpToPageAsync;
    private readonly Style _thumbButtonStyle;
    private readonly Dispatcher _dispatcher;
    private readonly Action _focusWindow;
    private readonly ThumbnailController? _thumbnailController;

    private CancellationTokenSource? _cts;

    public CatalogController(
        Panel catalogPanel,
        FrameworkElement catalogOverlay,
        Func<int> getPageMaximum,
        Func<int> getCurrentPageIndex,
        Func<int, int, CancellationToken, Task<BitmapSource?>> getThumbnailAsync,
        Func<int, Task> jumpToPageAsync,
        Style thumbButtonStyle,
        Dispatcher dispatcher,
        Action focusWindow,
        ThumbnailController? thumbnailController = null)
    {
        _catalogPanel = catalogPanel ?? throw new ArgumentNullException(nameof(catalogPanel));
        _catalogOverlay = catalogOverlay ?? throw new ArgumentNullException(nameof(catalogOverlay));
        _getPageMaximum = getPageMaximum ?? throw new ArgumentNullException(nameof(getPageMaximum));
        _getCurrentPageIndex = getCurrentPageIndex ?? throw new ArgumentNullException(nameof(getCurrentPageIndex));
        _getThumbnailAsync = getThumbnailAsync ?? throw new ArgumentNullException(nameof(getThumbnailAsync));
        _jumpToPageAsync = jumpToPageAsync ?? throw new ArgumentNullException(nameof(jumpToPageAsync));
        _thumbButtonStyle = thumbButtonStyle ?? new Style(typeof(Button));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _focusWindow = focusWindow ?? throw new ArgumentNullException(nameof(focusWindow));
        _thumbnailController = thumbnailController;
    }

    /// <summary>
    /// カタログが現在表示されているかどうかを返します。
    /// </summary>
    public bool IsVisible => _catalogOverlay.Visibility == Visibility.Visible;

    /// <summary>
    /// カタログの表示/非表示を切り替えます。
    /// 表示中は Close() を呼び、非表示時は ShowAsync() を呼び出します。
    /// </summary>
    public async Task ToggleAsync()
    {
        if (IsVisible)
        {
            Close();
        }
        else
        {
            await ShowAsync();
        }
    }

    /// <summary>
    /// カタログを表示し、ページごとにサムネイルを非同期で生成して UI に追加します。
    /// ThumbnailController で既に生成されたサムネイルを優先的に使用して高速化します。
    /// </summary>
    public async Task ShowAsync()
    {
        CancelAndDispose();

        _dispatcher.Invoke(() =>
        {
            _catalogOverlay.Visibility = Visibility.Visible;
            _catalogPanel.Children.Clear();
        });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        int max = _getPageMaximum();
        int currentPageIndex = Math.Clamp(_getCurrentPageIndex(), 0, Math.Max(0, max));

        if (_thumbnailController != null)
        {
            var existingIndices = _thumbnailController.GetExistingThumbnailIndices().ToList();

            foreach (var i in existingIndices)
            {
                if (token.IsCancellationRequested) return;
                if (i > max) break;

                var thumb = _thumbnailController.GetExistingThumbnail(i);
                if (thumb != null)
                {
                    var item = CreateThumbnailElement(thumb, i, 180, i == currentPageIndex);
                    _dispatcher.Invoke(() =>
                    {
                        if (_catalogOverlay.Visibility == Visibility.Visible)
                        {
                            _catalogPanel.Children.Add(item);
                        }
                    });
                }

                if (i % 20 == 0) await Task.Yield();
            }
        }

        var existingSet = _thumbnailController != null
            ? new HashSet<int>(_thumbnailController.GetExistingThumbnailIndices())
            : [];

        for (int i = 0; i <= max; i++)
        {
            if (token.IsCancellationRequested) return;
            if (existingSet.Contains(i)) continue;

            BitmapSource? thumb = null;
            try
            {
                thumb = await _getThumbnailAsync(i, 200, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                thumb = null;
            }

            if (token.IsCancellationRequested) return;

            if (thumb != null)
            {
                var item = CreateThumbnailElement(thumb, i, 180, i == currentPageIndex);
                _dispatcher.Invoke(() =>
                {
                    if (_catalogOverlay.Visibility == Visibility.Visible)
                    {
                        _catalogPanel.Children.Add(item);
                    }
                });
            }

            if (i % 10 == 0) await Task.Yield();
        }
    }

    /// <summary>
    /// カタログを閉じ、生成中のタスクをキャンセルし、UI を閉じる。
    /// </summary>
    public void Close()
    {
        CancelAndDispose();
        _dispatcher.Invoke(() => _catalogOverlay.Visibility = Visibility.Collapsed);
    }

    /// <summary>
    /// サムネイルを表示するための UI 要素（ボタン）を生成します。
    /// クリック時には該当ページにジャンプし、カタログを閉じてウィンドウにフォーカスを戻します。
    /// </summary>
    private Button CreateThumbnailElement(BitmapSource source, int index, double width, bool isSelected)
    {
        var thumbnailBorder = new Border
        {
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(3),
            BorderBrush = isSelected ? SelectedThumbnailBorderBrush : Brushes.Transparent,
            Background = isSelected ? SelectedThumbnailBackgroundBrush : Brushes.Transparent,
            Child = new Image { Source = source, Width = width }
        };

        var btn = new Button
        {
            Content = thumbnailBorder,
            Tag = index,
            Margin = new Thickness(4),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Focusable = false,
            Style = _thumbButtonStyle
        };

        btn.MouseEnter += (_, _) =>
        {
            if (isSelected) return;
            thumbnailBorder.BorderBrush = HoverThumbnailBorderBrush;
            thumbnailBorder.Background = HoverThumbnailBackgroundBrush;
        };

        btn.MouseLeave += (_, _) =>
        {
            if (isSelected) return;
            thumbnailBorder.BorderBrush = Brushes.Transparent;
            thumbnailBorder.Background = Brushes.Transparent;
        };

        btn.Click += async (s, _) =>
        {
            try
            {
                await _jumpToPageAsync((int)btn.Tag!);
            }
            catch { }
            _dispatcher.Invoke(() => _catalogOverlay.Visibility = Visibility.Collapsed);
            _focusWindow();
        };

        return btn;
    }

    /// <summary>
    /// 既存の CancellationTokenSource をキャンセル・破棄するユーティリティ。
    /// </summary>
    private void CancelAndDispose()
    {
        if (_cts != null)
        {
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
            _cts = null;
        }
    }

    /// <summary>
    /// リソースを解放（現在はキャンセルのみ）。
    /// </summary>
    public void Dispose()
    {
        CancelAndDispose();
    }
}
