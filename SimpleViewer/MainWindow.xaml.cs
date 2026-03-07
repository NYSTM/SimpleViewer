using Microsoft.Win32;
using SimpleViewer.Presenters;
using SimpleViewer.Utils.UI;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimpleViewer;

/// <summary>
/// メインウィンドウのコードビハインド。
/// 各種ハンドラとコーディネーターを初期化し、イベントを適切なハンドラに委譲します。
/// </summary>
public partial class MainWindow : Window
{
    private readonly SimpleViewerPresenter _presenter;
    private readonly WindowCoordinator _coordinator;
    private readonly FileOpenHandler _fileOpenHandler;
    private readonly TitleBarManager _titleBarManager;
    private readonly DragDropHandler _dragDropHandler;
    private readonly MouseGestureHandler _mouseGestureHandler;
    private readonly MainWindowViewImplementation _viewImplementation;
    private readonly MainWindowMenuHandler _menuHandler;
    private readonly MainWindowSizeProvider _sizeProvider;

    public string? InitialPath { get; set; }

    /// <summary>
    /// コンストラクタ: 各種コンポーネントの初期化を行う
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // サイズプロバイダーを初期化
        _sizeProvider = new MainWindowSizeProvider(
            MainScrollViewer,
            ImageLeft,
            ImageRight);

        // タイトルバーマネージャーを初期化
        _titleBarManager = new TitleBarManager(this);

        // ボタンスタイルを取得
        var buttonStyle = TryFindResource(ToolBar.ButtonStyleKey) as Style
            ?? Application.Current?.TryFindResource(typeof(Button)) as Style
            ?? new Style(typeof(Button));

        // WindowCoordinatorを初期化（Presenterなしで初期化）
        _coordinator = new WindowCoordinator(
            window: this,
            presenter: null,
            viewContainer: ViewContainer,
            mainScrollViewer: MainScrollViewer,
            imageLeft: ImageLeft,
            imageRight: ImageRight,
            zoomText: ZoomText,
            sidebarColumn: SidebarColumn,
            thumbnailSidebar: ThumbnailSidebar,
            sidebarTreeView: SidebarTreeView,
            catalogPanel: CatalogPanel,
            catalogOverlay: CatalogOverlay,
            pageSlider: PageSlider,
            dispatcher: Dispatcher,
            buttonStyle: buttonStyle,
            openFileCallback: OpenFileDialogAsync,
            toggleSidebarCallback: null!, // 循環参照を避けるため、後で設定
            clearUICallback: ClearUI,
            closeSourceCallback: null,
            getViewSizeFunc: _sizeProvider.GetViewSize,
            getContentSizeFunc: _sizeProvider.GetContentSize
        );

        // ToggleSidebarコールバックを設定（初期化後に循環参照を解決）
        _coordinator.SetToggleSidebarCallback(() => _coordinator.ToggleSidebar());
        
        // ViewImplementationを初期化（SidebarManagerへのアクセスを避ける）
        SimpleViewerPresenter? presenterRef = null;
        SidebarManager? sidebarManagerRef = null;
        
        _viewImplementation = new MainWindowViewImplementation(
            ImageLeft,
            ImageRight,
            MainScrollViewer,
            StatusText,
            ModeText,
            PageSlider,
            this,
            () => sidebarManagerRef ?? throw new InvalidOperationException("SidebarManager がまだ初期化されていません"),
            null, // FileOpenHandlerは後で設定
            _coordinator.OnViewSizeChanged,
            () => presenterRef?.CurrentDisplayMode ?? Models.DisplayMode.Single);

        // 正式なPresenterを作成
        _presenter = new SimpleViewerPresenter(_viewImplementation);
        presenterRef = _presenter;

        // CoordinatorにPresenterを設定
        _coordinator.SetPresenter(_presenter);
        
        // SidebarManagerの参照を取得
        sidebarManagerRef = _coordinator.SidebarManager;

        // FileOpenHandlerを正式なPresenterで作成
        _fileOpenHandler = new FileOpenHandler(
            _presenter,
            sidebarManagerRef,
            _titleBarManager);

        // CloseSourceコールバックをWindowCoordinatorに設定
        _coordinator.SetCloseSourceCallback(async () =>
        {
            ClearUI();
            await _presenter.CloseSourceAsync();
            _fileOpenHandler.ClearCurrentSource();
        });

        // ViewImplementationにFileOpenHandlerを設定
        _viewImplementation.SetFileOpenHandler(_fileOpenHandler);

        // メニューハンドラーを初期化
        _menuHandler = new MainWindowMenuHandler(
            _presenter,
            _coordinator,
            _fileOpenHandler,
            this,
            OpenFileDialogAsync,
            ClearUI,
            _sizeProvider.GetViewSize,
            _sizeProvider.GetContentSize);

        // ドラッグ&ドロップハンドラーを初期化
        _dragDropHandler = new DragDropHandler(
            openFileCallback: OpenSourceAsync,
            focusWindowCallback: FocusWindow,
            isLoadingCallback: () => _presenter.IsLoadingSource);

        // マウスジェスチャーハンドラーを初期化
        _mouseGestureHandler = new MouseGestureHandler(
            MainScrollViewer,
            nextPageCallback: () => _presenter.NextPageAsync(),
            previousPageCallback: () => _presenter.PreviousPageAsync());

        // 初期化処理
        _coordinator.Initialize();
        this.Focusable = true;

