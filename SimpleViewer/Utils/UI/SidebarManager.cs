using SimpleViewer.Models.Configuration;
using SimpleViewer.Presenters;
using SimpleViewer.Presenters.Controllers;
using SimpleViewer.Utils.Trees;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// サイドバー（サムネイル表示およびツリー表示）を管理するクラス。
    /// ThumbnailController、TreeBuilder、SidebarSizeWatcher を合成し、
    /// サイドバーおよびツリーの構築・更新・操作イベントの橋渡しを行います。
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
        // ツリービュー（オプション。なければ実行時に探索する）
        private TreeView? _treeView;

        // サムネイル生成・管理を担うコントローラ
        private readonly ThumbnailController _thumbnailController;
        // サイドバー幅の変化を監視してデバウンスするユーティリティ
        private readonly SidebarSizeWatcher _sizeWatcher;
        // パス文字列群からツリーモデルを構築するユーティリティ
        private readonly TreeBuilder _treeBuilder;
        // TreeView のハイライト処理を担当するユーティリティ
        private readonly TreeViewHighlighter _treeViewHighlighter;

        // EnsureSidebar のデバウンス用
        private CancellationTokenSource? _ensureCts;

        /// <summary>
        /// SidebarManager を初期化します（設定オブジェクトパターン）。
        /// </summary>
        /// <param name="config">サイドバーマネージャーの設定</param>
        public SidebarManager(SidebarManagerConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            _presenter = config.Presenter ?? throw new ArgumentNullException(nameof(config.Presenter));
            _thumbnailSidebar = config.ThumbnailSidebar ?? throw new ArgumentNullException(nameof(config.ThumbnailSidebar));
            _dispatcher = config.Dispatcher ?? throw new ArgumentNullException(nameof(config.Dispatcher));
            _jumpToPageCallback = config.JumpToPageCallback ?? throw new ArgumentNullException(nameof(config.JumpToPageCallback));
            _treeView = config.TreeView;

            // 関連コンポーネントを初期化
            _thumbnailController = new ThumbnailController(
                _presenter, 
                _thumbnailSidebar, 
                _dispatcher, 
                _jumpToPageCallback, 
                config.FocusWindowCallback, 
                config.ThumbnailButtonStyle);

            _treeBuilder = new TreeBuilder(_dispatcher, OnTreeItemInvoked);
            _sizeWatcher = new SidebarSizeWatcher(_thumbnailSidebar, OnDebouncedWidthChangedAsync);
            _treeViewHighlighter = new TreeViewHighlighter(_dispatcher);
        }

        /// <summary>
        /// SidebarManager を初期化します（従来の互換性維持用コンストラクタ）。
        /// </summary>
        /// <param name="presenter">Presenter（ページ移動等を行うため）</param>
        /// <param name="thumbnailSidebar">サムネイルを表示する ItemsControl</param>
        /// <param name="treeView">ツリービュー（任意。null の場合は自動検索する）</param>
        /// <param name="dispatcher">UI スレッドにアクセスするための Dispatcher</param>
        /// <param name="jumpToPageCallback">サムネイルやツリーからページジャンプするためのコールバック</param>
        /// <param name="focusWindowCallback">カタログ/ツリー操作後にウィンドウへフォーカスを戻すコールバック</param>
        /// <param name="thumbnailButtonStyle">サムネイルボタンに適用するスタイル</param>
        public SidebarManager(
            SimpleViewerPresenter presenter, 
            ItemsControl thumbnailSidebar, 
            TreeView? treeView, 
            Dispatcher dispatcher, 
            Func<int, Task> jumpToPageCallback, 
            Action focusWindowCallback, 
            Style thumbnailButtonStyle)
            : this(new SidebarManagerConfig
            {
                Presenter = presenter,
                ThumbnailSidebar = thumbnailSidebar,
                TreeView = treeView,
                Dispatcher = dispatcher,
                JumpToPageCallback = jumpToPageCallback,
                FocusWindowCallback = focusWindowCallback,
                ThumbnailButtonStyle = thumbnailButtonStyle
            })
        {
        }

        /// <summary>
        /// TreeBuilder からのアイテム選択コールバック。
        /// ページ移動処理を呼び出すラッパーです。
        /// </summary>
        /// <param name="pageIndex">ジャンプ先ページのインデックス（0 始まり）</param>
        private void OnTreeItemInvoked(int pageIndex)
        {
            // 非同期処理を統一的に扱うヘルパーへ委譲する
            _ = InvokePageSelectionAsync(pageIndex);
        }

        /// <summary>
        /// サイドバー幅変更のデバウンス完了時に呼ばれる。
        /// 必要に応じて高解像度サムネイルを再取得します。
        /// </summary>
        /// <param name="width">確定したサイドバー幅（ピクセル）</param>
        private async Task OnDebouncedWidthChangedAsync(int width)
        {
            var cts = new CancellationTokenSource();
            try
            {
                await _thumbnailController.RefreshAsync(width, cts.Token);
            }
            catch (OperationCanceledException) 
            { 
                // キャンセル時は何もしない
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnDebouncedWidthChangedAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// サイドバーが必要な場合にサムネイルを構築します。
        /// totalPages が 0 以下であればサイドバーとツリーをクリアします。
        /// デバッグや高速スクロール時の多重呼び出しを避けるため内部でデバウンスします。
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

            // 既存のデバウンス要求をキャンセルし、新たに短い遅延を設ける
            _ensureCts?.Cancel();
            _ensureCts = new CancellationTokenSource();
            var token = _ensureCts.Token;

            try
            {
                // 短い遅延で連続した呼び出しをまとめる
                await Task.Delay(120, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // 実際の構築は Dispatcher 経由で呼ぶ（ThumbnailController 内で UI 更新を行う）
            try
            {
                await _thumbnailController.BuildAsync(totalPages, currentPageIndex, Math.Max(32, _thumbnailSidebar.ActualWidth - 12)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureSidebarAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 指定インデックスのサムネイルをハイライトします。
        /// </summary>
        /// <param name="index">ハイライトするページのインデックス（0 始まり）</param>
        public void HighlightThumbnail(int index)
        {
            _thumbnailController.Highlight(index);
            // ツリー表示側も可能なら同期してハイライトする
            HighlightTreeItem(index);
        }

        /// <summary>
        /// サイドバー（サムネイル領域）をクリアします。
        /// </summary>
        public void ClearSidebar() => _thumbnailController.Clear();

        /// <summary>
        /// 指定されたパスの一覧からツリーを構築します。
        /// rootName を指定するとルートノード名を置き換えます。
        /// </summary>
        /// <param name="paths">構築対象のパス一覧（順序はページインデックスに対応）</param>
        /// <param name="pathToPageIndex">パスからページインデックスへのマッピング（オプション）</param>
        /// <param name="rootName">ルートノードに表示する名前（オプション）</param>
        public void BuildTree(IEnumerable<string> paths, IDictionary<string, int>? pathToPageIndex = null, string? rootName = null)
        {
            if (!TryEnsureTreeView()) return;

            ClearTree();
            _treeBuilder.Build(_treeView!, paths, pathToPageIndex, rootName);
        }

        /// <summary>
        /// ツリーをクリアします。
        /// </summary>
        public void ClearTree()
        {
            if (!TryEnsureTreeView()) return;
            _treeView!.Items.Clear();
        }

        /// <summary>
        /// 指定インデックスに対応する TreeViewItem をハイライトします。
        /// </summary>
        /// <param name="index">ハイライト対象のページインデックス</param>
        public void HighlightTreeItem(int index)
        {
            if (!TryEnsureTreeView()) return;
            _treeViewHighlighter.HighlightItem(_treeView, index);
        }

        /// <summary>
        /// ページ選択に関わる一連の処理を統一して扱います。
        /// Presenter へのジャンプを await して例外を捕捉し、サムネイルハイライトを行います。
        /// </summary>
        /// <param name="pageIndex">ジャンプ先ページのインデックス（0 始まり）</param>
        private async Task InvokePageSelectionAsync(int pageIndex)
        {
            // ツリークリック時は即座にハイライトを実行
            _thumbnailController.Highlight(pageIndex);

            try
            {
                await _jumpToPageCallback.Invoke(pageIndex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InvokePageSelectionAsync failed: {ex.Message}");
            }

            // 注意: ツリークリック時はTreeViewにフォーカスを残すため、
            // フォーカス復帰コールバックは呼び出さない。
            // これによりTreeViewItemの選択ハイライトが濃い青色のまま維持される。
        }

        /// <summary>
        /// _treeView が未設定の場合にアプリケーションから探して設定します。
        /// </summary>
        /// <returns>TreeView が見つかれば true、それ以外は false</returns>
        private bool TryEnsureTreeView()
        {
            if (_treeView != null) return true;

            _treeView = VisualTreeSearcher.FindInApplication<TreeView>("SidebarTreeView");
            
            return _treeView != null;
        }

        /// <summary>
        /// ThumbnailController への参照を公開します（カタログ表示でサムネイルを共有するため）。
        /// </summary>
        public ThumbnailController ThumbnailController => _thumbnailController;
    }
}
