using SimpleViewer.Models;
using SimpleViewer.Models.Configuration;
using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Presenters;
using SimpleViewer.Presenters.Controllers;
using SimpleViewer.Presenters.Handlers;
using SimpleViewer.Services;
using SimpleViewer.Utils.Configuration;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// ウィンドウ内の各種コントローラ（Zoom、Sidebar、Catalog、Input、Settings）を統合管理するクラス。
/// - 各コントローラの初期化と相互連携を担当する
/// - UI要素への参照を一元管理して依存関係を整理する
/// - ウィンドウのライフサイクル（起動、終了）に関わる処理を統合する
/// </summary>
public class WindowCoordinator
{
    // 各種コントローラとマネージャ
    private readonly ZoomManager _zoomManager;
    private SettingsController? _settingsController;
    private SidebarManager? _sidebarManager;
    private InputHandler? _inputHandler;
    private CatalogController? _catalogController;
    private readonly CacheCleanupService _cacheCleanupService;
    private readonly SettingsManager _settingsManager;
    private SimpleViewerPresenter? _presenter;

    // UI要素への参照（遅延初期化用に保持）
    private readonly FrameworkElement _viewContainer;
    private readonly ScrollViewer _mainScrollViewer;
    private readonly Image _imageLeft;
    private readonly Image _imageRight;
    private readonly TextBlock _zoomText;
    private readonly ColumnDefinition _sidebarColumn;
    private readonly Window _window;
    private readonly string _settingsDirectory;
    private readonly ItemsControl _thumbnailSidebar;
    private readonly TreeView? _sidebarTreeView;
    private readonly Panel _catalogPanel;
    private readonly FrameworkElement _catalogOverlay;
    private readonly Slider _pageSlider;
    private readonly Dispatcher _dispatcher;
    private readonly Style _buttonStyle;
    private readonly Func<Task> _openFileCallback;
    private readonly Action _toggleSidebarCallback;
    private readonly Func<Size> _getViewSizeFunc;
    private readonly Func<Size> _getContentSizeFunc;
    private Action? _clearUICallback;
    private Action? _closeSourceCallback;

    /// <summary>
    /// ZoomManagerへの参照を取得します。
    /// </summary>
    public ZoomManager ZoomManager => _zoomManager;

    /// <summary>
    /// SidebarManagerへの参照を取得します。
    /// </summary>
    public SidebarManager SidebarManager => _sidebarManager ?? throw new InvalidOperationException("SidebarManager is not initialized. Call SetPresenter first.");

    /// <summary>
    /// CatalogControllerへの参照を取得します。
    /// </summary>
    public CatalogController CatalogController => _catalogController ?? throw new InvalidOperationException("CatalogController is not initialized. Call SetPresenter first.");

    /// <summary>
    /// InputHandlerへの参照を取得します。
    /// </summary>
    public InputHandler InputHandler => _inputHandler ?? throw new InvalidOperationException("InputHandler is not initialized. Call SetPresenter first.");

