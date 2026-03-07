using SimpleViewer.Services;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Tests.Services;

public class BitmapFileHandlerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BitmapFileHandler _handler;

    public BitmapFileHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SimpleViewerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _handler = new BitmapFileHandler();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static BitmapSource CreateTestBitmap(int width = 4, int height = 4)
    {
        // WIC/WPF を使わずに小さなピクセルデータから BitmapSource を生成
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);
        var bmp = BitmapSource.Create(
            width, height,
            96, 96,
            PixelFormats.Rgb24,
            null,
            pixels,
            width * 3);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    [System.STAThread]
    public void SaveBitmapToFile_SavesValidJpegFile()
    {
        // 有効な Jpeg ファイルを保存できる
        var path = Path.Combine(_tempDir, "output.jpg");
        var bmp = CreateTestBitmap();

        _handler.SaveBitmapToFile(bmp, path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    [System.STAThread]
    public void SaveBitmapToFile_CreatesDirectoryIfNotExists()
    {
        // 存在しないディレクトリを自動で作成する
        var subDir = Path.Combine(_tempDir, "sub", "nested");
        var path = Path.Combine(subDir, "output.jpg");
        var bmp = CreateTestBitmap();

        _handler.SaveBitmapToFile(bmp, path);

        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(path));
    }

    [Fact]
    [System.STAThread]
    public void SaveBitmapToFile_OverwritesExistingFile()
    {
        // 既存ファイルを上書きできる
        var path = Path.Combine(_tempDir, "overwrite.jpg");
        var bmp = CreateTestBitmap();

        _handler.SaveBitmapToFile(bmp, path);
        var firstLength = new FileInfo(path).Length;

        // 異なるサイズのビットマップで上書き
        var bmp2 = CreateTestBitmap(8, 8);
        _handler.SaveBitmapToFile(bmp2, path);
        var secondLength = new FileInfo(path).Length;

        Assert.True(File.Exists(path));
        Assert.NotEqual(firstLength, secondLength);
    }

    [Fact]
    [System.STAThread]
    public async Task LoadBitmapFromFileAsync_LoadsSavedFileRoundtrip()
    {
        // 保存したファイルを再度読み込める
        var path = Path.Combine(_tempDir, "roundtrip.jpg");
        var bmp = CreateTestBitmap();
        _handler.SaveBitmapToFile(bmp, path);

        var loaded = await _handler.LoadBitmapFromFileAsync(path, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded!.Width > 0);
        Assert.True(loaded.Height > 0);
    }

    [Fact]
    [System.STAThread]
    public async Task LoadBitmapFromFileAsync_ReturnsNullForNonExistentFile()
    {
        // 存在しないファイルは null を返す
        var path = Path.Combine(_tempDir, "nonexistent.jpg");

        var result = await _handler.LoadBitmapFromFileAsync(path, CancellationToken.None);

        Assert.Null(result);
    }
}
