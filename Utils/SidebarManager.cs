using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Diagnostics;

namespace SimpleViewer.Utils
{
    /// <summary>
    /// サイドバー（サムネイル表示およびツリー表示）の管理を行うユーティリティクラス。
    /// - ThumbnailController / TreeBuilder / SidebarSizeWatcher を合成して UI の更新を行う
    /// - MainWindow の具体的な UI 要素への依存は必要最小限に留める
    /// - Dispatcher を使って UI スレッドでの更新を行う
    /// </summary>
    public class SidebarManager
    {
        // Presenter（ページ移動や画像取得を行う主要ロジック）
        private readonly SimpleViewerPresenter _presenter;
        // サムネイルを表示する ItemsControl（XAML 側のコントロール）
        private readonly ItemsControl _thumbnailSidebar;
        // UI スレッドに対する Dispatcher（必要に応じて UI 更新を実行する）
        private readonly Dispatcher _dispatcher;
        // サムネイルやツリー項目からページジャンプするためのコールバック
        private readonly Func<int, Task> _jumpToPageCallback;
        // カタログ操作後にウィンドウへフォーカスを戻すためのコールバック
        private readonly Action _focusWindowCallback;
        // サムネイルボタンに適用するスタイル
        private readonly Style _thumbnailButtonStyle;
        // ツリービュー（オプション。なければ実行時に探索する）
        private TreeView? _treeView;

        // サムネイル生成・管理を担うコントローラ
        private ThumbnailController _thumbnailController;
        // サイドバー幅の変化を監視してデバウンスするユーティリティ
        private SidebarSizeWatcher _sizeWatcher;
        // パス文字列群からツリーモデルを構築するユーティリティ
        private TreeBuilder _treeBuilder;

        /// <summary>
        /// SidebarManager を初期化します。
        /// 各ユーティリティを生成してイベント購読を行います。
        /// </summary>
        /// <param name="presenter">Presenter（ページ移動等を行うため）</param>
        /// <param name="thumbnailSidebar">サムネイルを表示する ItemsControl</param>
        /// <param name="treeView">ツリービュー（任意。null の場合は自動検索する）</param>
        /// <param name="dispatcher">UI スレッドにアクセスするための Dispatcher</param>
        /// <param name="jumpToPageCallback">サムネイルやツリーからページジャンプするためのコールバック</param>
        /// <param name="focusWindowCallback">カタログ/ツリー操作後にウィンドウへフォーカスを戻すコールバック</param>
        /// <param name="thumbnailButtonStyle">サムネイルボタンに適用するスタイル</param>
        public SidebarManager(SimpleViewerPresenter presenter, ItemsControl thumbnailSidebar, TreeView? treeView, Dispatcher dispatcher, Func<int, Task> jumpToPageCallback, Action focusWindowCallback, Style thumbnailButtonStyle)
        {
            _presenter = presenter;
            _thumbnailSidebar = thumbnailSidebar;
            _dispatcher = dispatcher;
            _jumpToPageCallback = jumpToPageCallback;
            _focusWindowCallback = focusWindowCallback;
            _thumbnailButtonStyle = thumbnailButtonStyle;

            _treeView = treeView;

            // 関連コンポーネントを初期化
            // ThumbnailController: サムネイルの構築・更新の責務
            _thumbnailController = new ThumbnailController(_presenter, _thumbnailSidebar, _dispatcher, _jumpToPageCallback, _focusWindowCallback, _thumbnailButtonStyle);
            // TreeBuilder: パス一覧から TreeView を構築する責務
            _treeBuilder = new TreeBuilder(_dispatcher, OnTreeItemInvoked);
            // SidebarSizeWatcher: サイドバー幅変更のデバウンス通知を行う
            _sizeWatcher = new SidebarSizeWatcher(_thumbnailSidebar, async (w) => await OnDebouncedWidthChanged(w));
        }

