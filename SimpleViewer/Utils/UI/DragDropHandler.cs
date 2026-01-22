using System.IO;
using System.Windows;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// ドラッグ&ドロップ処理を担当するクラス。
    /// ファイルのドロップイベントを処理し、コールバックを通じて通知します。
    /// </summary>
    public class DragDropHandler
    {
        private readonly Func<string, Task> _openFileCallback;
        private readonly Action _focusWindowCallback;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="openFileCallback">ファイルを開くコールバック</param>
        /// <param name="focusWindowCallback">ウィンドウにフォーカスを戻すコールバック</param>
        public DragDropHandler(Func<string, Task> openFileCallback, Action focusWindowCallback)
        {
            _openFileCallback = openFileCallback ?? throw new ArgumentNullException(nameof(openFileCallback));
            _focusWindowCallback = focusWindowCallback ?? throw new ArgumentNullException(nameof(focusWindowCallback));
        }

        /// <summary>
        /// DragOverイベントを処理します。
        /// </summary>
        /// <param name="e">DragEventArgs</param>
        public void HandleDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Dropイベントを処理します。
        /// </summary>
        /// <param name="e">DragEventArgs</param>
        public async Task HandleDropAsync(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            try
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0)
                {
                    return;
                }

                // 最初のファイルまたはフォルダを開く
                string path = files[0];
                
                // ファイルまたはディレクトリの存在確認
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    return;
                }

                await _openFileCallback(path);
                _focusWindowCallback();
            }
            catch
            {
                // エラーは無視（ファイルオープン側で処理される）
            }
        }
    }
}
