using SimpleViewer.Services;
using System.IO;

namespace SimpleViewer.Tests.Services;

public class DiskCacheManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DiskCacheManager _manager;

    public DiskCacheManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DiskCacheTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = new DiskCacheManager(_tempDir, maxCacheMB: 10, useSecureDelete: false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_CreatesDirectoryAutomaticallyIfNotExists()
    {
        // 存在しないディレクトリを自動で作成する
        var newDir = Path.Combine(_tempDir, "autocreated");
        var mgr = new DiskCacheManager(newDir, maxCacheMB: 5, useSecureDelete: false);

        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void SetCurrentSource_CreatesSubfolder()
    {
        // サブフォルダを作成する
        _manager.SetCurrentSource(@"C:\images\source.zip");

        var subDirs = Directory.GetDirectories(_tempDir);
        Assert.Single(subDirs);
    }

    [Fact]
    public void SetCurrentSource_KeepsSameSubfolderForSameSource()
    {
        // 同じソースを呼び出してもサブフォルダは一つ
        _manager.SetCurrentSource(@"C:\images\source.zip");
        _manager.SetCurrentSource(@"C:\images\source.zip");

        var subDirs = Directory.GetDirectories(_tempDir);
        Assert.Single(subDirs);
    }

    [Fact]
    public void SetCurrentSource_DeletesPreviousSubfolderWhenSwitchingSource()
    {
        // 異なるソースに切り替えると以前のサブフォルダを削除する
        _manager.SetCurrentSource(@"C:\images\first.zip");
        var firstDirs = Directory.GetDirectories(_tempDir);
        var firstFolder = firstDirs[0];

        _manager.SetCurrentSource(@"C:\images\second.zip");

        Assert.False(Directory.Exists(firstFolder));
        var currentDirs = Directory.GetDirectories(_tempDir);
        Assert.Single(currentDirs);
    }

    [Fact]
    public void GetFilePath_ReturnsPathContainingKeyAfterSetCurrentSource()
    {
        // SetCurrentSource 後にキーを含むパスを返す
        _manager.SetCurrentSource(@"C:\images\source.zip");
        var key = "abc123";

        var filePath = _manager.GetFilePath(key);

        Assert.Contains(key, filePath);
        Assert.EndsWith(".thumb", filePath);
    }

    [Fact]
    public void FileExists_ReturnsFalseForNonExistentFile()
    {
        // 存在しないファイルは false を返す
        var path = Path.Combine(_tempDir, "notexist.thumb");

        Assert.False(_manager.FileExists(path));
    }

    [Fact]
    public void FileExists_ReturnsTrueForExistingFile()
    {
        // 存在するファイルは true を返す
        var path = Path.Combine(_tempDir, "exists.thumb");
        File.WriteAllText(path, "data");

        Assert.True(_manager.FileExists(path));
    }

    [Fact]
    public void ClearAllCache_DeletesCurrentSourceCacheFiles()
    {
        // 現在のソースのキャッシュファイルを削除する
        _manager.SetCurrentSource(@"C:\images\source.zip");
        var filePath = _manager.GetFilePath("testkey");
        File.WriteAllText(filePath, "cached");
        Assert.True(File.Exists(filePath));

        _manager.ClearAllCache();

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void DeleteFile_DeletesFile()
    {
        // ファイルを削除できる
        var path = Path.Combine(_tempDir, "todelete.thumb");
        File.WriteAllText(path, "data");

        _manager.DeleteFile(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteFile_DoesNotThrowExceptionForNonExistentFile()
    {
        // 存在しないファイルでも例外をスローしない
        var path = Path.Combine(_tempDir, "notexist.thumb");

        var ex = Record.Exception(() => _manager.DeleteFile(path));

        Assert.Null(ex);
    }

    [Fact]
    public void EnforceDiskCapacity_DeletesOldFilesWhenCapacityExceeded()
    {
        // 容量超過時に古いファイルを削除する
        // 上限 1 バイトの極小キャッシュで容量超過を強制
        var tinyDir = Path.Combine(_tempDir, "tiny");
        var tinyManager = new DiskCacheManager(tinyDir, maxCacheMB: 1, useSecureDelete: false);
        tinyManager.SetCurrentSource(@"C:\images\source.zip");

        // 2MB 相当のダミーファイルを複数作成
        for (int i = 0; i < 3; i++)
        {
            var filePath = tinyManager.GetFilePath($"key{i:D4}");
            File.WriteAllBytes(filePath, new byte[512 * 1024]); // 512KB 各
        }

        tinyManager.EnforceDiskCapacity();

        var remaining = Directory.GetFiles(
            Path.Combine(tinyDir, Directory.GetDirectories(tinyDir)[0]), "*.thumb");
        // 合計 1.5MB は 1MB 上限を超えているので何か削除されているはず
        Assert.True(remaining.Length < 3);
    }
}
