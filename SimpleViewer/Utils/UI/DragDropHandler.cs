using System.IO;
using System.Windows;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// ドラッグ&ドロップ処理を担当するクラス。
    /// ファイルのドロップイベントを処理し、コールバックを通じて通知します。
    /// ロード中のドロップを防止して、競合状態を回避します。
    /// </summary>
    public class DragDropHandler
    {
        private readonly Func<string, Task> _openFileCallback;
        private readonly Action _focusWindowCallback;
        private readonly Func<bool> _isLoadingCallback;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="openFileCallback">ファイルを開くコールバック</param>
        /// <param name="focusWindowCallback">ウィンドウにフォーカスを戻すコールバック</param>
        /// <param name="isLoadingCallback">ロード中かどうかを確認するコールバック</param>
        public DragDropHandler(
            Func<string, Task> openFileCallback, 
            Action focusWindowCallback,
            Func<bool> isLoadingCallback)
        {
            _openFileCallback = openFileCallback ?? throw new ArgumentNullException(nameof(openFileCallback));
            _focusWindowCallback = focusWindowCallback ?? throw new ArgumentNullException(nameof(focusWindowCallback));
            _isLoadingCallback = isLoadingCallback ?? throw new ArgumentNullException(nameof(isLoadingCallback));
        }

        /// <summary>
        /// DragOver イベントを処理します。
        /// ロード中の場合はドロップを禁止します。
        /// </summary>
        /// <param name="e">DragEventArgs</param>
        public void HandleDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !_isLoadingCallback())
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
        /// Drop イベントを処理します。
        /// ロード中の場合は処理をスキップします。
        /// </summary>
        /// <param name="e">DragEventArgs</param>
        public async Task HandleDropAsync(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            // ロード中は処理をスキップ
            if (_isLoadingCallback())
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
