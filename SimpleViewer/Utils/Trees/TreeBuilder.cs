using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SimpleViewer.Utils.Comparers;

namespace SimpleViewer.Utils.Trees
{
    /// <summary>
    /// TreeView 用のモデル構築と UI への反映を行うクラス。
    /// バックグラウンドでパス群からツリーモデルを構築し、
    /// UI スレッドで TreeViewItem を生成して TreeView に追加します。
    /// </summary>
    public class TreeBuilder
    {
        // UI スレッド用の Dispatcher
        private readonly Dispatcher _dispatcher;
        // アイテムが選択されたときに呼ばれるコールバック（オプション）
        private readonly Action<int>? _onItemInvoked;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="dispatcher">UI スレッド用 Dispatcher</param>
        /// <param name="onItemInvoked">アイテム選択時に呼ばれるコールバック（インデックス）</param>
        public TreeBuilder(Dispatcher dispatcher, Action<int>? onItemInvoked = null)
        {
            _dispatcher = dispatcher;
            _onItemInvoked = onItemInvoked;
        }

        /// <summary>
        /// 指定されたパス一覧から TreeView を構築します。
        /// バックグラウンドスレッドでモデルを作成し、UI スレッドで TreeViewItem を生成します。
        /// </summary>
        /// <param name="treeView">構築対象の TreeView</param>
        /// <param name="paths">パス一覧（区切り文字は '/' または '\\'）</param>
        /// <param name="pathToPageIndex">パス -> ページインデックスのマッピング（オプション）</param>
        /// <param name="rootName">表示するルート名（省略時は自動でルートを展開）</param>
        public void Build(TreeView treeView, IEnumerable<string> paths, IDictionary<string, int>? pathToPageIndex = null, string? rootName = null)
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;

            _ = Task.Run(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var rootModel = string.IsNullOrEmpty(rootName) ? new TreeNodeModel("__root__") : new TreeNodeModel(rootName);
                    var dict = new Dictionary<string, TreeNodeModel>(StringComparer.OrdinalIgnoreCase);
                    dict[""] = rootModel;

                    // 自然な並び順でソート
                    var naturalComparer = new NaturalStringComparer();
                    foreach (var p in paths.OrderBy(s => s, naturalComparer))
                    {
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(p)) continue;

                        var parts = p.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                        string accum = string.Empty;
                        TreeNodeModel parent = rootModel;

                        for (int i = 0; i < parts.Length; i++)
                        {
                            var part = parts[i];
                            accum = (accum.Length == 0) ? part : (accum + "\\" + part);
                            if (!dict.TryGetValue(accum, out var node))
                            {
                                node = new TreeNodeModel(part);
                                dict[accum] = node;
                                parent.Children[part] = node;
                            }
                            parent = node;
                        }

                        if (pathToPageIndex != null && pathToPageIndex.TryGetValue(p, out var idx))
                        {
                            parent.PageIndex = idx;
                        }
                    }

                    // UI スレッドで TreeViewItem を生成して追加
                    _dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (rootModel.Name == "__root__")
                            {
                                foreach (var child in rootModel.Children.Values)
                                {
                                    var tvi = CreateTreeViewItemFromModel(child);
                                    treeView.Items.Add(tvi);
                                }
                            }
                            else
                            {
                                var rootTvi = CreateTreeViewItemFromModel(rootModel);
                                rootTvi.IsExpanded = true;
                                treeView.Items.Add(rootTvi);
                            }

                            treeView.UpdateLayout();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"TreeBuilder UI Error: {ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TreeBuilder Error: {ex.Message}");
                }
            }, token);
        }

        /// <summary>
        /// TreeNodeModel から TreeViewItem を生成します。
        /// PageIndex があればマウスクリック時のイベントハンドラを登録します。
        /// </summary>
        /// <param name="model">ノードモデル</param>
        /// <returns>生成した TreeViewItem</returns>
        private TreeViewItem CreateTreeViewItemFromModel(TreeNodeModel model)
        {
            var tvi = new TreeViewItem { Header = model.Name, IsExpanded = false };

            // 子要素も自然な並び順でソート
            var naturalComparer = new NaturalStringComparer();
            foreach (var child in model.Children.Values.OrderBy(c => c.Name, naturalComparer))
            {
                tvi.Items.Add(CreateTreeViewItemFromModel(child));
            }

            if (model.PageIndex.HasValue)
            {
                tvi.Tag = model.PageIndex.Value;
                // マウス左ボタンのクリックのみで処理（イベントの重複を防ぐ）
                tvi.PreviewMouseLeftButtonUp += TreeViewItem_MouseLeftButtonUp;
            }

            return tvi;
        }

        /// <summary>
        /// TreeViewItem のマウス左ボタンアップイベントハンドラ。
        /// ツリーアイテムをクリックした際にページ移動を実行します。
        /// </summary>
        private void TreeViewItem_MouseLeftButtonUp(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_onItemInvoked == null) return;
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                // 実際にクリックされたアイテムのみ処理（親要素のイベントは無視）
                if (e.OriginalSource is FrameworkElement element)
                {
                    var clickedItem = FindParentTreeViewItem(element);
                    if (clickedItem == tvi)
                    {
                        // TreeView に明示的にフォーカスを設定して、選択ハイライトを濃い青色にする
                        var treeView = FindParentTreeView(tvi);
                        if (treeView != null)
                        {
                            treeView.Focus();
                            System.Windows.Input.Keyboard.Focus(treeView);
                        }

                        _onItemInvoked(idx);
                        e.Handled = true;
                    }
                }
            }
        }

        /// <summary>
        /// FrameworkElement から親の TreeViewItem を検索します。
        /// </summary>
        /// <param name="element">検索開始要素</param>
        /// <returns>見つかった TreeViewItem または null</returns>
        private TreeViewItem? FindParentTreeViewItem(FrameworkElement element)
        {
            var current = element;
            while (current != null)
            {
                if (current is TreeViewItem tvi) return tvi;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
            }
            return null;
        }

        /// <summary>
        /// TreeViewItem から親の TreeView を検索します。
        /// </summary>
        /// <param name="item">検索開始要素</param>
        /// <returns>見つかった TreeView または null</returns>
        private TreeView? FindParentTreeView(DependencyObject item)
        {
            var current = item;
            while (current != null)
            {
                if (current is TreeView tv) return tv;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
