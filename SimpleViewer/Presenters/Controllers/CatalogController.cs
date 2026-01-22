using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer.Presenters.Controllers;

/// <summary>
/// カタログ表示（サムネイル一覧）の生成と表示制御を担当するコントローラークラス。
/// - サムネイル読み込みは非同期で実行し、UI 更新は必ず Dispatcher 経由で行う。
/// - 表示中の生成処理は CancellationToken によるキャンセル可能とし、不要な処理を中断する。
/// - MainWindow や UI 要素への直接依存は最小限に抑え、テストしやすくする。
/// </summary>
public class CatalogController : IDisposable
{
    private readonly Panel _catalogPanel;
    private readonly FrameworkElement _catalogOverlay;
    private readonly Func<int> _getPageMaximum;
    private readonly Func<int, int, CancellationToken, Task<BitmapSource?>> _getThumbnailAsync;
    private readonly Func<int, Task> _jumpToPageAsync;
    private readonly Style _thumbButtonStyle;
    private readonly Dispatcher _dispatcher;
    private readonly Action _focusWindow;

    // 表示中のサムネイル生成をキャンセルするための CTS
    private CancellationTokenSource? _cts;

    public CatalogController(
        Panel catalogPanel,
        FrameworkElement catalogOverlay,
        Func<int> getPageMaximum,
        Func<int, int, CancellationToken, Task<BitmapSource?>> getThumbnailAsync,
        Func<int, Task> jumpToPageAsync,
        Style thumbButtonStyle,
        Dispatcher dispatcher,
        Action focusWindow)
    {
        _catalogPanel = catalogPanel ?? throw new ArgumentNullException(nameof(catalogPanel));
        _catalogOverlay = catalogOverlay ?? throw new ArgumentNullException(nameof(catalogOverlay));
        _getPageMaximum = getPageMaximum ?? throw new ArgumentNullException(nameof(getPageMaximum));
        _getThumbnailAsync = getThumbnailAsync ?? throw new ArgumentNullException(nameof(getThumbnailAsync));
        _jumpToPageAsync = jumpToPageAsync ?? throw new ArgumentNullException(nameof(jumpToPageAsync));
        _thumbButtonStyle = thumbButtonStyle ?? new Style(typeof(Button));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _focusWindow = focusWindow ?? throw new ArgumentNullException(nameof(focusWindow));
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
    /// - 以前の生成処理があればキャンセルしてから開始します。
    /// - サムネイル生成はキャンセル可能で、失敗は無視して次に進みます（柔軟性）。
    /// - UI 更新は Dispatcher.Invoke を使って UI スレッドで行います。
    /// </summary>
    public async Task ShowAsync()
    {
        // 以前の処理をキャンセルしてから開始
        CancelAndDispose();

        // カタログ UI を表示してパネルをクリア（UI スレッドで実行）
        _dispatcher.Invoke(() =>
        {
            _catalogOverlay.Visibility = Visibility.Visible;
            _catalogPanel.Children.Clear();
        });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        int max = _getPageMaximum();
        // 0..max の範囲でサムネイルを生成していく
        for (int i = 0; i <= max; i++)
        {
            if (token.IsCancellationRequested) return; // キャンセル確認
            BitmapSource? thumb = null;
            try
            {
                // サムネイル生成は外部サービスに委譲し、キャンセル可能にする
                thumb = await _getThumbnailAsync(i, 200, token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                // 柔軟性: サムネイル取得失敗はログに出力せず次に進む
                thumb = null;
            }

            // 取得後にもキャンセルを再確認し、不要な追加を防ぐ
            if (token.IsCancellationRequested) return;

            if (thumb != null)
            {
                var item = CreateThumbnailElement(thumb, i, 180);
                // UI 更新は Dispatcher 経由で行う
                _dispatcher.Invoke(() =>
                {
                    // 再度カタログが表示中か確認してから追加
                    if (_catalogOverlay.Visibility == Visibility.Visible)
                    {
                        _catalogPanel.Children.Add(item);
                    }
                });
            }

            // 大量のページがある場合に UI スレッドがスターベーションしないよう適宜 Yield
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
            Style = _thumbButtonStyle
        };

        // クリック時の非同期ハンドラ。UI スレッドで安全に呼ばれる
        btn.Click += async (s, _) =>
        {
            try
            {
                await _jumpToPageAsync((int)btn.Tag!);
            }
            catch { }
            // カタログを閉じてフォーカスを戻す
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
