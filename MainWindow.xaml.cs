using Microsoft.Win32;
using SimpleViewer.Models;
using SimpleViewer.Presenters;
using SimpleViewer.Utils;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer;

public partial class MainWindow : Window, IView
{
    private readonly SimpleViewerPresenter _presenter;
    private readonly ZoomManager _zoomManager = new();
    private readonly SettingsManager _settingsManager; // 追加

    private readonly Dictionary<int, Button> _sidebarItems = new();
    private int _lastHighlightedIndex = -1;

    private CancellationTokenSource? _sidebarCts;
    private CancellationTokenSource? _catalogCts;

    private Point _startPoint;
    private double _scrollHorizontalOffset;
    private double _scrollVerticalOffset;

    public string? InitialPath { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _presenter = new SimpleViewerPresenter(this);
        _zoomManager.ZoomChanged += ZoomManager_ZoomChanged;

        _settingsManager = new SettingsManager(AppDomain.CurrentDomain.BaseDirectory); // 追加

        // 1. 設定の読み込みと適用
        LoadSettings();

        RenderOptions.SetBitmapScalingMode(ViewContainer, BitmapScalingMode.HighQuality);
        this.Focusable = true;

        this.Loaded += async (s, e) =>
        {
            // 起動時のフォーカス確保
            this.Focus();
            Keyboard.Focus(this);
            if (!string.IsNullOrEmpty(InitialPath)) await OpenNewSourceAsync(Path.GetFullPath(InitialPath));
        };

        // 2. ウィンドウを閉じる際の自動保存イベント登録
        this.Closing += (s, e) => SaveSettings();
    }

    #region 設定永続化 (強化版)

    private void LoadSettings()
    {
        // try-catch の代わりに SettingsManager 内で処理
        var s = _settingsManager.LoadSettings();

        // 表示モードの復元
        _presenter.SetDisplayMode(s.DisplayMode);

        // ズーム設定の復元
        ZoomMode restoredMode = ZoomMode.Manual;
        if (Enum.TryParse(s.ZoomMode, out ZoomMode zMode))
        {
            restoredMode = zMode;
        }
        _zoomManager.SetZoom(s.ZoomFactor, restoredMode);

        // サイドバー状態の復元
        SidebarColumn.Width = s.IsSidebarVisible ? new GridLength(200) : new GridLength(0);

        // ウィンドウサイズと状態(最大化など)の復元
        this.Width = s.WindowWidth;
        this.Height = s.WindowHeight;
        if (s.WindowState == (int)WindowState.Maximized)
        {
            this.WindowState = WindowState.Maximized;
        }
    }

    private void SaveSettings()
    {
        var s = new AppSettings
        {
            DisplayMode = _presenter.CurrentDisplayMode,
            ZoomMode = _zoomManager.CurrentMode.ToString(),
            ZoomFactor = _zoomManager.ZoomFactor,
            IsSidebarVisible = SidebarColumn.Width.Value > 0,
            WindowState = (int)this.WindowState,
            WindowWidth = this.ActualWidth,
            WindowHeight = this.ActualHeight
        };

        _settingsManager.SaveSettings(s);
    }

    #endregion

    #region IView 実装

    public void SetImages(BitmapSource? left, BitmapSource? right)
    {
        ImageLeft.Source = left;
        ImageRight.Source = right;
        ImageRight.Visibility = (right == null) ? Visibility.Collapsed : Visibility.Visible;
        MainScrollViewer.ScrollToHome();
        _zoomManager.UpdateZoom(GetViewSize(), GetContentSize());
    }

    public void UpdateProgress(int current, int total)
    {
        StatusText.Text = $"{current} / {total}";
        PageSlider.Maximum = Math.Max(0, total - 1);
        PageSlider.Value = current - 1;
        UpdateModeDisplay();

        if (_sidebarItems.Count == 0 && total > 0)
        {
            _sidebarCts?.Cancel();
            _sidebarCts = new CancellationTokenSource();
            _ = BuildSidebarAsync(_sidebarCts.Token);
        }
        HighlightCurrentThumbnail(current - 1);
    }

    public void ShowError(string message) => MessageBox.Show(this, message, "SimpleViewer", MessageBoxButton.OK, MessageBoxImage.Warning);

    #endregion
    // ZoomManagerのZoomChangedイベントハンドラ
    private void ZoomManager_ZoomChanged(object? sender, EventArgs e)
    {
        ViewContainer.LayoutTransform = new ScaleTransform(_zoomManager.ZoomFactor, _zoomManager.ZoomFactor);
        ZoomText.Text = _zoomManager.GetZoomText();
    }

