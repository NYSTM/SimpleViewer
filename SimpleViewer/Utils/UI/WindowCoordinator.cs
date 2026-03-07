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
/// ウィンドウ内の各種コントローラを統合管理するクラス。
/// </summary>
public class WindowCoordinator
{
    private readonly ZoomManager _zoomManager;
    private SettingsController? _settingsController;
    private SidebarManager? _sidebarManager;
    private InputHandler? _inputHandler;
    private CatalogController? _catalogController;
    private readonly CacheCleanupService _cacheCleanupService;
    private readonly SettingsManager _settingsManager;
    private SimpleViewerPresenter? _presenter;

    private readonly FrameworkElement _viewContainer;
    private readonly ScrollViewer _mainScrollViewer;
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
    private Action _toggleSidebarCallback = null!; // 後で設定されるため null! で初期化
    private readonly Func<Size> _getViewSizeFunc;
    private readonly Func<Size> _getContentSizeFunc;
    private Action? _closeSourceCallback;

    public ZoomManager ZoomManager => _zoomManager;
    public SidebarManager SidebarManager => _sidebarManager ?? throw new InvalidOperationException("SidebarManager がまだ初期化されていません");
    public CatalogController CatalogController => _catalogController ?? throw new InvalidOperationException("CatalogController がまだ初期化されていません");
    public InputHandler InputHandler => _inputHandler ?? throw new InvalidOperationException("InputHandler がまだ初期化されていません");

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
        Action? toggleSidebarCallback, // null 許容型に変更
        Action? clearUICallback,
        Action? closeSourceCallback,
        Func<Size> getViewSizeFunc,
        Func<Size> getContentSizeFunc)
    {
        _window = window;
        _viewContainer = viewContainer;
        _mainScrollViewer = mainScrollViewer;
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
        if (toggleSidebarCallback != null)
        {
            _toggleSidebarCallback = toggleSidebarCallback;
        }
        _closeSourceCallback = closeSourceCallback;
        _getViewSizeFunc = getViewSizeFunc;
        _getContentSizeFunc = getContentSizeFunc;

        _cacheCleanupService = new CacheCleanupService(_settingsDirectory);
        _settingsManager = new SettingsManager(_settingsDirectory);

        _zoomManager = new ZoomManager();
        _zoomManager.ZoomChanged += (s, e) => OnZoomChanged();

        if (presenter != null)
        {
            _presenter = presenter;
            InitializeComponents(presenter);
        }
    }

    public void SetPresenter(SimpleViewerPresenter presenter)
    {
        _presenter = presenter;
        if (_sidebarManager == null || _catalogController == null || _inputHandler == null)
            InitializeComponents(presenter);
    }

    public void SetCloseSourceCallback(Func<Task> closeSourceCallback)
    {
        _closeSourceCallback = () => _ = closeSourceCallback();
        if (_inputHandler != null && _presenter != null)
            RebuildInputHandler();
    }

    public void SetToggleSidebarCallback(Action toggleSidebarCallback)
    {
        _toggleSidebarCallback = toggleSidebarCallback;
    }

    private void InitializeComponents(SimpleViewerPresenter presenter)
    {
        _settingsController ??= new SettingsController(_settingsDirectory, presenter, _zoomManager, _sidebarColumn, _window);

        _sidebarManager ??= new SidebarManager(
            presenter, _thumbnailSidebar, _sidebarTreeView, _dispatcher,
            async (pageIndex) => await presenter.JumpToPageAsync(pageIndex),
            () => _window.Focus(), _buttonStyle);

        _catalogController ??= new CatalogController(
            _catalogPanel, _catalogOverlay, () => (int)_pageSlider.Maximum,
            async (index, size, token) => await presenter.GetThumbnailAsync(index, size, token),
            async (pageIndex) => await presenter.JumpToPageAsync(pageIndex),
            _buttonStyle, _dispatcher, () => _window.Focus(), _sidebarManager.ThumbnailController);

        _inputHandler ??= CreateInputHandler(presenter);
        _presenter = presenter;
    }

    private InputHandler CreateInputHandler(SimpleViewerPresenter presenter)
    {
        return new InputHandler(
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
            shouldNavigateOnWheel: () => _zoomManager.CurrentMode != ZoomMode.Manual || _zoomManager.ZoomFactor <= 1.05);
    }

    private void RebuildInputHandler()
    {
        if (_presenter != null) _inputHandler = CreateInputHandler(_presenter);
    }

    public void Initialize()
    {
        SkiaImageLoader.Initialize(() => _settingsManager.LoadSettings().ApplyExifOrientation);
        try { _cacheCleanupService.CleanupCacheFolder(); } catch { }
        _settingsController?.LoadAndApply();
        RenderOptions.SetBitmapScalingMode(_viewContainer, BitmapScalingMode.HighQuality);
    }

    public void Shutdown()
    {
        if (_settingsController != null)
        {
            try { if (!_settingsController.SaveFromCurrentStateAsync().Wait(TimeSpan.FromSeconds(2))) Debug.WriteLine("設定保存タイムアウト"); }
            catch (Exception ex) { Debug.WriteLine($"設定保存エラー: {ex.Message}"); }
        }
        try { _cacheCleanupService.CleanupCacheFolder(); } catch { }
    }

    private void OnZoomChanged()
    {
        _viewContainer.LayoutTransform = new ScaleTransform(_zoomManager.ZoomFactor, _zoomManager.ZoomFactor);
        _zoomText.Text = _zoomManager.GetZoomText();
    }

    public void OnViewSizeChanged(Func<Size> getViewSizeFunc, Func<Size> getContentSizeFunc)
        => _zoomManager.UpdateZoom(getViewSizeFunc(), getContentSizeFunc());

    public void ToggleSidebar()
        => _sidebarColumn.Width = (_sidebarColumn.Width.Value > 0) ? new GridLength(0) : new GridLength(200);

    public async Task ToggleCatalogAsync() { if (_catalogController != null) await _catalogController.ToggleAsync(); }
    public void CloseCatalog() => _catalogController?.Close();

    public async Task UpdateApplyExifOrientationAsync(bool applyOrientation)
    {
        try
        {
            var settings = _settingsManager.LoadSettings();
            settings.ApplyExifOrientation = applyOrientation;
            _settingsManager.SaveSettings(settings);

            if (_presenter != null)
            {
                int currentPageIndex = (int)_pageSlider.Value;
                await _presenter.ReloadCurrentPageAsync();
                if (_sidebarManager != null)
                {
                    int totalPages = _presenter.GetTotalPageCount();
                    if (totalPages > 0)
                    {
                        _sidebarManager.ClearSidebar();
                        await _sidebarManager.EnsureSidebarAsync(totalPages, currentPageIndex);
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"EXIF設定更新エラー: {ex.Message}"); }
    }

    public AppSettings LoadSettings() => _settingsManager.LoadSettings();
}
