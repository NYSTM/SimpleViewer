using SimpleViewer.Models.ImageSources;

namespace SimpleViewer.Tests.Models.ImageSources;

public class ImageSourceBaseTests
{
    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("image.png")]
    [InlineData("icon.bmp")]
    [InlineData("anim.gif")]
    [InlineData("photo.webp")]
    [InlineData(@"C:\pictures\photo.JPG")]
    [InlineData(@"C:\pictures\image.PNG")]
    public void IsStaticImageFile_ReturnsTrueForSupportedExtensions(string path)
    {
        // サポート対象の拡張子は true を返す
        Assert.True(ImageSourceBase.IsStaticImageFile(path));
    }

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("archive.zip")]
    [InlineData("archive.cbz")]
    [InlineData("video.mp4")]
    [InlineData("data.txt")]
    [InlineData("noextension")]
    public void IsStaticImageFile_ReturnsFalseForUnsupportedExtensions(string path)
    {
        // 非サポート拡張子は false を返す
        Assert.False(ImageSourceBase.IsStaticImageFile(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsStaticImageFile_ReturnsFalseForNullOrWhitespace(string? path)
    {
        // null または空白は false を返す
        Assert.False(ImageSourceBase.IsStaticImageFile(path!));
    }
}
