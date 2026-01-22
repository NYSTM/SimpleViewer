using System.Diagnostics;
using System.IO;

namespace SimpleViewer.Services;

/// <summary>
/// キャッシュフォルダの削除を担当するサービスクラス。
/// 信頼性重視のため複数回のリトライや属性修正、排他オープンの試行を行います。
/// </summary>
public class CacheCleanupService
{
    private readonly string _cacheDirectory;

    /// <summary>
    /// CacheCleanupService を初期化します。
    /// </summary>
    /// <param name="baseDirectory">アプリケーションのベースディレクトリ（通常はアプリ起動パス）</param>
    public CacheCleanupService(string baseDirectory)
    {
        _cacheDirectory = Path.Combine(baseDirectory, "cache");
    }

    /// <summary>
    /// キャッシュフォルダを削除しようと試みます。
    /// 削除失敗はログに記録して無視します。
    /// </summary>
    public void CleanupCacheFolder()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory)) return;

            DeleteCacheFiles();
            DeleteCacheDirectory();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CleanupCacheFolder failed: {ex.Message}");
        }
    }

    /// <summary>
    /// キャッシュフォルダ内のファイルを個別に削除します。
    /// </summary>
    private void DeleteCacheFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.thumb", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (!TryDeleteFileWithRetries(file, attempts: 8, delayMs: 250))
                {
                    Debug.WriteLine($"Failed to delete cache file after retries: '{file}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete cache file '{file}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// キャッシュディレクトリ本体を削除します。
    /// </summary>
    private void DeleteCacheDirectory()
    {
        try
        {
            const int dirAttempts = 5;
            for (int i = 0; i < dirAttempts; i++)
            {
                try
                {
                    if (!Directory.Exists(_cacheDirectory)) break;

                    // ディレクトリを削除（空でない場合は例外を投げる）
                    Directory.Delete(_cacheDirectory, recursive: false);
                    break; // 成功
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete cache directory '{_cacheDirectory}' (attempt {i + 1}): {ex.Message}");
                    Thread.Sleep(200);
                }
            }

            // 最後の手段で再帰削除を試す（注意: ロックされているファイルはここでも失敗する）
            if (Directory.Exists(_cacheDirectory))
            {
                try
                {
                    Directory.Delete(_cacheDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Recursive delete failed for '{_cacheDirectory}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to delete cache directory '{_cacheDirectory}': {ex.Message}");
        }
    }

    /// <summary>
    /// ファイル削除を複数回試行します。
    /// - ファイル属性を通常に戻す
    /// - 排他オープンしてトランケートを試す
    /// - 指定回数リトライする
    /// </summary>
    /// <param name="filePath">削除対象のファイルパス</param>
    /// <param name="attempts">リトライ回数</param>
    /// <param name="delayMs">リトライ間隔（ミリ秒）</param>
    /// <returns>削除に成功した場合は true、それ以外は false</returns>
    private static bool TryDeleteFileWithRetries(string filePath, int attempts = 5, int delayMs = 200)
    {
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (!File.Exists(filePath)) return true;

                // 属性を通常に戻す（読み取り専用などが付いている場合に備える）
                ResetFileAttributes(filePath);

                // まず通常削除を試す
                if (TryDeleteFile(filePath)) return true;

                // 排他で開いてトランケートを試みる
                TryTruncateFile(filePath);

                // 再度削除を試みる
                if (TryDeleteFile(filePath)) return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Attempt {attempt} to delete '{filePath}' failed: {ex.Message}");
            }

            // 少し待ってから再試行（同期的に待機して確実性を優先）
            Thread.Sleep(delayMs);
        }

        // ここまで到達したら削除に失敗
        return !File.Exists(filePath);
    }

    /// <summary>
    /// ファイル属性を通常に戻します。
    /// </summary>
    /// <param name="filePath">対象ファイルパス</param>
    private static void ResetFileAttributes(string filePath)
    {
        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
        catch
        {
            // 属性変更失敗は無視
        }
    }

    /// <summary>
    /// ファイルの通常削除を試みます。
    /// </summary>
    /// <param name="filePath">削除対象のファイルパス</param>
    /// <returns>削除に成功した場合は true、それ以外は false</returns>
    private static bool TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            return !File.Exists(filePath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ファイルを排他で開いてトランケートを試みます。
    /// </summary>
    /// <param name="filePath">対象ファイルパス</param>
    private static void TryTruncateFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try
            {
                fs.SetLength(0);
            }
            catch
            {
                // トランケート失敗は無視
            }

            try
            {
                fs.Flush(true);
            }
            catch
            {
                try
                {
                    fs.Flush();
                }
                catch
                {
                    // フラッシュ失敗は無視
                }
            }
        }
        catch
        {
            // 排他オープン失敗はリトライ対象
        }
    }
}
