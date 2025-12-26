using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SimpleViewer.Presenters;
using SimpleViewer.Models;
using System.Windows.Controls.Primitives;

namespace SimpleViewer;

public partial class MainWindow : Window, IView
{
    private readonly SimpleViewerPresenter _presenter;
    private double _zoomFactor = 1.0;
    private const double ZoomStep = 0.1;

    private enum ZoomMode { Manual, FitWidth, FitPage }
    private ZoomMode _currentZoomMode = ZoomMode.Manual;

    private readonly Dictionary<int, Button> _sidebarItems = new();
    private int _lastHighlightedIndex = -1;

    private CancellationTokenSource? _sidebarCts;
    private CancellationTokenSource? _catalogCts;

    private Point _startPoint;
    private double _scrollHorizontalOffset;
    private double _scrollVerticalOffset;

    private readonly string _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public string? InitialPath { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _presenter = new SimpleViewerPresenter(this);

        LoadSettings();

        RenderOptions.SetBitmapScalingMode(ViewContainer, BitmapScalingMode.HighQuality);
        this.Focusable = true;

        this.Loaded += async (s, e) =>
        {
            this.Focus();
            if (!string.IsNullOrEmpty(InitialPath)) await OpenNewSourceAsync(Path.GetFullPath(InitialPath));
        };

        this.Closing += (s, e) => SaveSettings();
    }

    #region 設定永続化

    // MainWindow.xaml.cs の中
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            string json = File.ReadAllText(_settingsPath);

            // 型安全な読み込み
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            if (s == null) return;

            // DisplayMode を直接セット可能
            _presenter.SetDisplayMode(s.DisplayMode);