    // ZoomManagerに渡すビューサイズを取得するヘルパーメソッド
    private Size GetViewSize()
    {
        return new Size(MainScrollViewer.ActualWidth - 4, MainScrollViewer.ActualHeight - 4);
    }

    // ZoomManagerに渡すコンテンツサイズを取得するヘルパーメソッド
    private Size GetContentSize()
    {
        if (ImageLeft.Source == null) return new Size(0, 0);

        double totalW = ImageLeft.Source.Width + (ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Width ?? 0) : 0);
        double maxH = Math.Max(ImageLeft.Source.Height, ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Height ?? 0) : 0);
        return new Size(totalW, maxH);
    }

    #region サイドバー & ハイライト (セッション管理)

    private async Task BuildSidebarAsync(CancellationToken token)
    {
        int total = (int)PageSlider.Maximum + 1;
        var sessionCts = _sidebarCts;

        for (int i = 0; i < total; i++)
        {
            if (token.IsCancellationRequested) return;
            var thumb = await _presenter.GetThumbnailAsync(i, 160, token);
            if (token.IsCancellationRequested || _sidebarCts != sessionCts) return;

            if (thumb != null)
            {
                var item = CreateThumbnailElement(thumb, i, 150);
                _sidebarItems[i] = item;
                ThumbnailSidebar.Items.Add(item);
                if (i == (int)PageSlider.Value) HighlightCurrentThumbnail(i);
            }
            if (i % 5 == 0) await Task.Yield();
        }
    }

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
            Style = (Style)FindResource(ToolBar.ButtonStyleKey)
        };
        btn.Click += async (s, _) =>
        {
            int idx = (int)((Button)s).Tag;
            HighlightCurrentThumbnail(idx);
            await _presenter.JumpToPageAsync(idx);
            this.Focus();
        };
        return btn;
    }

    private void HighlightCurrentThumbnail(int index)
    {
        this.Dispatcher.Invoke(() =>
        {
            if (_lastHighlightedIndex != -1 && _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
                oldBtn.BorderBrush = Brushes.Transparent;

            if (_sidebarItems.TryGetValue(index, out var newBtn))
            {
                newBtn.BorderBrush = SystemColors.HighlightBrush;
                newBtn.BringIntoView();
                _lastHighlightedIndex = index;
            }
        }, DispatcherPriority.Send);
    }

    #endregion

    #region 画面遷移 & 外部連携

    private async Task OpenNewSourceAsync(string path)
    {
        try
        {
            _sidebarCts?.Cancel(); _catalogCts?.Cancel();
            _sidebarItems.Clear();
            _lastHighlightedIndex = -1;
            ThumbnailSidebar.Items.Clear();
            CatalogPanel.Children.Clear();
            CatalogOverlay.Visibility = Visibility.Collapsed;
            await _presenter.OpenSourceAsync(path);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ClearUI()
    {
        _sidebarCts?.Cancel(); _catalogCts?.Cancel();
        _sidebarItems.Clear();
        _lastHighlightedIndex = -1;
        ThumbnailSidebar.Items.Clear();
        CatalogPanel.Children.Clear();
        CatalogOverlay.Visibility = Visibility.Collapsed;
        ImageLeft.Source = null;
        ImageRight.Source = null;
        StatusText.Text = "0 / 0";
    }

    #endregion

    #region メインメニュー / ボタンイベント

    private async void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "画像・アーカイブ|*.jpg;*.png;*.zip;*.pdf|すべて|*.*" };
        if (ofd.ShowDialog() == true) await OpenNewSourceAsync(ofd.FileName);
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        ClearUI();
        _presenter.CloseSource();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => this.Close();

    private void MenuToggleMode_Click(object sender, RoutedEventArgs e) => _ =_presenter.ToggleDisplayModeAsync();

    private void MenuFitWidth_Click(object sender, RoutedEventArgs e) { _zoomManager.SetMode(ZoomMode.FitWidth, GetViewSize(), GetContentSize()); }

    private void MenuFitPage_Click(object sender, RoutedEventArgs e) { _zoomManager.SetMode(ZoomMode.FitPage, GetViewSize(), GetContentSize()); }

    private void MenuResetZoom_Click(object sender, RoutedEventArgs e) => _zoomManager.ResetZoom();

    private void MenuZoomIn_Click(object sender, RoutedEventArgs e) => _zoomManager.ZoomIn();

    private void MenuZoomOut_Click(object sender, RoutedEventArgs e) => _zoomManager.ZoomOut();

    private void MenuToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        SidebarColumn.Width = (SidebarColumn.Width.Value > 0) ? new GridLength(0) : new GridLength(200);
    }

    private async void MenuCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogOverlay.Visibility == Visibility.Visible) { CloseCatalog_Click(null!, null!); return; }
        CatalogOverlay.Visibility = Visibility.Visible;
        CatalogPanel.Children.Clear();
        _catalogCts?.Cancel();
        _catalogCts = new CancellationTokenSource();
        var token = _catalogCts.Token;

        for (int i = 0; i <= (int)PageSlider.Maximum; i++)
        {
            if (token.IsCancellationRequested) return;
            var thumb = await _presenter.GetThumbnailAsync(i, 200, token);
            if (thumb != null)
            {
                var item = CreateThumbnailElement(thumb, i, 180);
                item.Click += (s, _) => CatalogOverlay.Visibility = Visibility.Collapsed;
                CatalogPanel.Children.Add(item);
            }
            if (i % 10 == 0) await Task.Yield();
        }
    }

    private void CloseCatalog_Click(object sender, RoutedEventArgs e)
    {
        _catalogCts?.Cancel();
        CatalogOverlay.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 入力イベント (キーボード / マウス / ドロップ)

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.O: MenuOpen_Click(null!, null!); e.Handled = true; return;
                case Key.W: MenuClose_Click(null!, null!); e.Handled = true; return;
                case Key.F: _zoomManager.SetMode(ZoomMode.FitWidth, GetViewSize(), GetContentSize()); e.Handled = true; return;
                case Key.G: _zoomManager.SetMode(ZoomMode.FitPage, GetViewSize(), GetContentSize()); e.Handled = true; return;
                case Key.D0: case Key.NumPad0: _zoomManager.ResetZoom(); e.Handled = true; return;
                case Key.OemPlus: case Key.Add: _zoomManager.ZoomIn(); e.Handled = true; return;
                case Key.OemMinus: case Key.Subtract: _zoomManager.ZoomOut(); e.Handled = true; return;
            }
        }
        switch (e.Key)
        {
            case Key.Left: case Key.Space: _ = _presenter.NextPageAsync(); e.Handled = true; break;
            case Key.Right: case Key.Back: _ = _presenter.PreviousPageAsync(); e.Handled = true; break;
            case Key.F3: MenuCatalog_Click(null!, null!); e.Handled = true; break;
            case Key.F4: MenuToggleSidebar_Click(null!, null!); e.Handled = true; break;
            case Key.S: MenuToggleMode_Click(null!, null!); e.Handled = true; break;
            case Key.Escape:
                if (CatalogOverlay.Visibility == Visibility.Visible) CloseCatalog_Click(null!, null!);
                else _zoomManager.ResetZoom();
                e.Handled = true; break;
        }
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0) _zoomManager.ZoomIn(); else _zoomManager.ZoomOut();
            e.Handled = true;
        }
        else if (_zoomManager.CurrentMode != ZoomMode.Manual || _zoomManager.ZoomFactor <= 1.05)
        {
            if (e.Delta > 0) _ = _presenter.PreviousPageAsync(); else _ = _presenter.NextPageAsync();
            e.Handled = true;
        }
    }

    private void MainScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && IsDescendantOfScrollBar(dep)) return;
        _startPoint = e.GetPosition(this);
        _scrollHorizontalOffset = MainScrollViewer.HorizontalOffset;
        _scrollVerticalOffset = MainScrollViewer.VerticalOffset;
        MainScrollViewer.CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void MainScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (MainScrollViewer.IsMouseCaptured)
        {
            Vector delta = _startPoint - e.GetPosition(this);
            MainScrollViewer.ScrollToHorizontalOffset(_scrollHorizontalOffset + delta.X);
            MainScrollViewer.ScrollToVerticalOffset(_scrollVerticalOffset + delta.Y);
        }
    }

    private void MainScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (MainScrollViewer.IsMouseCaptured) { MainScrollViewer.ReleaseMouseCapture(); Cursor = Cursors.Arrow; }
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                await OpenNewSourceAsync(files[0]);

                // ドロップ直後のフォーカス修正
                this.Activate(); // ウィンドウをアクティブにする
                this.Focus();
                Keyboard.Focus(this);
            }
        }
    }

    private bool IsDescendantOfScrollBar(DependencyObject element)
    {
        while (element != null) { if (element is ScrollBar) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }

    private void MainScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _zoomManager.UpdateZoom(GetViewSize(), GetContentSize());
    }

    private void PageSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _ = _presenter.JumpToPageAsync((int)PageSlider.Value);
        this.Focus();
    }

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