    /// <summary>
    /// WindowCoordinatorを初期化します。
    /// </summary>
    /// <param name="window">対象ウィンドウ</param>
    /// <param name="presenter">Presenter（nullの場合、SetPresenterで後から設定する必要があります）</param>
    /// <param name="viewContainer">ズーム対象のコンテナ</param>
    /// <param name="mainScrollViewer">メインスクロールビューア</param>
    /// <param name="imageLeft">左側画像</param>
    /// <param name="imageRight">右側画像</param>
    /// <param name="zoomText">ズーム率表示テキスト</param>
    /// <param name="sidebarColumn">サイドバー列定義</param>
    /// <param name="thumbnailSidebar">サムネイル表示領域</param>
    /// <param name="sidebarTreeView">ツリービュー</param>
    /// <param name="catalogPanel">カタログパネル</param>
    /// <param name="catalogOverlay">カタログオーバーレイ</param>
    /// <param name="pageSlider">ページスライダー</param>
    /// <param name="dispatcher">ディスパッチャー</param>
    /// <param name="buttonStyle">ボタンスタイル</param>
    /// <param name="openFileCallback">ファイルを開くコールバック</param>
    /// <param name="toggleSidebarCallback">サイドバー切替コールバック</param>
    /// <param name="clearUICallback">UIクリアコールバック</param>
    /// <param name="closeSourceCallback">ソースを閉じるコールバック</param>
    /// <param name="getViewSizeFunc">ビューサイズ取得関数</param>
    /// <param name="getContentSizeFunc">コンテンツサイズ取得関数</param>
    public WindowCoordinator(
        Window window,
        SimpleViewerPresenter? presenter,
        FrameworkElement viewContainer,
        ScrollViewer mainScrollViewer,
        Image imageLeft,
        Image imageRight,
        TextBlock zoomText,
        ColumnDefinition sidebarColumn,
        ItemsControl thumbnailSidebar,
        TreeView? sidebarTreeView,
        Panel catalogPanel,
        FrameworkElement catalogOverlay,
        Slider pageSlider,
        Dispatcher dispatcher,
        Style buttonStyle,
        Func<Task> openFileCallback,
        Action toggleSidebarCallback,
        Action? clearUICallback,
        Action? closeSourceCallback,
        Func<Size> getViewSizeFunc,
        Func<Size> getContentSizeFunc)
    {
        _window = window;
        _viewContainer = viewContainer;
        _mainScrollViewer = mainScrollViewer;
        _imageLeft = imageLeft;
        _imageRight = imageRight;
        _zoomText = zoomText;
        _sidebarColumn = sidebarColumn;
        _settingsDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _thumbnailSidebar = thumbnailSidebar;
        _sidebarTreeView = sidebarTreeView;
        _catalogPanel = catalogPanel;
        _catalogOverlay = catalogOverlay;
        _pageSlider = pageSlider;
        _dispatcher = dispatcher;
        _buttonStyle = buttonStyle;
        _openFileCallback = openFileCallback;
        _toggleSidebarCallback = toggleSidebarCallback;
        _clearUICallback = clearUICallback;
        _closeSourceCallback = closeSourceCallback;
        _getViewSizeFunc = getViewSizeFunc;
        _getContentSizeFunc = getContentSizeFunc;

        // キャッシュクリーンアップサービスの初期化
        _cacheCleanupService = new CacheCleanupService(AppDomain.CurrentDomain.BaseDirectory);

        // SettingsManagerの初期化
        _settingsManager = new SettingsManager(_settingsDirectory);

        // ZoomManagerの初期化
        _zoomManager = new ZoomManager();
        _zoomManager.ZoomChanged += (s, e) => OnZoomChanged();

        // Presenterが提供されている場合、すべてのコンポーネントを初期化
        if (presenter != null)
        {
            _presenter = presenter;
            InitializeComponents(presenter);
        }
    }

    /// <summary>
    /// Presenterを設定します（初期化時にPresenterが未設定の場合に使用）。
    /// </summary>
    /// <param name="presenter">設定するPresenter</param>
    public void SetPresenter(SimpleViewerPresenter presenter)
    {
        _presenter = presenter;
        if (_sidebarManager == null || _catalogController == null || _inputHandler == null)
        {
            InitializeComponents(presenter);
        }
    }

    /// <summary>
    /// ソースを閉じるコールバックを設定します。
    /// FileOpenHandler が初期化された後に呼び出されます。
    /// </summary>
    /// <param name="closeSourceCallback">ソースを閉じるコールバック</param>
    public void SetCloseSourceCallback(Func<Task> closeSourceCallback)
    {
        _closeSourceCallback = () => _ = closeSourceCallback();
        
        // InputHandler が既に初期化されている場合は再作成
        if (_inputHandler != null && _presenter != null)
        {
            _inputHandler = new InputHandler(
                nextPage: () => _presenter.NextPageAsync(),
                previousPage: () => _presenter.PreviousPageAsync(),
                openFile: _openFileCallback,
                closeSource: _closeSourceCallback,
                openCatalog: async () => await _catalogController!.ToggleAsync(),
                toggleSidebar: _toggleSidebarCallback,
                toggleMode: () => _presenter.ToggleDisplayModeAsync(),
                resetZoom: () => _zoomManager.ResetZoom(),
                fitWidth: () => _zoomManager.SetMode(ZoomMode.FitWidth, _getViewSizeFunc(), _getContentSizeFunc()),
                fitPage: () => _zoomManager.SetMode(ZoomMode.FitPage, _getViewSizeFunc(), _getContentSizeFunc()),
                zoomIn: () => _zoomManager.ZoomIn(),
                zoomOut: () => _zoomManager.ZoomOut(),
                focusWindow: () => _window.Focus(),
                getCatalogVisibility: () => _catalogOverlay.Visibility,
                closeCatalog: () => _catalogController?.Close(),
                shouldNavigateOnWheel: () => _zoomManager.CurrentMode != ZoomMode.Manual || _zoomManager.ZoomFactor <= 1.05
            );
        }
    }

