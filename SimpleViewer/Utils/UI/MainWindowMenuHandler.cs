using SimpleViewer.Models;
using SimpleViewer.Presenters;
using System.Windows;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// MainWindowのメニューおよびボタンイベント処理を担当するクラス。
/// ファイル操作、表示モード切替、ズーム操作などのイベントハンドラを集約します。
/// </summary>
public class MainWindowMenuHandler
{
    private readonly SimpleViewerPresenter _presenter;
    private readonly WindowCoordinator _coordinator;
    private readonly FileOpenHandler _fileOpenHandler;
    private readonly Window _window;
    private readonly Func<Task> _openFileDialogCallback;
    private readonly Action _clearUiCallback;
    private readonly Func<Size> _getViewSizeFunc;
    private readonly Func<Size> _getContentSizeFunc;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="presenter">Presenterインスタンス</param>
    /// <param name="coordinator">WindowCoordinator</param>
    /// <param name="fileOpenHandler">ファイルオープンハンドラー</param>
    /// <param name="window">対象ウィンドウ</param>
    /// <param name="openFileDialogCallback">ファイルダイアログを開くコールバック</param>
    /// <param name="clearUiCallback">UIクリアのコールバック</param>
    /// <param name="getViewSizeFunc">ビューサイズ取得関数</param>
    /// <param name="getContentSizeFunc">コンテンツサイズ取得関数</param>
    public MainWindowMenuHandler(
        SimpleViewerPresenter presenter,
        WindowCoordinator coordinator,
        FileOpenHandler fileOpenHandler,
        Window window,
        Func<Task> openFileDialogCallback,
        Action clearUiCallback,
        Func<Size> getViewSizeFunc,
        Func<Size> getContentSizeFunc)
    {
        _presenter = presenter;
        _coordinator = coordinator;
        _fileOpenHandler = fileOpenHandler;
        _window = window;
        _openFileDialogCallback = openFileDialogCallback;
        _clearUiCallback = clearUiCallback;
        _getViewSizeFunc = getViewSizeFunc;
        _getContentSizeFunc = getContentSizeFunc;
    }

    /// <summary>
    /// ファイルを開くメニュークリック時の処理
    /// </summary>
    public async Task HandleMenuOpenClickAsync()
    {
        await _openFileDialogCallback();
    }

    /// <summary>
    /// ファイルを閉じるメニュークリック時の処理
    /// </summary>
    public async Task HandleMenuCloseClickAsync()
    {
        _clearUiCallback();
        await _presenter.CloseSourceAsync();
        _fileOpenHandler.ClearCurrentSource();
    }

    /// <summary>
    /// 終了メニュークリック時の処理
    /// </summary>
    public void HandleMenuExitClick()
    {
        _window.Close();
    }

    /// <summary>
    /// 表示モード切替メニュークリック時の処理
    /// </summary>
    public async Task HandleMenuToggleModeClickAsync()
    {
        await _presenter.ToggleDisplayModeAsync();
    }

    /// <summary>
    /// 幅に合わせるメニュークリック時の処理
    /// </summary>
    public void HandleMenuFitWidthClick()
    {
        _coordinator.ZoomManager.SetMode(ZoomMode.FitWidth, _getViewSizeFunc(), _getContentSizeFunc());
    }

    /// <summary>
    /// ページに合わせるメニュークリック時の処理
    /// </summary>
    public void HandleMenuFitPageClick()
    {
        _coordinator.ZoomManager.SetMode(ZoomMode.FitPage, _getViewSizeFunc(), _getContentSizeFunc());
    }

    /// <summary>
    /// ズームリセットメニュークリック時の処理
    /// </summary>
    public void HandleMenuResetZoomClick()
    {
        _coordinator.ZoomManager.ResetZoom();
    }

    /// <summary>
    /// ズームインメニュークリック時の処理
    /// </summary>
    public void HandleMenuZoomInClick()
    {
        _coordinator.ZoomManager.ZoomIn();
    }

    /// <summary>
    /// ズームアウトメニュークリック時の処理
    /// </summary>
    public void HandleMenuZoomOutClick()
    {
        _coordinator.ZoomManager.ZoomOut();
    }

    /// <summary>
    /// サイドバー切替メニュークリック時の処理
    /// </summary>
    public void HandleMenuToggleSidebarClick()
    {
        _coordinator.ToggleSidebar();
    }

    /// <summary>
    /// カタログ切替メニュークリック時の処理
    /// </summary>
    public async Task HandleMenuCatalogClickAsync()
    {
        await _coordinator.ToggleCatalogAsync();
    }

    /// <summary>
    /// カタログを閉じるボタンクリック時の処理
    /// </summary>
    public void HandleCloseCatalogClick()
    {
        _coordinator.CloseCatalog();
    }

    /// <summary>
    /// EXIF Orientation適用設定の切替処理
    /// </summary>
    /// <param name="isChecked">チェック状態</param>
    public async Task HandleApplyExifOrientationToggleAsync(bool isChecked)
    {
        await _coordinator.UpdateApplyExifOrientationAsync(isChecked);
    }
}
