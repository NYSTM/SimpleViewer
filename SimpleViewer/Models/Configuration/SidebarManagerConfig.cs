using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Models.Configuration
{
    /// <summary>
    /// SidebarManager の初期化パラメータを保持する設定クラス。
    /// コンストラクタの引数が多い場合に可読性を向上させます。
    /// </summary>
    public class SidebarManagerConfig
    {
        /// <summary>
        /// Presenter（ページ移動や画像取得を行う主要ロジック）
        /// </summary>
        public required SimpleViewerPresenter Presenter { get; init; }

        /// <summary>
        /// サムネイルを表示する ItemsControl（XAML 側のコントロール）
        /// </summary>
        public required ItemsControl ThumbnailSidebar { get; init; }

        /// <summary>
        /// ツリービュー（オプション。なければ実行時に探索する）
        /// </summary>
        public TreeView? TreeView { get; init; }

        /// <summary>
        /// UI スレッドに対する Dispatcher（必要に応じて UI 更新を実行する）
        /// </summary>
        public required Dispatcher Dispatcher { get; init; }

        /// <summary>
        /// サムネイルやツリー項目からページジャンプするためのコールバック
        /// </summary>
        public required Func<int, Task> JumpToPageCallback { get; init; }

        /// <summary>
        /// カタログ操作後にウィンドウへフォーカスを戻すためのコールバック
        /// </summary>
        public required Action FocusWindowCallback { get; init; }

        /// <summary>
        /// サムネイルボタンに適用するスタイル
        /// </summary>
        public required Style ThumbnailButtonStyle { get; init; }
    }
}
