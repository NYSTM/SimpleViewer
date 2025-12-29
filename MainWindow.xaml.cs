using Microsoft.Win32;
using SimpleViewer.Models;
using SimpleViewer.Presenters;
using SimpleViewer.Utils;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimpleViewer;

/// <summary>
/// メインウィンドウのコードビハインド。
/// - View として Presenter (SimpleViewerPresenter) を受け取り、ユーザー操作や UI 更新を仲介する
/// - ズームやサイドバー、カタログ操作などの補助コントローラを組み合わせて振る舞いを実現する
/// </summary>
public partial class MainWindow : Window, IView
{
    // Presenter / 各種ユーティリティ
    private readonly SimpleViewerPresenter _presenter;
    private readonly ZoomManager _zoomManager = new();
    private readonly SettingsController _settingsController;
    private readonly SidebarManager _sidebarManager;
    private readonly InputHandler _inputHandler;
    private readonly CatalogController _catalogController;

    // ドラッグスクロール用の状態保持
    // _startPoint: ドラッグ開始時のマウス位置（ウィンドウ座標）
    // _scrollHorizontalOffset/_scrollVerticalOffset: ドラッグ開始時のスクロール位置を保持
    private Point _startPoint;
    private double _scrollHorizontalOffset;
    private double _scrollVerticalOffset;

    // 起動パラメータとして渡された初期パス（外部から設定されることを想定）
    public string? InitialPath { get; set; }

    // 現在開いているソースのパス（ZIP やフォルダ名をタイトルに表示するために保持）
    private string? _currentSourcePath;

    /// <summary>
    /// コンストラクタ: Presenter / 各コントローラの初期化、イベント登録、設定の読み込みを行う
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Presenter を生成し View を紐付ける
        _presenter = new SimpleViewerPresenter(this);

        // Zoom 状態の変更通知を UI に反映するハンドラを登録
        _zoomManager.ZoomChanged += ZoomManager_ZoomChanged;

        // 設定保存用ディレクトリをアプリ起動パスに設定
        var settingsDir = AppDomain.CurrentDomain.BaseDirectory;
        _settingsController = new SettingsController(settingsDir, _presenter, _zoomManager, SidebarColumn, this);

        // ボタンスタイルをシステムリソースから取得できなければ最小限のフォールバックを用意
        var buttonStyle = TryFindResource(ToolBar.ButtonStyleKey) as Style
            ?? Application.Current?.TryFindResource(typeof(Button)) as Style
            ?? new Style(typeof(Button));

        // サイドバー管理（サムネイルおよびツリー）を初期化
        _sidebarManager = new SidebarManager(
            _presenter,
            ThumbnailSidebar,
            SidebarTreeView,
            Dispatcher,
            async (pageIndex) => await _presenter.JumpToPageAsync(pageIndex),
            () => this.Focus(),
            buttonStyle
        );

        // カタログ表示コントローラの初期化
        _catalogController = new CatalogController(
            CatalogPanel,
            CatalogOverlay,
            () => (int)PageSlider.Maximum,
            async (index, size, token) => await _presenter.GetThumbnailAsync(index, size, token),
            async (pageIndex) => await _presenter.JumpToPageAsync(pageIndex),
            buttonStyle,
            Dispatcher,
            () => this.Focus()
        );

        // InputHandler の初期化（キーボード/ホイール操作等を抽象化）
        // 各種コマンドは Presenter や各マネージャへ委譲する
        _inputHandler = new InputHandler(
            nextPage: () => _presenter.NextPageAsync(),
            previousPage: () => _presenter.PreviousPageAsync(),
            openFile: () => MenuOpen_Click_Proxy(), // InputHandler から呼ばれるファイルオープンのプロキシ
            openCatalog: async () => await _catalogController.ToggleAsync(),
            toggleSidebar: () => MenuToggleSidebar_Click(null!, null!),
            toggleMode: () => _presenter.ToggleDisplayModeAsync(),
            resetZoom: () => _zoomManager.ResetZoom(),
            fitWidth: () => _zoomManager.SetMode(ZoomMode.FitWidth, GetViewSize(), GetContentSize()),
            fitPage: () => _zoomManager.SetMode(ZoomMode.FitPage, GetViewSize(), GetContentSize()),
            zoomIn: () => _zoomManager.ZoomIn(),
            zoomOut: () => _zoomManager.ZoomOut(),
            focusWindow: () => this.Focus(),
            getCatalogVisibility: () => CatalogOverlay.Visibility,
            closeCatalog: () => { if (_catalogController != null) _catalogController.Close(); },
            shouldNavigateOnWheel: () => _zoomManager.CurrentMode != ZoomMode.Manual || _zoomManager.ZoomFactor <= 1.05
        );

