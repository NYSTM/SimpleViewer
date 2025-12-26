using System.IO;

namespace SimpleViewer.Models;

public abstract class ImageSourceBase
{
    // 対応する静止画フォーマット。WebPも含めます。
    protected static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    protected bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return SupportedExtensions.Contains(ext);
    }
}