    /// <summary>
    /// Presenter依存のコンポーネントを初期化します。
    /// </summary>
    /// <param name="presenter">Presenterインスタンス</param>
    private void InitializeComponents(SimpleViewerPresenter presenter)
    {
        // SettingsControllerの初期化
        if (_settingsController == null)
        {
            _settingsController = new SettingsController(_settingsDirectory, presenter, _zoomManager, _sidebarColumn, _window);
        }

        // SidebarManagerの初期化
        if (_sidebarManager == null)
        {
            _sidebarManager = new SidebarManager(
                presenter,
                _thumbnailSidebar,
                _sidebarTreeView,
                _dispatcher,
                async (pageIndex) => await presenter.JumpToPageAsync(pageIndex),
                () => _window.Focus(),
                _buttonStyle
            );
        }

        // CatalogControllerの初期化（ThumbnailController を渡して共有を有効化）
        if (_catalogController == null)
        {
            _catalogController = new CatalogController(
                _catalogPanel,
                _catalogOverlay,
                () => (int)_pageSlider.Maximum,
                async (index, size, token) => await presenter.GetThumbnailAsync(index, size, token),
                async (pageIndex) => await presenter.JumpToPageAsync(pageIndex),
                _buttonStyle,
                _dispatcher,
                () => _window.Focus(),
                _sidebarManager.ThumbnailController  // サムネイル共有のため
            );
        }

        // InputHandlerの初期化
        if (_inputHandler == null)
        {
            _inputHandler = new InputHandler(
                nextPage: () => presenter.NextPageAsync(),
                previousPage: () => presenter.PreviousPageAsync(),
                openFile: _openFileCallback,
                closeSource: _closeSourceCallback ?? (() => { }),
                openCatalog: async () => await _catalogController!.ToggleAsync(),
                toggleSidebar: _toggleSidebarCallback,
                toggleMode: () => presenter.ToggleDisplayModeAsync(),
                resetZoom: () => _zoomManager.ResetZoom(),
                fitWidth: () => _zoomManager.SetMode(ZoomMode.FitWidth, _getViewSizeFunc(), _getContentSizeFunc()),
                fitPage: () => _zoomManager.SetMode(ZoomMode.FitPage, _getViewSizeFunc(), _getContentSizeFunc()),
                zoomIn: () => _zoomManager.ZoomIn(),
                zoomOut: () => _zoomManager.ZoomOut(),
                focusWindow: () => _window.Focus(),
                getCatalogVisibility: () => _catalogOverlay.Visibility,
                closeCatalog: () => _catalogController?.Close(),
                shouldNavigateOnWheel: () => _zoomManager.CurrentMode != ZoomMode.Manual || _zoomManager.ZoomFactor <= 1.05
            );
        }

        _presenter = presenter;
    }

    /// <summary>
    /// 起動時の初期化処理を実行します。
    /// </summary>
    public void Initialize()
    {
        // SkiaImageLoaderを初期化（設定を都度読み込むデリゲートを渡す）
        SkiaImageLoader.Initialize(() => _settingsManager.LoadSettings().ApplyExifOrientation);

        // キャッシュフォルダのクリーンアップ
        try
        {
            _cacheCleanupService.CleanupCacheFolder();
        }
        catch { /* 削除失敗は無視 */ }

        // 設定の読み込みと適用
        _settingsController?.LoadAndApply();

        // 高品質なビットマップスケーリングを設定
        RenderOptions.SetBitmapScalingMode(_viewContainer, BitmapScalingMode.HighQuality);
    }

