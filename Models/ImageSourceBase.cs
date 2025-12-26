using System.IO;

namespace SimpleViewer.Models;

public abstract class ImageSourceBase : IDisposable
{
    protected static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    public static bool IsStaticImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    protected bool IsImageFile(string path) => IsStaticImageFile(path);

    // virtual を追加して、派生クラスで override できるようにする
    public virtual void Dispose()
    {
        // 基底クラスで共通の解放処理があればここに記述
    }
}