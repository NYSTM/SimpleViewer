using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Utils
{
    /// <summary>
    /// TreeView 用のモデル構築と UI 変換を担当するクラス。
    /// バックグラウンドスレッドでパス文字列からツリーモデルを構築し、
    /// UI スレッド上で TreeViewItem を生成して TreeView に追加します。
    /// </summary>
    public class TreeBuilder
    {
        // UI スレッドでの操作を行うための Dispatcher
        private readonly Dispatcher _dispatcher;
        // ツリー項目が操作されたときに呼び出すコールバック（ページジャンプ等）
        private readonly Action<int>? _onItemInvoked; // 選択/クリック/ダブルクリック時に呼び出すコールバック

        /// <summary>
        /// コンストラクタ。
        /// </summary>
        /// <param name="dispatcher">UI スレッドでの操作に使用する Dispatcher</param>
        /// <param name="onItemInvoked">項目が選択・クリック・ダブルクリックされたときに呼ばれるコールバック（ページインデックスを受け取る）</param>
        public TreeBuilder(Dispatcher dispatcher, Action<int>? onItemInvoked = null)
        {
            _dispatcher = dispatcher;
            _onItemInvoked = onItemInvoked;
        }

        /// <summary>
        /// 指定されたパス一覧から TreeView の項目を構築して追加します。
        /// モデル構築はバックグラウンドで行い、UI は Dispatcher 経由で更新されます。
        /// </summary>
        /// <param name="treeView">構築先の TreeView</param>
        /// <param name="paths">構築対象のパス一覧（"\" または "/" 区切りを想定）</param>
        /// <param name="pathToPageIndex">オプション: パスからページインデックスへのマッピング</param>
        /// <param name="rootName">オプション: ルートノードの表示名（省略時はトップレベルを展開）</param>
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

                    foreach (var p in paths.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
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

                    // UI スレッドで TreeViewItems を生成して追加する
                    _dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // 既存のハンドラ解除や Items のクリアは呼び出し元で行う想定
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

                            // SelectedItemChanged を一度安全に解除してから登録する
                            treeView.SelectedItemChanged -= TreeView_SelectedItemChanged; // 安全に一旦解除
                            treeView.SelectedItemChanged += TreeView_SelectedItemChanged;

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
        /// モデルから TreeViewItem を再帰的に生成します。
        /// PageIndex が設定されているノードにはイベントハンドラを登録します。
        /// </summary>
        /// <param name="model">変換元のノードモデル</param>
        /// <returns>生成された TreeViewItem</returns>
        private TreeViewItem CreateTreeViewItemFromModel(TreeNodeModel model)
        {
            var tvi = new TreeViewItem { Header = model.Name, IsExpanded = false };

            foreach (var child in model.Children.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                tvi.Items.Add(CreateTreeViewItemFromModel(child));
            }

            if (model.PageIndex.HasValue)
            {
                tvi.Tag = model.PageIndex.Value;
                tvi.Selected += TreeViewItem_Selected;
                tvi.MouseDoubleClick += TreeViewItem_MouseDoubleClick;
                tvi.PreviewMouseLeftButtonUp += TreeViewItem_MouseLeftButtonUp;
            }

            return tvi;
        }

        /// <summary>
        /// TreeViewItem のマウス左ボタンアップ時に呼ばれるハンドラ
        /// </summary>
        private void TreeViewItem_MouseLeftButtonUp(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_onItemInvoked == null) return;
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _onItemInvoked(idx);
            }
        }

        /// <summary>
        /// TreeView の選択項目が変更されたときに呼ばれるハンドラ。
        /// 選択された TreeViewItem にページインデックスが紐づいていればコールバックを呼ぶ。
        /// </summary>
        /// <param name="sender">イベント送信元（TreeView）</param>
        /// <param name="e">イベント引数</param>
        private void TreeView_SelectedItemChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_onItemInvoked == null) return;
            if (e.NewValue is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _onItemInvoked(idx);
            }
        }

        /// <summary>
        /// TreeViewItem が選択されたときのハンドラ（キーボード等での選択も含む）
        /// </summary>
        private void TreeViewItem_Selected(object? sender, RoutedEventArgs e)
        {
            if (_onItemInvoked == null) return;
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _onItemInvoked(idx);
            }
        }

        /// <summary>
        /// TreeViewItem がダブルクリックされたときのハンドラ
        /// </summary>
        private void TreeViewItem_MouseDoubleClick(object? sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_onItemInvoked == null) return;
            if (sender is TreeViewItem tvi && tvi.Tag is int idx)
            {
                _onItemInvoked(idx);
            }
        }
    }
}
