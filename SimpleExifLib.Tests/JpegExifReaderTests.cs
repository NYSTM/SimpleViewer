using System.Reflection;

namespace SimpleExifLib.Tests
{
    /// <summary>
    /// JpegExifReader の基本的な動作を検証する単体テスト
    /// テスト用の小さなJPEGバイト列を埋め込みリソースとして使用する
    /// </summary>
    public class JpegExifReaderTests
    {
        [Fact]
        public async Task ReadAsync_ReturnsExifData_WhenExifPresent()
        {
            // 埋め込みリソースからテストJPEGを取得
            using var stream = GetResourceStream("test_with_exif.jpg");
            Assert.NotNull(stream);

            var exif = await ExifReaderFactory.ReadAsync(stream!);

            Assert.NotNull(exif);
            Assert.False(string.IsNullOrEmpty(exif!.CameraMake));
            Assert.False(string.IsNullOrEmpty(exif.CameraModel));
        }

        private Stream? GetResourceStream(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            var fullName = asm.GetManifestResourceNames();
            // 単純化: 同ディレクトリの実ファイルを読み込む
            var baseDir = Path.GetDirectoryName(asm.Location)!;
            var file = Path.Combine(baseDir, name);
            if (!File.Exists(file)) return null;
            return File.OpenRead(file);
        }
    }
}
