using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// WPF のビジュアルツリーを検索するユーティリティクラス。
    /// 名前や型による子要素の再帰的検索機能を提供します。
    /// </summary>
    public static class VisualTreeSearcher
    {
        /// <summary>
        /// ビジュアルツリー検索ヘルパー: 指定名の子要素を再帰的に探索します。
        /// </summary>
        /// <typeparam name="T">期待する要素の型</typeparam>
        /// <param name="parent">探索を開始する親要素</param>
        /// <param name="name">探す要素の Name プロパティ値</param>
        /// <returns>見つかった要素（見つからなければ null）</returns>
        public static T? FindChildByName<T>(DependencyObject parent, string name) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is FrameworkElement fe && fe.Name == name && child is T t)
                {
                    return t;
                }

                var result = FindChildByName<T>(child, name);
                if (result != null) return result;
            }

            return null;
        }

        /// <summary>
        /// ビジュアルツリーから指定型の子要素を再帰的に探索します（名前は問わない）。
        /// </summary>
        /// <typeparam name="T">期待する要素の型</typeparam>
        /// <param name="parent">探索を開始する親要素</param>
        /// <returns>見つかった要素（見つからなければ null）</returns>
        public static T? FindChildByType<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T t)
                {
                    return t;
                }

                var result = FindChildByType<T>(child);
                if (result != null) return result;
            }

            return null;
        }

        /// <summary>
        /// アプリケーション全体から指定名の要素を検索します。
        /// </summary>
        /// <typeparam name="T">期待する要素の型</typeparam>
        /// <param name="name">探す要素の Name プロパティ値</param>
        /// <returns>見つかった要素（見つからなければ null）</returns>
        public static T? FindInApplication<T>(string name) where T : DependencyObject
        {
            if (Application.Current?.MainWindow != null)
            {
                var found = Application.Current.MainWindow.FindName(name) as T 
                    ?? FindChildByName<T>(Application.Current.MainWindow, name);
                
                if (found != null) return found;
            }

            if (Application.Current != null)
            {
                foreach (Window w in Application.Current.Windows)
                {
                    var found = w.FindName(name) as T 
                        ?? FindChildByName<T>(w, name);
                    
                    if (found != null) return found;
                }
            }

            return null;
        }
    }
}
