using System.Windows;
using System.Windows.Input;

namespace SimpleViewer.Presenters.Handlers;

/// <summary>
/// キー / マウス入力のハンドリングを集約するハンドラークラス。
/// MainWindow の PreviewKeyDown / PreviewMouseWheel から呼び出され、
/// 主要な操作（ページ移動、ズーム、カタログ開閉など）を処理します。
/// </summary>
public class InputHandler
{
    private readonly Func<Task> _nextPage;
    private readonly Func<Task> _previousPage;
    private readonly Func<Task> _openCatalog;
    private readonly Func<Task> _openFile;
    private readonly Action _closeSource;
    private readonly Action _toggleSidebar;
    private readonly Func<Task> _toggleMode;
    private readonly Action _resetZoom;
    private readonly Action _fitWidth;
    private readonly Action _fitPage;
    private readonly Action _zoomIn;
    private readonly Action _zoomOut;
    private readonly Action _focusWindow;
    private readonly Func<Visibility> _getCatalogVisibility;
    private readonly Action _closeCatalog;
    private readonly Func<bool> _shouldNavigateOnWheel;

    /// <summary>
    /// コンストラクタ: 必要なコールバックを注入します。
    /// </summary>
    public InputHandler(
        Func<Task> nextPage,
        Func<Task> previousPage,
        Func<Task> openFile,
        Action closeSource,
        Func<Task> openCatalog,
        Action toggleSidebar,
        Func<Task> toggleMode,
        Action resetZoom,
        Action fitWidth,
        Action fitPage,
        Action zoomIn,
        Action zoomOut,
        Action focusWindow,
        Func<Visibility> getCatalogVisibility,
        Action closeCatalog,
        Func<bool> shouldNavigateOnWheel)
    {
        _nextPage = nextPage ?? throw new ArgumentNullException(nameof(nextPage));
        _previousPage = previousPage ?? throw new ArgumentNullException(nameof(previousPage));
        _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
        _closeSource = closeSource ?? throw new ArgumentNullException(nameof(closeSource));
        _openCatalog = openCatalog ?? throw new ArgumentNullException(nameof(openCatalog));
        _toggleSidebar = toggleSidebar ?? throw new ArgumentNullException(nameof(toggleSidebar));
        _toggleMode = toggleMode ?? throw new ArgumentNullException(nameof(toggleMode));
        _resetZoom = resetZoom ?? throw new ArgumentNullException(nameof(resetZoom));
        _fitWidth = fitWidth ?? throw new ArgumentNullException(nameof(fitWidth));
        _fitPage = fitPage ?? throw new ArgumentNullException(nameof(fitPage));
        _zoomIn = zoomIn ?? throw new ArgumentNullException(nameof(zoomIn));
        _zoomOut = zoomOut ?? throw new ArgumentNullException(nameof(zoomOut));
        _focusWindow = focusWindow ?? throw new ArgumentNullException(nameof(focusWindow));
        _getCatalogVisibility = getCatalogVisibility ?? throw new ArgumentNullException(nameof(getCatalogVisibility));
        _closeCatalog = closeCatalog ?? throw new ArgumentNullException(nameof(closeCatalog));
        _shouldNavigateOnWheel = shouldNavigateOnWheel ?? throw new ArgumentNullException(nameof(shouldNavigateOnWheel));
    }

    /// <summary>
    /// PreviewKeyDown の非同期ハンドル。処理した場合は true を返します。
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    public async Task<bool> HandlePreviewKeyDownAsync(KeyEventArgs e)
    {
        // Ctrl キー組み合わせのショートカット
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.O:
                    await _openFile();
                    e.Handled = true;
                    return true;
                case Key.W:
                    _closeSource();
                    e.Handled = true;
                    return true;
                case Key.F:
                    _fitWidth();
                    e.Handled = true;
                    return true;
                case Key.G:
                    _fitPage();
                    e.Handled = true;
                    return true;
                case Key.D0:
                case Key.NumPad0:
                    _resetZoom();
                    e.Handled = true;
                    return true;
                case Key.OemPlus:
                case Key.Add:
                    _zoomIn();
                    e.Handled = true;
                    return true;
                case Key.OemMinus:
                case Key.Subtract:
                    _zoomOut();
                    e.Handled = true;
                    return true;
            }
        }

        // 単一キーでの操作
        switch (e.Key)
        {
            case Key.Left:
            case Key.Space:
                await _nextPage();
                e.Handled = true;
                return true;
            case Key.Right:
            case Key.Back:
                await _previousPage();
                e.Handled = true;
                return true;
            case Key.F3:
                await _openCatalog();
                e.Handled = true;
                return true;
            case Key.F4:
                _toggleSidebar();
                e.Handled = true;
                return true;
            case Key.S:
                await _toggleMode();
                e.Handled = true;
                return true;
            case Key.Escape:
                if (_getCatalogVisibility() == Visibility.Visible) _closeCatalog();
                else _resetZoom();
                e.Handled = true;
                return true;
        }

        return false;
    }

    /// <summary>
    /// PreviewKeyDown の同期版（fire-and-forget 実行のユースケース向け）。
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    public bool HandlePreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.O:
                    _ = _openFile();
                    e.Handled = true;
                    return true;
                case Key.W:
                    _closeSource();
                    e.Handled = true;
                    return true;
                case Key.F:
                    _fitWidth();
                    e.Handled = true;
                    return true;
                case Key.G:
                    _fitPage();
                    e.Handled = true;
                    return true;
                case Key.D0:
                case Key.NumPad0:
                    _resetZoom();
                    e.Handled = true;
                    return true;
                case Key.OemPlus:
                case Key.Add:
                    _zoomIn();
                    e.Handled = true;
                    return true;
                case Key.OemMinus:
                case Key.Subtract:
                    _zoomOut();
                    e.Handled = true;
                    return true;
            }
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Space:
                _ = _nextPage();
                e.Handled = true;
                return true;
            case Key.Right:
            case Key.Back:
                _ = _previousPage();
                e.Handled = true;
                return true;
            case Key.F3:
                _ = _openCatalog();
                e.Handled = true;
                return true;
            case Key.F4:
                _toggleSidebar();
                e.Handled = true;
                return true;
            case Key.S:
                _ = _toggleMode();
                e.Handled = true;
                return true;
            case Key.Escape:
                if (_getCatalogVisibility() == Visibility.Visible) _closeCatalog();
                else _resetZoom();
                e.Handled = true;
                return true;
        }

        return false;
    }

    /// <summary>
    /// PreviewMouseWheel のハンドリング。
    /// Ctrl キーが押されている場合はズーム、そうでなければページ遷移を行います（設定に応じて）。
    /// </summary>
    /// <param name="e">MouseWheelEventArgs</param>
    public bool HandlePreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Delta > 0) _zoomIn(); else _zoomOut();
            e.Handled = true;
            return true;
        }
        else if (_shouldNavigateOnWheel())
        {
            if (e.Delta > 0) _ = _previousPage(); else _ = _nextPage();
            e.Handled = true;
            return true;
        }

        return false;
    }
}
