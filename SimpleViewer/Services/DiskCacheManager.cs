using System.IO;
using System.Security.Cryptography;

namespace SimpleViewer.Services;

/// <summary>
/// ディスクキャッシュの管理を担当するクラス。
/// - ファイルの読み書き、削除、容量管理を行います
/// - セキュア削除機能を提供します
/// </summary>
public class DiskCacheManager
{
    private readonly string _cacheDirectory;
    private readonly long _maxCacheBytes;
    private readonly bool _useSecureDelete;
    private readonly object _diskLock = new();

    /// <summary>
    /// DiskCacheManagerを初期化します。
    /// </summary>
    /// <param name="cacheDirectory">キャッシュディレクトリのパス</param>
    /// <param name="maxCacheMB">最大キャッシュサイズ（MB）。0以下は無制限</param>
    /// <param name="useSecureDelete">セキュア削除を使用するかどうか</param>
    public DiskCacheManager(string cacheDirectory, int maxCacheMB, bool useSecureDelete)
    {
        _cacheDirectory = cacheDirectory;
        _maxCacheBytes = Math.Max(0, maxCacheMB) * 1024L * 1024L;
        _useSecureDelete = useSecureDelete;

        // キャッシュディレクトリを確実に作成
        EnsureCacheDirectoryExists();
    }

    /// <summary>
    /// キャッシュディレクトリが存在することを確認し、存在しない場合は作成します。
    /// </summary>
    private void EnsureCacheDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
                System.Diagnostics.Debug.WriteLine($"[DiskCacheManager] キャッシュディレクトリを作成しました: {_cacheDirectory}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DiskCacheManager] キャッシュディレクトリの作成に失敗しました: {ex.Message}");
            // ディレクトリ作成失敗は致命的ではないが、ログに記録
        }
    }

    /// <summary>
    /// 指定されたキーに対応するファイルパスを取得します。
    /// </summary>
    /// <param name="key">キャッシュキー</param>
    /// <returns>ファイルパス</returns>
    public string GetFilePath(string key)
    {
        return Path.Combine(_cacheDirectory, key + ".thumb");
    }

    /// <summary>
    /// ファイルが存在するかどうかを確認します。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>ファイルが存在する場合はtrue</returns>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// すべてのキャッシュファイルを削除します。
    /// </summary>
    public void ClearAllCache()
    {
        lock (_diskLock)
        {
            try
            {
                var di = new DirectoryInfo(_cacheDirectory);
                if (!di.Exists) return;

                foreach (var f in di.GetFiles("*.thumb"))
                {
                    DeleteFile(f.FullName);
                }
            }
            catch { /* 無視 */ }
        }
    }

    /// <summary>
    /// すべてのキャッシュファイルを非同期で削除します。
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    public Task ClearAllCacheAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            string[] targetFiles;
            lock (_diskLock)
            {
                var di = new DirectoryInfo(_cacheDirectory);
                if (!di.Exists) return;

                // GetFiles("*.thumb") は呼び出した瞬間に配列を生成するため、
                // これ以降に作成されたファイルはリストに含まれない（保護される）。
                targetFiles = di.GetFiles("*.thumb")
                                .Select(f => f.FullName)
                                .ToArray();
            }

            // lockの外で実行することで、ファイルシステムへの負荷を分散し、UI応答性も維持する
            foreach (var filePath in targetFiles)
            {
                // 高速なキャンセレーション
                if (ct.IsCancellationRequested) break;

                try
                {
                    // 別のスレッドが使用中の場合は IOException が発生するが、
                    // catchして続行することで「消せるものだけ全部消す」一括処理を実現
                    DeleteFile(filePath);
                }
                catch (IOException)
                {
                    // 使用中のファイルは無視して次に進む
                }
                catch (UnauthorizedAccessException)
                {
                    // 権限エラーも同様
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 予期せぬエラーは診断用に記録
                    Console.WriteLine($"Delete failed: {filePath}, {ex.Message}");
                }
            }
        }, ct);
    }

    /// <summary>
    /// ファイルを削除します。セキュア削除が有効な場合は上書き後に削除します。
    /// </summary>
    /// <param name="filePath">削除するファイルのパス</param>
    public void DeleteFile(string filePath)
    {
        try
        {
            if (_useSecureDelete)
            {
                SecureDeleteFile(filePath);
            }
            else
            {
                try { File.Delete(filePath); } catch { }
            }
        }
        catch { /* 削除失敗はスキップ */ }
    }

    /// <summary>
    /// ディスクキャッシュの容量を制限内に収めます。
    /// 容量超過時は古いファイルから削除します。
    /// </summary>
    public void EnforceDiskCapacity()
    {
        if (_maxCacheBytes <= 0) return;

        lock (_diskLock)
        {
            try
            {
                var di = new DirectoryInfo(_cacheDirectory);
                if (!di.Exists) return;

                var files = di.GetFiles("*.thumb")
                    .OrderBy(f => f.LastAccessTimeUtc)
                    .ToList();

                long total = files.Sum(f => f.Length);
                if (total <= _maxCacheBytes) return;

                foreach (var f in files)
                {
                    try
                    {
                        f.Delete();
                        total -= f.Length;
                        if (total <= _maxCacheBytes) break;
                    }
                    catch { /* 削除失敗はスキップ */ }
                }
            }
            catch { /* 無視 */ }
        }
    }

    /// <summary>
    /// ファイルを上書きしてから削除します（セキュア削除）。
    /// </summary>
    /// <param name="path">削除するファイルのパス</param>
    private static void SecureDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            // ファイルを排他で開き、内容を乱数で上書きする
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                long len = fs.Length;
                if (len > 0)
                {
                    var buffer = new byte[8192];
                    using var rng = RandomNumberGenerator.Create();
                    long written = 0;
                    while (written < len)
                    {
                        int toWrite = (int)Math.Min(buffer.Length, len - written);
                        rng.GetBytes(buffer);
                        fs.Write(buffer, 0, toWrite);
                        written += toWrite;
                    }

                    // ディスクへフラッシュ
                    try { fs.Flush(true); } catch { try { fs.Flush(); } catch { } }
                }
            }

            // 上書きした後に削除
            try { File.Delete(path); } catch { }
        }
        catch { /* 無視 */ }
    }
}
