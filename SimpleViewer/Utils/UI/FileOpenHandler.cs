using SimpleViewer.Presenters;
using System.Diagnostics;
using System.IO;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// ファイルオープンとソース管理を担当するクラス。
    /// ツリー構築のロジックを含み、Presenter との橋渡しを行います。
    /// </summary>
    public class FileOpenHandler
    {
        private readonly SimpleViewerPresenter _presenter;
        private readonly SidebarManager _sidebarManager;
        private readonly TitleBarManager _titleBarManager;

        /// <summary>
        /// 現在開いているソースのパス
        /// </summary>
        public string? CurrentSourcePath { get; private set; }

        /// <summary>
        /// 現在のソース情報をクリアします。
        /// </summary>
        public void ClearCurrentSource()
        {
            CurrentSourcePath = null;
            _titleBarManager.SetDefaultTitle();
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="presenter">Presenter インスタンス</param>
        /// <param name="sidebarManager">サイドバーマネージャー</param>
        /// <param name="titleBarManager">タイトルバーマネージャー</param>
        public FileOpenHandler(
            SimpleViewerPresenter presenter,
            SidebarManager sidebarManager,
            TitleBarManager titleBarManager)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            _sidebarManager = sidebarManager ?? throw new ArgumentNullException(nameof(sidebarManager));
            _titleBarManager = titleBarManager ?? throw new ArgumentNullException(nameof(titleBarManager));
        }

        /// <summary>
        /// 新しいソースを開きます。
        /// </summary>
        /// <param name="path">開くファイルまたはフォルダのパス</param>
        public async Task OpenSourceAsync(string path)
        {
            // サイドバーとツリーをクリア
            _sidebarManager.ClearSidebar();
            _sidebarManager.ClearTree();

            // Presenter でソースを開く
            await _presenter.OpenSourceAsync(path);
            CurrentSourcePath = path;

            // タイトルを更新
            _titleBarManager.SetTitleFromSource(path);

            // ツリーを構築
            await BuildTreeAsync(path);
        }

        /// <summary>
        /// ページ変更時にタイトルを更新します。
        /// </summary>
        /// <param name="currentPage">現在のページ番号（1始まり）</param>
        public async Task UpdateTitleForPageAsync(int currentPage)
        {
            if (string.IsNullOrEmpty(CurrentSourcePath))
            {
                return;
            }

            try
            {
                var fileList = await _presenter.GetFileListAsync();
                if (fileList != null && fileList.Count > 0)
                {
                    int index = Math.Clamp(currentPage - 1, 0, fileList.Count - 1);
                    string entryName = fileList[index];
                    _titleBarManager.SetTitleWithEntry(CurrentSourcePath, entryName);
                }
            }
            catch
            {
                // エラー時はソースパスのみでタイトル設定
                _titleBarManager.SetTitleFromSource(CurrentSourcePath);
            }
        }

        /// <summary>
        /// ツリーを構築します。
        /// </summary>
        /// <param name="sourcePath">ソースパス</param>
        private async Task BuildTreeAsync(string sourcePath)
        {
            try
            {
                var fileList = await _presenter.GetFileListAsync();
                if (fileList == null || fileList.Count == 0)
                {
                    _sidebarManager.ClearTree();
                    return;
                }

                // パスからページインデックスへのマッピングを作成
                var pathToPageIndex = CreatePathToPageIndexMap(fileList);

                // ルート名を決定
                string? rootName = DetermineRootName(sourcePath);

                // ツリーを構築
                _sidebarManager.BuildTree(fileList, pathToPageIndex, rootName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BuildTreeAsync failed: {ex.Message}");
                _sidebarManager.ClearTree();
            }
        }

        /// <summary>
        /// ファイルリストからパス→インデックスのマップを作成します。
        /// </summary>
        /// <param name="fileList">ファイルリスト</param>
        /// <returns>パスからページインデックスへのマップ</returns>
        private Dictionary<string, int> CreatePathToPageIndexMap(IReadOnlyList<string> fileList)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fileList.Count; i++)
            {
                map[fileList[i]] = i;
            }
            return map;
        }

        /// <summary>
        /// ソースパスからツリーのルート名を決定します。
        /// </summary>
        /// <param name="sourcePath">ソースパス</param>
        /// <returns>ルート名（決定できない場合はnull）</returns>
        private string? DetermineRootName(string sourcePath)
        {
            try
            {
                if (Directory.Exists(sourcePath))
                {
                    return Path.GetFileName(
                        sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    );
                }

                var extension = Path.GetExtension(sourcePath);
                if (!string.IsNullOrEmpty(extension) &&
                    (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    return Path.GetFileName(sourcePath);
                }

                var directory = Path.GetDirectoryName(sourcePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    return Path.GetFileName(
                        directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    );
                }

                return Path.GetFileName(sourcePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
