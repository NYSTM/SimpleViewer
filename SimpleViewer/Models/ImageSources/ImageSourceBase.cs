using System.Collections.Frozen;
using System.IO;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// 画像ソースの基底クラス。
/// 画像ソース実装（フォルダ、アーカイブ、PDF 等）はこのクラスを継承し、
/// 共通の拡張子判定ロジックや Dispose パターンを利用します。
/// </summary>
public abstract class ImageSourceBase : IDisposable
{
    /// <summary>
    /// サポートする静的画像ファイルの拡張子一覧。
    /// FrozenSet を使用して高速な検索を実現します（.NET 8 の最適化）。
    /// 小文字/大文字を区別せずに判定します。
    /// </summary>
    private static readonly FrozenSet<string> SupportedExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 指定パスがサポート対象の静的画像ファイルかどうかを判定します。
    /// null/空/空白のパスは false を返します。
    /// FrozenSet による O(1) の高速検索を使用します。
    /// </summary>
    /// <param name="path">判定するファイルパス（相対/絶対可）</param>
    public static bool IsStaticImageFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// インスタンス側から便利に拡張子判定を行うためのラッパー。
    /// 基本的に <see cref="IsStaticImageFile(string)"/> を呼び出します。
    /// </summary>
    protected bool IsImageFile(string path) => IsStaticImageFile(path);

    /// <summary>
    /// 必要に応じて派生クラスでオーバーライドし、リソースを解放してください。
    /// 基底クラスでは既定の処理は行いません（派生側で解放を実装する前提）。
    /// </summary>
    public virtual void Dispose()
    {
        // 派生クラスでオーバーライドして解放処理を実装
    }
}