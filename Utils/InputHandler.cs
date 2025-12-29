using System.Windows;
using System.Windows.Input;

namespace SimpleViewer
{
    /// <summary>
    /// キーボードやマウスホイール等の入力イベントを MainWindow から切り出して処理するヘルパークラス。
    /// - 実際の UI 操作（ページ移動、ズーム等）はデリゲートで MainWindow に委譲する。
    /// - 非同期操作は可能な限り await して呼び出すか、同期 API 側では fire-and-forget で実行する。
    /// </summary>
    public class InputHandler
    {
        private readonly Func<Task> _nextPage;
        private readonly Func<Task> _previousPage;
        private readonly Func<Task> _openCatalog;
        private readonly Func<Task> _openFile; // ファイルを開くデリゲート (Ctrl+O)
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

        public InputHandler(
            Func<Task> nextPage,
            Func<Task> previousPage,
            Func<Task> openFile,
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
        /// 非同期版: PreviewKeyDown の処理を行う。処理した場合は true を返す。
        /// - Ctrl 修飾キーと単独キーの両方に対して設定されたショートカットを処理する。
        /// - 長時間処理が発生し得る操作（ファイルダイアログの表示など）は await して呼び出す。
        /// </summary>
        /// <param name="e">キーボードイベント引数</param>
        public async Task<bool> HandlePreviewKeyDownAsync(KeyEventArgs e)
        {
            // Ctrl 系ショートカットの処理
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                switch (e.Key)
                {
                    case Key.O:
                        // Ctrl+O: ファイルを開く（ダイアログ）
                        await _openFile();
                        e.Handled = true;
                        return true;
                    case Key.W:
                        // Ctrl+W: 本アプリでは用いないがイベントを消費する
                        e.Handled = true;
                        return true;
                    case Key.F:
                        // Ctrl+F: 幅にフィット
                        _fitWidth();
                        e.Handled = true;
                        return true;
                    case Key.G:
                        // Ctrl+G: ページにフィット
                        _fitPage();
                        e.Handled = true;
                        return true;
                    case Key.D0:
                    case Key.NumPad0:
                        // Ctrl+0: ズームリセット
                        _resetZoom();
                        e.Handled = true;
                        return true;
                    case Key.OemPlus:
                    case Key.Add:
                        // Ctrl++: ズームイン
                        _zoomIn();
                        e.Handled = true;
                        return true;
                    case Key.OemMinus:
                    case Key.Subtract:
                        // Ctrl+-: ズームアウト
                        _zoomOut();
                        e.Handled = true;
                        return true;
                }
            }

            // Ctrl 非押下時の通常キー処理
            switch (e.Key)
            {
                case Key.Left:
                case Key.Space:
                    // 左矢印またはスペースで次ページへ（UI設計上の割当）
                    await _nextPage();
                    e.Handled = true;
                    return true;
                case Key.Right:
                case Key.Back:
                    // 右矢印またはBackで前ページへ
                    await _previousPage();
                    e.Handled = true;
                    return true;
                case Key.F3:
                    // F3: カタログを開く
                    await _openCatalog();
                    e.Handled = true;
                    return true;
                case Key.F4:
                    // F4: サイドバーの表示切替
                    _toggleSidebar();
                    e.Handled = true;
                    return true;
                case Key.S:
                    // S: 表示モード切替
                    await _toggleMode();
                    e.Handled = true;
                    return true;
                case Key.Escape:
                    // Esc: カタログが開いていれば閉じる、そうでなければズームリセット
                    if (_getCatalogVisibility() == Visibility.Visible) _closeCatalog();
                    else _resetZoom();
                    e.Handled = true;
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 同期版: PreviewKeyDown の処理を行う。非同期の操作は fire-and-forget で実行する。
        /// - MainWindow など同期 API から直接呼び出すための互換ラッパー。
        /// - 非同期処理は例外処理が行われない点に注意（UI 側で必要なら await 版を使用）。
        /// </summary>
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

            // Control 非押下時の通常キー処理
            switch (e.Key)
            {
                case Key.Left:
                case Key.Space:
                    _ = _nextPage(); // fire-and-forget
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
        /// マウスホイールイベントの処理を行う。処理した場合は true を返す。
        /// - Ctrl 押下時はズーム操作に割り当てる。
        /// - それ以外は _shouldNavigateOnWheel によりページ移動に割り当てるか判断する。
        /// </summary>
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
}
