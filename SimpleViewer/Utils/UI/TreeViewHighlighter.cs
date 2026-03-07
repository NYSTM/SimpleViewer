using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// TreeView のアイテムハイライトと展開を管理するクラス。
/// 指定されたページインデックスに対応する TreeViewItem を検索し、
/// 祖先を展開して選択状態にします。
/// </summary>
public class TreeViewHighlighter
{
    private readonly Dispatcher _dispatcher;
    private bool _isUpdatingSelection = false;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="dispatcher">UI スレッド用 Dispatcher</param>
    public TreeViewHighlighter(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// 指定インデックスに対応する TreeViewItem を検索して選択（ハイライト）します。
    /// Tree が未作成または項目が見つからない場合は何もしません。
    /// </summary>
    /// <param name="treeView">対象の TreeView</param>
    /// <param name="pageIndex">ハイライト対象のページインデックス</param>
    public void HighlightItem(TreeView? treeView, int pageIndex)
    {
        if (treeView == null) return;
        if (_isUpdatingSelection) return; // 無限ループ防止

        _ = _dispatcher.BeginInvoke(() =>
        {
            try
            {
                _isUpdatingSelection = true;

                var target = FindTreeViewItemByTag(treeView.Items, pageIndex);
                if (target == null) return;

                // 祖先を展開して可視化
                ExpandAncestors(target);

                // TreeView に明示的にフォーカスを設定して、選択ハイライトを濃い青色にする
                if (treeView.IsKeyboardFocusWithin)
                {
                    treeView.Focus();
                    System.Windows.Input.Keyboard.Focus(treeView);
                }

                // 選択してビューにスクロール
                target.IsSelected = true;
                target.BringIntoView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HighlightItem failed: {ex.Message}");
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 指定された TreeViewItem の祖先を展開します。
    /// </summary>
    /// <param name="item">展開対象の TreeViewItem</param>
    private void ExpandAncestors(TreeViewItem item)
    {
        DependencyObject? parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is TreeViewItem parentTvi)
        {
            parentTvi.IsExpanded = true;
            parent = ItemsControl.ItemsControlFromItemContainer(parentTvi);
        }
    }

    /// <summary>
    /// 再帰的に TreeViewItem を探索して Tag に一致するものを返します。
    /// </summary>
    /// <param name="items">検索対象の ItemCollection</param>
    /// <param name="tag">検索するタグ値（ページインデックス）</param>
    /// <returns>見つかった TreeViewItem、または null</returns>
    private TreeViewItem? FindTreeViewItemByTag(ItemCollection items, int tag)
    {
        foreach (var it in items.OfType<TreeViewItem>())
        {
            if (it.Tag is int idx && idx == tag) return it;

            if (it.Items.Count > 0)
            {
                var result = FindTreeViewItemByTag(it.Items, tag);
                if (result != null) return result;
            }
        }

        return null;
    }
}