        /// <summary>
        /// TreeBuilder からのアイテム選択コールバック。
        /// Presenter へページ移動を依頼し、サムネイルをハイライトする。
        /// </summary>
        /// <param name="pageIndex">ジャンプ先のページインデックス（0 始まり）</param>
        private void OnTreeItemInvoked(int pageIndex)
        {
            // 非同期操作は fire-and-forget で呼び出す（UI側でハンドリング）
            _ = _jumpToPageCallback.Invoke(pageIndex);
            // 操作完了後にウィンドウへフォーカスを戻す
            _focusWindowCallback?.Invoke();
            // サムネイルをハイライトして現在のページを示す
            HighlightThumbnail(pageIndex);
        }

        /// <summary>
        /// サイドバー幅変更のデバウンス完了時に呼ばれる。必要に応じて高解像度サムネイルを再取得する。
        /// </summary>
        /// <param name="width">確定したサイドバーの幅（ピクセル）</param>
        /// <returns>非同期操作の完了を表す Task</returns>
        private async Task OnDebouncedWidthChanged(int width)
        {
            // ThumbnailController 側での即時更新は期待しており、ここでは高解像度版の再取得を試みる
            var cts = new CancellationTokenSource();
            try
            {
                await _thumbnailController.RefreshAsync(width, cts.Token);
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// サイドバーが必要な場合にサムネイルを構築する。
        /// totalPages が 0 以下ならサイドバーとツリーをクリアする。
        /// </summary>
        /// <param name="totalPages">総ページ数</param>
        /// <param name="currentPageIndex">現在のページインデックス（0 始まり）</param>
        /// <returns>非同期操作の完了を表す Task</returns>
        public async Task EnsureSidebarAsync(int totalPages, int currentPageIndex)
        {
            if (totalPages <= 0)
            {
                ClearSidebar();
                ClearTree();
                return;
            }

            // サムネイルの希望幅はサイドバー幅から算出
            await _thumbnailController.BuildAsync(totalPages, currentPageIndex, Math.Max(32, _thumbnailSidebar.ActualWidth - 12));
        }

        /// <summary>
        /// 指定インデックスのサムネイルをハイライトする。
        /// ThumbnailController に処理を委譲する薄いラッパー。
        /// </summary>
        /// <param name="index">ハイライトするページのインデックス（0 始まり）</param>
        public void HighlightThumbnail(int index) => _thumbnailController.Highlight(index);

        /// <summary>
        /// サイドバー（サムネイル領域）をクリアする。
        /// ThumbnailController に処理を委譲する。
        /// </summary>
        public void ClearSidebar() => _thumbnailController.Clear();

        /// <summary>
        /// 指定されたパスの一覧からツリーを構築する。
        /// rootName を指定するとルートノード名を置き換える。
        /// </summary>
        /// <param name="paths">構築対象のパス一覧（順序はページインデックスに対応していること）</param>
        /// <param name="pathToPageIndex">オプション: パスからページインデックスへのマッピング</param>
        /// <param name="rootName">オプション: ルートノードに表示する名前</param>
        public void BuildTree(IEnumerable<string> paths, IDictionary<string, int>? pathToPageIndex = null, string? rootName = null)
        {
            // treeview が渡されていない場合はアプリケーションから探す
            if (_treeView == null)
            {
                var mw = Application.Current?.MainWindow;
                if (mw != null) _treeView = mw.FindName("SidebarTreeView") as TreeView ?? FindChildByName<TreeView>(mw, "SidebarTreeView");
                if (_treeView == null && Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        _treeView = w.FindName("SidebarTreeView") as TreeView ?? FindChildByName<TreeView>(w, "SidebarTreeView");
                        if (_treeView != null) break;
                    }
                }
            }

            if (_treeView == null) return;

            // 既存のツリーをクリアしてから構築
            ClearTree();
            _treeBuilder.Build(_treeView, paths, pathToPageIndex, rootName);
        }

        /// <summary>
        /// ツリーをクリアし、イベントハンドラの解除などクリーンアップを行う。
        /// TreeView が提供するイベントをすべて解除してから Items をクリアする。
        /// </summary>
        public void ClearTree()
        {
            // treeview が見つからない場合は再探索を試みる
            if (_treeView == null)
            {
                var mw = Application.Current?.MainWindow;
                if (mw != null) _treeView = mw.FindName("SidebarTreeView") as TreeView ?? FindChildByName<TreeView>(mw, "SidebarTreeView");
                if (_treeView == null && Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        _treeView = w.FindName("SidebarTreeView") as TreeView ?? FindChildByName<TreeView>(w, "SidebarTreeView");
                        if (_treeView != null) break;
                    }
                }
            }

            if (_treeView != null)
            {
                // 再帰的に TreeViewItem のイベントを解除するヘルパー
                void DetachRecursive(ItemCollection items)
                {
                    foreach (var it in items.OfType<TreeViewItem>())
                    {
                        // 各種イベントハンドラを解除してリークを防ぐ
                        it.Selected -= TreeViewItem_Selected;
                        it.MouseDoubleClick -= TreeViewItem_MouseDoubleClick;
                        it.PreviewMouseLeftButtonUp -= TreeViewItem_MouseLeftButtonUp;
                        if (it.Items.Count > 0) DetachRecursive(it.Items);
                    }
                }

                DetachRecursive(_treeView.Items);
                _treeView.SelectedItemChanged -= TreeView_SelectedItemChanged;
                _treeView.Items.Clear();
            }
        }

