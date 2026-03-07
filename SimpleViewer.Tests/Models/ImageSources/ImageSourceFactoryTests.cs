using SimpleViewer.Models.ImageSources;
using System.IO;

namespace SimpleViewer.Tests.Models.ImageSources;

public class ImageSourceFactoryTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public ImageSourceFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ImageSourceFactoryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        });
    }

    // ---- 引数バリデーション ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateSourceAsync_ThrowsArgumentNullExceptionForInvalidPath(string? path)
    {
        // null または空白パスは ArgumentNullException をスロー
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ImageSourceFactory.CreateSourceAsync(path!));
    }

    // ---- フォルダ判定 ----

    [Fact]
    public async Task CreateSourceAsync_ReturnsFolderImageSourceForDirectoryPath()
    {
        // フォルダパスを渡すと FolderImageSource が返る
        var source = await ImageSourceFactory.CreateSourceAsync(_tempDir);
        try
        {
            Assert.IsType<FolderImageSource>(source);
        }
        finally
        {
            source.Dispose();
        }
    }

    // ---- ZIP / CBZ ----

    [Theory]
    [InlineData(".zip")]
    [InlineData(".cbz")]
    public async Task CreateSourceAsync_ReturnsArchiveImageSourceForArchiveExtensions(string ext)
    {
        // アーカイブ拡張子は ArchiveImageSource が返る
        var filePath = Path.Combine(_tempDir, $"test{ext}");
        
        // 有効な空の ZIP ファイルを作成
        using (var zip = System.IO.Compression.ZipFile.Open(filePath, System.IO.Compression.ZipArchiveMode.Create))
        {
            // 空のアーカイブを作成
        }

        var source = await ImageSourceFactory.CreateSourceAsync(filePath);
        try
        {
            Assert.IsType<ArchiveImageSource>(source);
        }
        finally
        {
            source.Dispose();
        }
    }

    // ---- 画像ファイル（フォルダにリダイレクト） ----

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".bmp")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    public async Task CreateSourceAsync_ReturnsFolderImageSourceForStaticImageFiles(string ext)
    {
        // 静的画像ファイルは FolderImageSource が返る
        var filePath = Path.Combine(_tempDir, $"image{ext}");
        await File.WriteAllBytesAsync(filePath, []);

        var source = await ImageSourceFactory.CreateSourceAsync(filePath);
        try
        {
            Assert.IsType<FolderImageSource>(source);
        }
        finally
        {
            source.Dispose();
        }
    }

    // ---- 未対応フォーマット ----

    [Theory]
    [InlineData(".txt")]
    [InlineData(".mp4")]
    [InlineData(".docx")]
    public async Task CreateSourceAsync_ThrowsNotSupportedExceptionForUnsupportedExtensions(string ext)
    {
        // 未対応拡張子は NotSupportedException をスロー
        var filePath = Path.Combine(_tempDir, $"file{ext}");
        await File.WriteAllBytesAsync(filePath, []);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ImageSourceFactory.CreateSourceAsync(filePath));
    }
}