    /// <summary>
    /// 終了時のクリーンアップ処理を実行します。
    /// </summary>
    public void Shutdown()
    {
        // 設定の保存
        if (_settingsController != null)
        {
            try
            {
                var task = _settingsController.SaveFromCurrentStateAsync();
                if (!task.Wait(TimeSpan.FromSeconds(2)))
                {
                    Debug.WriteLine("Settings save timed out on exit.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings on closing: {ex.Message}");
            }
        }

        // キャッシュフォルダのクリーンアップ
        try
        {
            _cacheCleanupService.CleanupCacheFolder();
        }
        catch { /* 削除失敗は無視 */ }
    }

    /// <summary>
    /// ズーム変更時のUI更新処理
    /// </summary>
    private void OnZoomChanged()
    {
        _viewContainer.LayoutTransform = new ScaleTransform(_zoomManager.ZoomFactor, _zoomManager.ZoomFactor);
        _zoomText.Text = _zoomManager.GetZoomText();
    }

    /// <summary>
    /// ビューサイズ変更時の処理
    /// </summary>
    /// <param name="getViewSizeFunc">ビューサイズ取得関数</param>
    /// <param name="getContentSizeFunc">コンテンツサイズ取得関数</param>
    public void OnViewSizeChanged(Func<Size> getViewSizeFunc, Func<Size> getContentSizeFunc)
    {
        _zoomManager.UpdateZoom(getViewSizeFunc(), getContentSizeFunc());
    }

    /// <summary>
    /// ズーム操作を実行します。
    /// </summary>
    /// <param name="mode">ズームモード</param>
    /// <param name="getViewSizeFunc">ビューサイズ取得関数</param>
    /// <param name="getContentSizeFunc">コンテンツサイズ取得関数</param>
    public void SetZoomMode(ZoomMode mode, Func<Size> getViewSizeFunc, Func<Size> getContentSizeFunc)
    {
        _zoomManager.SetMode(mode, getViewSizeFunc(), getContentSizeFunc());
    }

    /// <summary>
    /// ズームをリセットします。
    /// </summary>
    public void ResetZoom()
    {
        _zoomManager.ResetZoom();
    }

    /// <summary>
    /// ズームインを実行します。
    /// </summary>
    public void ZoomIn()
    {
        _zoomManager.ZoomIn();
    }

    /// <summary>
    /// ズームアウトを実行します。
    /// </summary>
    public void ZoomOut()
    {
        _zoomManager.ZoomOut();
    }

    /// <summary>
    /// サイドバーの表示/非表示を切り替えます。
    /// </summary>
    public void ToggleSidebar()
    {
        _sidebarColumn.Width = (_sidebarColumn.Width.Value > 0) 
            ? new GridLength(0) 
            : new GridLength(200);
    }

    /// <summary>
    /// カタログの表示/非表示を切り替えます。
    /// </summary>
    public async Task ToggleCatalogAsync()
    {
        if (_catalogController != null)
        {
            await _catalogController.ToggleAsync();
        }
    }

    /// <summary>
    /// カタログを閉じます。
    /// </summary>
    public void CloseCatalog()
    {
        _catalogController?.Close();
    }

    /// <summary>
    /// EXIF Orientationの適用設定を更新します。
    /// 設定ファイルに保存し、表示中の画像とサムネイルを再読み込みして変更を即座に反映します。
    /// </summary>
    /// <param name="applyOrientation">EXIF Orientationを適用するかどうか</param>
    public async Task UpdateApplyExifOrientationAsync(bool applyOrientation)
    {
        try
        {
            // 現在の設定を読み込み
            var settings = _settingsManager.LoadSettings();
            
            // EXIF設定を更新
            settings.ApplyExifOrientation = applyOrientation;
            
            // 設定を保存
            _settingsManager.SaveSettings(settings);

            // 画像が読み込まれている場合は現在のページとサムネイルを再読み込み
            if (_presenter != null)
            {
                // 現在のページインデックスを保存
                int currentPageIndex = (int)_pageSlider.Value;
                
                await _presenter.ReloadCurrentPageAsync();
                
                // サイドバーのサムネイルも更新（キャッシュクリア後に強制再構築）
                if (_sidebarManager != null)
                {
                    int totalPages = _presenter.GetTotalPageCount();
                    if (totalPages > 0)
                    {
                        // 既存のサムネイルUIをクリアしてから再構築
                        _sidebarManager.ClearSidebar();
                        await _sidebarManager.EnsureSidebarAsync(totalPages, currentPageIndex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to update EXIF orientation setting: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在の設定を読み込んで返します。
    /// </summary>
    /// <returns>現在の設定</returns>
    public AppSettings LoadSettings()
    {
        return _settingsManager.LoadSettings();
    }
}