        // 以下はツリーの UI イベントハンドラ群: どの場合もページジャンプとフォーカス復帰、サムネイルハイライトを行う
        /// <summary>
        /// TreeViewItem のマウス左ボタンが離されたときのハンドラ
        /// </summary>
        private void TreeViewItem_MouseLeftButtonUp(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // マウスの左ボタンが離されたときに選択された項目のページへジャンプする
            if (sender is TreeViewItem tvi)
            {
                if (tvi.Tag is int idx)
                {
                    _ = _jumpToPageCallback.Invoke(idx);
                    _focusWindowCallback?.Invoke();
                    HighlightThumbnail(idx);
                }
            }
        }

        /// <summary>
        /// TreeView の選択項目が変更されたときのハンドラ
        /// </summary>
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 選択項目が変更されたときのハンドラ
            if (e.NewValue is TreeViewItem tvi)
            {
                if (tvi.Tag is int idx)
                {
                    _ = _jumpToPageCallback.Invoke(idx);
                    _focusWindowCallback?.Invoke();
                    HighlightThumbnail(idx);
                    return;
                }
            }
        }

        /// <summary>
        /// TreeViewItem が選択されたときのハンドラ（キーボード等での選択も含む）
        /// </summary>
        private void TreeViewItem_Selected(object? sender, RoutedEventArgs e)
        {
            // キーボード操作等で項目が選択されたときに呼ばれる
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _ = _jumpToPageCallback.Invoke(idx);
                _focusWindowCallback?.Invoke();
                HighlightThumbnail(idx);
            }
        }

        /// <summary>
        /// TreeViewItem のダブルクリック時のハンドラ
        /// </summary>
        private void TreeViewItem_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // ダブルクリックでページにジャンプする
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _ = _jumpToPageCallback.Invoke(idx);
                _focusWindowCallback?.Invoke();
                HighlightThumbnail(idx);
            }
        }

        /// <summary>
        /// ビジュアルツリー検索ヘルパー: 名前付き要素を探索する
        /// </summary>
        /// <typeparam name="T">期待する要素の型</typeparam>
        /// <param name="parent">探索を開始する親要素</param>
        /// <param name="name">探す要素の Name プロパティ値</param>
        /// <returns>見つかった要素（見つからなければ null）</returns>
        private static T? FindChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe)
                {
                    if (fe.Name == name && child is T t) return t;
                }

                var result = FindChildByName<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }
}