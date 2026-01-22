using System.IO;
using System.Windows;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// タイトルバーの表示内容を管理するクラス。
    /// ソースファイル名、エントリ名を組み合わせてタイトル文字列を生成します。
    /// </summary>
    public class TitleBarManager
    {
        private const string DefaultTitle = "SimpleViewer";
        private readonly Window _window;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="window">タイトルを設定する対象ウィンドウ</param>
        public TitleBarManager(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// タイトルをデフォルト状態に設定します。
        /// </summary>
        public void SetDefaultTitle()
        {
            _window.Title = DefaultTitle;
        }

        /// <summary>
        /// ソースパスからタイトルを設定します。
        /// </summary>
        /// <param name="sourcePath">ソースファイルまたはフォルダのパス</param>
        public void SetTitleFromSource(string? sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                SetDefaultTitle();
                return;
            }

            try
            {
                string displayName = ExtractDisplayName(sourcePath);
                _window.Title = $"{DefaultTitle} - {displayName}";
            }
            catch
            {
                SetDefaultTitle();
            }
        }

        /// <summary>
        /// ソースとエントリ名を組み合わせてタイトルを設定します。
        /// </summary>
        /// <param name="sourcePath">ソースファイルまたはフォルダのパス</param>
        /// <param name="entryName">エントリ名（ファイル名など）</param>
        public void SetTitleWithEntry(string? sourcePath, string? entryName)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                SetDefaultTitle();
                return;
            }

            try
            {
                string containerName = Path.GetFileName(sourcePath) ?? sourcePath;
                
                if (string.IsNullOrEmpty(entryName))
                {
                    _window.Title = $"{DefaultTitle} - {containerName}";
                }
                else
                {
                    _window.Title = $"{DefaultTitle} - {containerName} - {entryName}";
                }
            }
            catch
            {
                SetDefaultTitle();
            }
        }

        /// <summary>
        /// パスから表示名を抽出します。
        /// </summary>
        /// <param name="path">ファイルまたはフォルダのパス</param>
        /// <returns>表示用の名前</returns>
        private string ExtractDisplayName(string path)
        {
            if (Directory.Exists(path))
            {
                return Path.GetFileName(
                    path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                ) ?? path;
            }

            return Path.GetFileName(path) ?? path;
        }
    }
}
