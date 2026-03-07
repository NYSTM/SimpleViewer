using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SimpleViewer.Services;

/// <summary>
/// ディスクキャッシュの管理を担当するクラス。
/// ソースごとにサブフォルダを作成して、効率的な削除を実現します。
/// セキュリティのため、不要なキャッシュは即座に削除します。
/// </summary>
public class DiskCacheManager
{
    private readonly string _cacheDirectory;
    private readonly long _maxCacheBytes;
    private readonly bool _useSecureDelete;
    private readonly object _diskLock = new();
    
    // 現在のソース用のサブフォルダ名
    private string? _currentSourceFolder;

    /// <summary>
    /// DiskCacheManager を初期化します。
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
            }
        }
        catch
        {
            // ディレクトリ作成失敗は致命的ではないため無視
        }
    }
    
    /// <summary>
    /// ソース識別子からサブフォルダ名を生成します。
    /// 完全なパスから SHA256 ハッシュを生成し、一意性を保証します。
    /// ハッシュの最初の 16 文字を使用して、パス長を短縮します。
    /// </summary>
    /// <param name="sourceIdentifier">ソース識別子（フルパス）</param>
    /// <returns>サブフォルダ名（16文字のハッシュ）</returns>
    private static string GetSourceFolderName(string sourceIdentifier)
    {
        // 完全なパスからハッシュ値を生成（一意性を保証）
        var bytes = Encoding.UTF8.GetBytes(sourceIdentifier);
        var hash = SHA256.HashData(bytes);
        
        // 16文字に短縮（8バイト = 64ビット）
        return Convert.ToHexString(hash)[..16];
    }
    
    /// <summary>
    /// 現在のソース用のサブフォルダを設定します。
    /// 以前のソースのサブフォルダは即座に削除されます（セキュリティ）。
    /// </summary>
    /// <param name="sourceIdentifier">ソース識別子</param>
    public void SetCurrentSource(string sourceIdentifier)
    {
        var newFolder = GetSourceFolderName(sourceIdentifier);
        
        lock (_diskLock)
        {
            // 以前のソースフォルダを削除（同一ソースでない場合）
            if (_currentSourceFolder != null && _currentSourceFolder != newFolder)
            {
                try
                {
                    var oldPath = Path.Combine(_cacheDirectory, _currentSourceFolder);
                    if (Directory.Exists(oldPath))
                    {
                        Directory.Delete(oldPath, recursive: true);
                    }
                }
                catch
                {
                    // 削除失敗は無視
                }
            }
            
            _currentSourceFolder = newFolder;
            
            // 新しいソースのサブフォルダを作成
            var newPath = Path.Combine(_cacheDirectory, newFolder);
            try
            {
                if (!Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                }
            }
            catch
            {
                // ディレクトリ作成失敗は無視
            }
        }
    }
    
    /// <summary>
    /// 指定されたキーに対応するファイルパスを取得します。
    /// 現在のソースのサブフォルダ内に配置されます。
    /// </summary>
    /// <param name="key">キャッシュキー</param>
    /// <returns>ファイルパス</returns>
    public string GetFilePath(string key)
    {
        if (_currentSourceFolder == null)
        {
            // フォールバック: ルートディレクトリ
            return Path.Combine(_cacheDirectory, key + ".thumb");
        }
        
        var sourceFolder = Path.Combine(_cacheDirectory, _currentSourceFolder);
        return Path.Combine(sourceFolder, key + ".thumb");
    }

    /// <summary>
    /// ファイルが存在するかどうかを確認します。
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>ファイルが存在する場合は true</returns>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// すべてのキャッシュファイルを削除します。
    /// 現在のソースのサブフォルダのみを削除します（高速）。
    /// </summary>
    public void ClearAllCache()
    {
        lock (_diskLock)
        {
            if (_currentSourceFolder == null) return;
            
            try
            {
                var sourcePath = Path.Combine(_cacheDirectory, _currentSourceFolder);
                if (Directory.Exists(sourcePath))
                {
                    Directory.Delete(sourcePath, recursive: true);
                    
                    // サブフォルダを再作成
                    Directory.CreateDirectory(sourcePath);
                }
            }
            catch
            {
                // 削除失敗は無視
            }
        }
    }

    /// <summary>
    /// すべてのキャッシュファイルを非同期で削除します。
    /// 現在のソースのサブフォルダのみを削除します（高速）。
    /// </summary>
    /// <param name="ct">キャンセルトークン</param>
    public Task ClearAllCacheAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            lock (_diskLock)
            {
                if (_currentSourceFolder == null) return;
                
                try
                {
                    var sourcePath = Path.Combine(_cacheDirectory, _currentSourceFolder);
                    if (Directory.Exists(sourcePath))
                    {
                        Directory.Delete(sourcePath, recursive: true);
                        
                        // サブフォルダを再作成
                        Directory.CreateDirectory(sourcePath);
                    }
                }
                catch
                {
                    // 削除失敗は無視
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
    /// 現在のソースのサブフォルダのみを対象とします。
    /// </summary>
    public void EnforceDiskCapacity()
    {
        if (_maxCacheBytes <= 0) return;

        lock (_diskLock)
        {
            if (_currentSourceFolder == null) return;
            
            try
            {
                var sourcePath = Path.Combine(_cacheDirectory, _currentSourceFolder);
                var di = new DirectoryInfo(sourcePath);
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
            catch { /* エラーは無視 */ }
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