        // Loaded時の処理
        this.Loaded += OnWindowLoaded;
        // Closing時の処理
        this.Closing += (s, e) => _coordinator.Shutdown();
    }

    #region ライフサイクル

    /// <summary>
    /// ウィンドウ読み込み完了時の処理
    /// </summary>
    private async void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        FocusWindow();
        
        // 設定からEXIF Orientationメニューの状態を復元
        LoadExifOrientationMenuState();
        
        if (!string.IsNullOrEmpty(InitialPath))
        {
            await OpenSourceAsync(Path.GetFullPath(InitialPath));
        }
    }

    /// <summary>
    /// 設定からEXIF Orientationメニューのチェック状態を読み込みます。
    /// </summary>
    private void LoadExifOrientationMenuState()
    {
        try
        {
            var settings = _coordinator.LoadSettings();
            MenuApplyExifOrientation.IsChecked = settings.ApplyExifOrientation;
        }
        catch
        {
            // 設定読み込み失敗時はデフォルト値（true）のまま
            MenuApplyExifOrientation.IsChecked = true;
        }
    }

    /// <summary>
    /// ウィンドウにフォーカスを設定します。
    /// </summary>
    private void FocusWindow()
    {
        this.Focus();
        Keyboard.Focus(this);
    }

    #endregion

    #region ファイル操作

    /// <summary>
    /// ファイルオープンダイアログを表示します。
    /// </summary>
    private async Task OpenFileDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "画像・アーカイブ|*.jpg;*.png;*.zip;*.pdf|すべて|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await OpenSourceAsync(dialog.FileName);
        }
    }

    /// <summary>
    /// 新しいソースを開きます。
    /// </summary>
    /// <param name="path">ファイルまたはフォルダのパス</param>
    private async Task OpenSourceAsync(String path)
    {
        try
        {
            // ロード中のカーソルを設定
            Mouse.OverrideCursor = Cursors.Wait;
            
            // カタログが開いている場合は閉じる
            try { _coordinator.CloseCatalog(); } catch { }

            await _fileOpenHandler.OpenSourceAsync(path);
        }
        catch (Exception ex)
        {
            _viewImplementation.ShowError(ex.Message);
        }
        finally
        {
            // カーソルを元に戻す
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>
    /// UIをクリアします。
    /// </summary>
    private void ClearUI()
    {
        _coordinator.SidebarManager.ClearSidebar();
        _coordinator.SidebarManager.ClearTree();
        CatalogPanel.Children.Clear();
        CatalogOverlay.Visibility = Visibility.Collapsed;
        ImageLeft.Source = null;
        ImageRight.Source = null;
        StatusText.Text = "0 / 0";
        _titleBarManager.SetDefaultTitle();
    }

    #endregion

    #region メニュー / ボタンイベント

    private async void MenuOpen_Click(object sender, RoutedEventArgs e) =>
        await _menuHandler.HandleMenuOpenClickAsync();

    private async void MenuClose_Click(object sender, RoutedEventArgs e) =>
        await _menuHandler.HandleMenuCloseClickAsync();

    private void MenuExit_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuExitClick();

    private async void MenuToggleMode_Click(object sender, RoutedEventArgs e) =>
        await _menuHandler.HandleMenuToggleModeClickAsync();

    private void MenuFitWidth_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuFitWidthClick();

    private void MenuFitPage_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuFitPageClick();

    private void MenuResetZoom_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuResetZoomClick();

    private void MenuZoomIn_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuZoomInClick();

    private void MenuZoomOut_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuZoomOutClick();

    private void MenuToggleSidebar_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleMenuToggleSidebarClick();

    private async void MenuCatalog_Click(object sender, RoutedEventArgs e) =>
        await _menuHandler.HandleMenuCatalogClickAsync();

    private void CloseCatalog_Click(object sender, RoutedEventArgs e) =>
        _menuHandler.HandleCloseCatalogClick();

    private async void MenuApplyExifOrientation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            await _menuHandler.HandleApplyExifOrientationToggleAsync(menuItem.IsChecked);
        }
    }

    #endregion

    #region 入力イベント (キーボード / マウス / ドロップ)

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_coordinator.InputHandler.HandlePreviewKeyDown(e)) return;
        base.OnPreviewKeyDown(e);
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _coordinator.InputHandler.HandlePreviewMouseWheel(e);

    private void MainScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _mouseGestureHandler.HandleMouseLeftButtonDown(e, this);

    private void MainScrollViewer_MouseMove(object sender, MouseEventArgs e) =>
        _mouseGestureHandler.HandleMouseMove(e, this);

    private async void MainScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        await _mouseGestureHandler.HandleMouseLeftButtonUpAsync(e, this);

    private async void MainScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        await _mouseGestureHandler.HandleMouseLeftButtonUpAsync(e, this);

    private async void Window_Drop(object sender, DragEventArgs e) =>
        await _dragDropHandler.HandleDropAsync(e);

    private void Window_DragOver(object sender, DragEventArgs e) =>
        _dragDropHandler.HandleDragOver(e);

    private void MainScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        _coordinator.OnViewSizeChanged(_sizeProvider.GetViewSize, _sizeProvider.GetContentSize);

    private void PageSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _ = _presenter.JumpToPageAsync((int)PageSlider.Value);
        FocusWindow();
    }

    #endregion
}