            if (Enum.TryParse(s.ZoomMode, out ZoomMode zMode)) _currentZoomMode = zMode;
            _zoomFactor = s.ZoomFactor;
            SidebarColumn.Width = s.IsSidebarVisible ? new GridLength(200) : new GridLength(0);
            this.Width = s.WindowWidth;
            this.Height = s.WindowHeight;
            ApplyZoom();
        }
        catch { /* 読み込み失敗時はデフォルト設定を使用 */ }
    }

    private void SaveSettings()
    {
        try
        {
            var s = new AppSettings
            {
                DisplayMode = _presenter.CurrentDisplayMode, // そのまま代入
                ZoomMode = _currentZoomMode.ToString(),
                ZoomFactor = _zoomFactor,
                IsSidebarVisible = SidebarColumn.Width.Value > 0,
                WindowWidth = this.ActualWidth,
                WindowHeight = this.ActualHeight
            };
            // インデント付きで保存（人間が読みやすいように）
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    #endregion

    #region IView 実装

    public void SetImages(BitmapSource? left, BitmapSource? right)
    {
        ImageLeft.Source = left;
        ImageRight.Source = right;
        ImageRight.Visibility = (right == null) ? Visibility.Collapsed : Visibility.Visible;
        MainScrollViewer.ScrollToHome();
        UpdateZoomByMode();
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

    #region ズーム制御

    private void UpdateZoomByMode()
    {
        if (ImageLeft.Source == null || MainScrollViewer.ActualWidth <= 0) { ApplyZoom(); return; }

        double totalW = ImageLeft.Source.Width + (ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Width ?? 0) : 0);
        double maxH = Math.Max(ImageLeft.Source.Height, ImageRight.Visibility == Visibility.Visible ? (ImageRight.Source?.Height ?? 0) : 0);

        double viewW = MainScrollViewer.ActualWidth - 4;
        double viewH = MainScrollViewer.ActualHeight - 4;

        if (_currentZoomMode == ZoomMode.FitWidth) _zoomFactor = viewW / totalW;
        else if (_currentZoomMode == ZoomMode.FitPage) _zoomFactor = Math.Min(viewW / totalW, viewH / maxH);

        ApplyZoom();
    }

    private void ApplyZoom()
    {
        ViewContainer.LayoutTransform = new ScaleTransform(_zoomFactor, _zoomFactor);
        string suffix = _currentZoomMode switch { ZoomMode.FitWidth => " (幅)", ZoomMode.FitPage => " (全)", _ => "" };
        ZoomText.Text = $"{(_zoomFactor * 100):0}%{suffix}";
    }

    private void ZoomIn() { _currentZoomMode = ZoomMode.Manual; _zoomFactor = Math.Min(_zoomFactor + ZoomStep, 10.0); ApplyZoom(); }
    private void ZoomOut() { _currentZoomMode = ZoomMode.Manual; _zoomFactor = Math.Max(_zoomFactor - ZoomStep, 0.1); ApplyZoom(); }
    private void ResetZoom() { _currentZoomMode = ZoomMode.Manual; _zoomFactor = 1.0; ApplyZoom(); }

    #endregion

    #region サイドバー & ハイライト (セッション管理)

    private async Task BuildSidebarAsync(CancellationToken token)
    {
        int total = (int)PageSlider.Maximum + 1;
        var sessionCts = _sidebarCts;

        for (int i = 0; i < total; i++)
        {
            if (token.IsCancellationRequested) return;
            var thumb = await _presenter.GetThumbnailAsync(i, 160);
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
        }, System.Windows.Threading.DispatcherPriority.Send);
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

    #region メインメニュー / ボタンイベント (復元済み)

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

    private void MenuToggleMode_Click(object sender, RoutedEventArgs e) => _presenter.ToggleDisplayModeAsync();

    private void MenuFitWidth_Click(object sender, RoutedEventArgs e) { _currentZoomMode = ZoomMode.FitWidth; UpdateZoomByMode(); }

    private void MenuFitPage_Click(object sender, RoutedEventArgs e) { _currentZoomMode = ZoomMode.FitPage; UpdateZoomByMode(); }

    private void MenuResetZoom_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private void MenuZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();

    private void MenuZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();

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
            var thumb = await _presenter.GetThumbnailAsync(i, 200);
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
                case Key.F: MenuFitWidth_Click(null!, null!); e.Handled = true; return;
                case Key.G: MenuFitPage_Click(null!, null!); e.Handled = true; return;
                case Key.D0: case Key.NumPad0: ResetZoom(); e.Handled = true; return;
                case Key.OemPlus: case Key.Add: ZoomIn(); e.Handled = true; return;
                case Key.OemMinus: case Key.Subtract: ZoomOut(); e.Handled = true; return;
            }
        }
        switch (e.Key)
        {
            case Key.Left: case Key.Space: _presenter.NextPageAsync(); e.Handled = true; break;
            case Key.Right: case Key.Back: _presenter.PreviousPageAsync(); e.Handled = true; break;
            case Key.F3: MenuCatalog_Click(null!, null!); e.Handled = true; break;
            case Key.F4: MenuToggleSidebar_Click(null!, null!); e.Handled = true; break;
            case Key.S: MenuToggleMode_Click(null!, null!); e.Handled = true; break;
            case Key.Escape:
                if (CatalogOverlay.Visibility == Visibility.Visible) CloseCatalog_Click(null!, null!);
                else ResetZoom();
                e.Handled = true; break;
        }
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0) ZoomIn(); else ZoomOut();
            e.Handled = true;
        }
        else if (_currentZoomMode != ZoomMode.Manual || _zoomFactor <= 1.05)
        {
            if (e.Delta > 0) _presenter.PreviousPageAsync(); else _presenter.NextPageAsync();
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
            if (files?.Length > 0) await OpenNewSourceAsync(files[0]);
            this.Focus();
        }
    }

    private bool IsDescendantOfScrollBar(DependencyObject element)
    {
        while (element != null) { if (element is ScrollBar) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }

    private void MainScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateZoomByMode();

    private void PageSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _presenter.JumpToPageAsync((int)PageSlider.Value);
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