        // 設定の読み込みと適用
        _settingsController.LoadAndApply();

        // 高品質なビットマップスケーリングを設定
        RenderOptions.SetBitmapScalingMode(ViewContainer, BitmapScalingMode.HighQuality);
        this.Focusable = true;

        // Loaded 時の処理: 起動時のフォーカス確保と初期パスのオープン
        this.Loaded += async (s, e) =>
        {
            this.Focus();
            Keyboard.Focus(this);
            if (!string.IsNullOrEmpty(InitialPath)) await OpenNewSourceAsync(Path.GetFullPath(InitialPath));
        };

        // 閉じるときに設定を同期的に保存する（短時間の待機で放棄）
        this.Closing += (s, e) => {
            try
            {
                var task = _settingsController.SaveFromCurrentStateAsync();
                // 最大待機時間を設定（例: 2秒）
                if (!task.Wait(TimeSpan.FromSeconds(2)))
                {
                    Debug.WriteLine("Settings save timed out on exit.");
                }
            }
            catch (Exception ex)
            {
                // 保存失敗は致命的ではないためログ出力に留める
                Debug.WriteLine($"Failed to save settings on closing: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// InputHandler から呼ばれるファイルオープンのプロキシ。
    /// ダイアログを開いて選択されたファイルをオープンする。
    /// </summary>
    private async Task MenuOpen_Click_Proxy()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "画像・アーカイブ|*.jpg;*.png;*.zip;*.pdf|すべて|*.*" };
        if (ofd.ShowDialog() == true) await OpenNewSourceAsync(ofd.FileName);
    }

    #region IView 実装

    /// <summary>
    /// Presenter から呼ばれる画像描画更新。
    /// 左右の画像をセットし、スクロールを先頭に戻す。
    /// </summary>
    public void SetImages(BitmapSource? left, BitmapSource? right)
    {
        ImageLeft.Source = left;
        ImageRight.Source = right;
        ImageRight.Visibility = (right == null) ? Visibility.Collapsed : Visibility.Visible;
        MainScrollViewer.ScrollToHome();
        // ビュー/コンテンツサイズに基づいてズームを再計算
        _zoomManager.UpdateZoom(GetViewSize(), GetContentSize());
    }

    /// <summary>
    /// Presenter から進捗情報を受け取り UI を更新する。
    /// サイドバーの再構築は EnsureSidebarAsync を用いて必要時のみ行う。
    /// </summary>
    public void UpdateProgress(int current, int total)
    {
        StatusText.Text = $"{current} / {total}";
        PageSlider.Maximum = Math.Max(0, total - 1);
        PageSlider.Value = current - 1;
        UpdateModeDisplay();

        // タイトルに現在のエントリ名を付加（ZIP やフォルダ内のファイル名を表示）
        // 非同期にファイルリストを取得して UI スレッドでタイトル更新を行う（UI ブロックを避ける）
        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSourcePath))
                {
                    var list = await _presenter.GetFileListAsync();
                    if (list != null && list.Count > 0)
                    {
                        int idx = Math.Clamp(current - 1, 0, list.Count - 1);
                        string entryName = list[idx];
                        string containerName = Path.GetFileName(_currentSourcePath);
                        // UI スレッドでタイトルを更新
                        Dispatcher.Invoke(() => this.Title = $"SimpleViewer - {containerName} - {entryName}");
                    }
                }
            }
            catch
            {
                // タイトル更新は副次的処理なので失敗しても無視
            }
        });

        if (total > 0)
        {
            // サイドバーの構築/更新（非同期で実行して UI スレッドをブロックしない）
            _ = _sidebarManager.EnsureSidebarAsync(total, current - 1);
            _sidebarManager.HighlightThumbnail(current - 1);
        }
        else
        {
            _sidebarManager.ClearSidebar();
        }
    }

    /// <summary>
    /// エラーをダイアログで表示するヘルパー
    /// </summary>
    public void ShowError(string message) => MessageBox.Show(this, message, "SimpleViewer", MessageBoxButton.OK, MessageBoxImage.Warning);

    #endregion

    /// <summary>
    /// ZoomManager の変更を受けて UI を更新するハンドラ
    /// </summary>
    private void ZoomManager_ZoomChanged(object? sender, EventArgs e)
    {
        // LayoutTransform にスケールを適用してズーム表示を実現
        ViewContainer.LayoutTransform = new ScaleTransform(_zoomManager.ZoomFactor, _zoomManager.ZoomFactor);
        ZoomText.Text = _zoomManager.GetZoomText();
    }

    /// <summary>
    /// ZoomManager に渡すビューサイズを取得するヘルパー
    /// </summary>
    private Size GetViewSize()
    {
        // 少しマージンを引いて計算
        return new Size(MainScrollViewer.ActualWidth - 4, MainScrollViewer.ActualHeight - 4);
    }

    /// <summary>
    /// ZoomManager に渡すコンテンツサイズを取得するヘルパー
    /// </summary>
    private Size GetContentSize()
    {
        if (ImageLeft.Source == null) return new Size(0, 0);

        double totalW = ImageLeft.Source.Width + (ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Width ?? 0) : 0);
        double maxH = Math.Max(ImageLeft.Source.Height, ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Height ?? 0) : 0);
        return new Size(totalW, maxH);
    }

    #region 画面遷移 & 外部連携

    /// <summary>
    /// 新しいソースを開く処理のエントリポイント。
    /// Presenter に処理を委譲した後、必要であればサイドバー/ツリーを構築する。
    /// </summary>
    private async Task OpenNewSourceAsync(string path)
    {
        try
        {
            // UI を即時クリアして古い状態が残らないようにする
            _sidebarManager.ClearSidebar();
            _sidebarManager.ClearTree();

            await _presenter.OpenSourceAsync(path);

            // 現在開いているソースのパスを保持
            _currentSourcePath = path;

            // ウィンドウタイトルに現在開いているファイル/フォルダ名を表示する
            try
            {
                string displayName;
                if (Directory.Exists(path))
                {
                    // フォルダの場合はフォルダ名を表示
                    displayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? path;
                }
                else
                {
                    // ファイルの場合はファイル名を表示
                    displayName = Path.GetFileName(path) ?? path;
                }
                this.Title = $"SimpleViewer - {displayName}";
            }
            catch
            {
                // タイトル設定失敗は無視してデフォルトタイトルを維持
                this.Title = "SimpleViewer";
            }

            // オープン直後にファイルリストを取得して現在のエントリ名をタイトルに反映する
            try
            {
                var list = await _presenter.GetFileListAsync();
                if (list != null && list.Count > 0)
                {
                    // 現在ページは通常 0（Presenter が最初に JumpToPageAsync を呼んでいる想定）
                    int idx = 0;
                    string entryName = list[Math.Clamp(idx, 0, list.Count - 1)];
                    string containerName = Path.GetFileName(path);
                    this.Title = $"SimpleViewer - {containerName} - {entryName}";
                }
            }
            catch
            {
                // 無視
            }

            // Presenter からファイル一覧が取得できる場合はツリーを構築する
            try
            {
                var list = await _presenter.GetFileListAsync();
                if (list != null && list.Count > 0)
                {
                    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < list.Count; i++) map[list[i]] = i;

                    // ルート名はフォルダ名やファイル名を元に推測する
                    string? rootName = null;
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            rootName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        }
                        else
                        {
                            var ext = Path.GetExtension(path);
                            if (!string.IsNullOrEmpty(ext) && (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                            {
                                rootName = Path.GetFileName(path);
                            }
                            else
                            {
                                var dir = Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir))
                                {
                                    rootName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                }
                                else
                                {
                                    rootName = Path.GetFileName(path);
                                }
                            }
                        }
                    }
                    catch { rootName = null; }

                    _sidebarManager.BuildTree(list, map, rootName);
                }
                else
                {
                    // ファイルなし: ツリーをクリアして UI をリセット
                    _sidebarManager.ClearTree();
                    ClearUI();
                }
            }
            catch (Exception ex)
            {
                // ツリー生成失敗は致命的ではないためログに出力して UI には影響させない
                Debug.WriteLine($"BuildTree failed: {ex.Message}");
                _sidebarManager.ClearTree();
                ClearUI();
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>
    /// UI を完全にクリアする補助メソッド
    /// </summary>
    private void ClearUI()
    {
        _sidebarManager.ClearSidebar();
        CatalogPanel.Children.Clear();
        CatalogOverlay.Visibility = Visibility.Collapsed;
        ImageLeft.Source = null;
        ImageRight.Source = null;
        StatusText.Text = "0 / 0";

        // タイトルをデフォルトに戻す
        this.Title = "SimpleViewer";
        _currentSourcePath = null;
    }

    #endregion

    #region メインメニュー / ボタンイベント

    /// <summary>
    /// メニューからファイルを開く処理
    /// </summary>
    private async void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "画像・アーカイブ|*.jpg;*.png;*.zip;*.pdf|すべて|*.*" };
        if (ofd.ShowDialog() == true) await OpenNewSourceAsync(ofd.FileName);
    }

    /// <summary>
    /// 現在のソースを閉じて UI をリセットする
    /// </summary>
    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        ClearUI();
        _sidebarManager.ClearTree();
        _presenter.CloseSource();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => this.Close();

    private void MenuToggleMode_Click(object sender, RoutedEventArgs e) => _ = _presenter.ToggleDisplayModeAsync();

    private void MenuFitWidth_Click(object sender, RoutedEventArgs e) { _zoomManager.SetMode(ZoomMode.FitWidth, GetViewSize(), GetContentSize()); }

    private void MenuFitPage_Click(object sender, RoutedEventArgs e) { _zoomManager.SetMode(ZoomMode.FitPage, GetViewSize(), GetContentSize()); }

    private void MenuResetZoom_Click(object sender, RoutedEventArgs e) => _zoomManager.ResetZoom();

    private void MenuZoomIn_Click(object sender, RoutedEventArgs e) => _zoomManager.ZoomIn();

    private void MenuZoomOut_Click(object sender, RoutedEventArgs e) => _zoomManager.ZoomOut();

    /// <summary>
    /// サイドバーの表示/非表示を切り替える
    /// </summary>
    private void MenuToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        SidebarColumn.Width = (SidebarColumn.Width.Value > 0) ? new GridLength(0) : new GridLength(200);
    }

    /// <summary>
    /// カタログ表示を切り替える（CatalogController に処理を委譲）
    /// </summary>
    private async void MenuCatalog_Click(object sender, RoutedEventArgs e)
    {
        await _catalogController.ToggleAsync();
    }

    /// <summary>
    /// カタログを閉じる
    /// </summary>
    private void CloseCatalog_Click(object sender, RoutedEventArgs e)
    {
        _catalogController.Close();
    }

    #endregion

    #region 入力イベント (キーボード / マウス / ドロップ)

    /// <summary>
    /// キーイベントを InputHandler に委譲する（処理された場合は以降のデフォルト処理を抑止）
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_inputHandler.HandlePreviewKeyDown(e)) return;
        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// マウスホイールのプレビューイベントを InputHandler に委譲する
    /// </summary>
    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inputHandler != null && _inputHandler.HandlePreviewMouseWheel(e)) return;
        // それ以外は既定の挙動を継続
    }

    /// <summary>
    /// ドラッグによるパン開始処理
    /// </summary>
    private void MainScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // スクロールバー上の操作であればパン操作を開始しない
        if (e.OriginalSource is DependencyObject dep && IsDescendantOfScrollBar(dep)) return;
        _startPoint = e.GetPosition(this);
        _scrollHorizontalOffset = MainScrollViewer.HorizontalOffset;
        _scrollVerticalOffset = MainScrollViewer.VerticalOffset;
        MainScrollViewer.CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    /// <summary>
    /// ドラッグ中のスクロール更新
    /// </summary>
    private void MainScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (MainScrollViewer.IsMouseCaptured)
        {
            Vector delta = _startPoint - e.GetPosition(this);
            MainScrollViewer.ScrollToHorizontalOffset(_scrollHorizontalOffset + delta.X);
            MainScrollViewer.ScrollToVerticalOffset(_scrollVerticalOffset + delta.Y);
        }
    }

    /// <summary>
    /// ドラッグ終了処理
    /// </summary>
    private void MainScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (MainScrollViewer.IsMouseCaptured) { MainScrollViewer.ReleaseMouseCapture(); Cursor = Cursors.Arrow; }
    }

    /// <summary>
    /// ファイルをウィンドウへドロップしたときの処理
    /// </summary>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                await OpenNewSourceAsync(files[0]);

                // ドロップ直後にフォーカスをウィンドウへ戻す
                this.Activate();
                this.Focus();
                Keyboard.Focus(this);
            }
        }
    }

    /// <summary>
    /// 指定要素が ScrollBar の子孫かどうかを判定するヘルパー
    /// </summary>
    private bool IsDescendantOfScrollBar(DependencyObject element)
    {
        while (element != null) { if (element is ScrollBar) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }

    /// <summary>
    /// スクロールビューワーのサイズが変わったときにズームを再計算する
    /// </summary>
    private void MainScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _zoomManager.UpdateZoom(GetViewSize(), GetContentSize());
        // サイドバーの高解像度サムネイルは SidebarSizeWatcher を通じて適宜更新されるため、ここで再構築は不要
    }

    /// <summary>
    /// PageSlider のドラッグ終了でページ移動を行う
    /// </summary>
    private void PageSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _ = _presenter.JumpToPageAsync((int)PageSlider.Value);
        this.Focus();
    }

    /// <summary>
    /// 現在の表示モードを UI に反映するヘルパー
    /// </summary>
    private void UpdateModeDisplay()
    {
        if (ModeText == null) return;
        ModeText.Text = _presenter.CurrentDisplayMode switch
        {
            DisplayMode.Single => "単一表示",
            DisplayMode.SpreadRTL => "見開き(右)",
            DisplayMode.SpreadLTR => "見開き(左)",
            _ => "---"
        };
    }

    #endregion
}