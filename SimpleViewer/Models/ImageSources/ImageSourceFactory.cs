using SimpleViewer.Models.Imaging.Decoders;
using System.IO;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// 指定されたパスに応じて適切な IImageSource 実装を生成する静的ファクトリ。
/// フォルダ・ZIP/CBZ・PDF・単一画像ファイルなどに対応し、非同期初期化が必要なものは
/// 非同期メソッドを用いて生成します。
/// </summary>
public static class ImageSourceFactory
{
    /// <summary>
    /// 指定パスから適切な画像ソースを生成して返します。
    /// </summary>
    /// <param name="path">ファイルまたはフォルダのパス</param>
    /// <param name="decoder">画像デコーダ（省略時は各実装の既定を使用）</param>
    /// <returns>IImageSource のインスタンス（必要なら非同期で初期化されたもの）</returns>
    public static async Task<IImageSource> CreateSourceAsync(string path, IImageDecoder? decoder = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        // 1. フォルダ判定
        if (Directory.Exists(path)) return new FolderImageSource(path, decoder);

        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext switch
        {
            ".zip" or ".cbz" => new ArchiveImageSource(path, decoder),
            ".pdf" => await PdfImageSource.CreateAsync(path, decoder),
            _ when ImageSourceBase.IsStaticImageFile(path) =>
                new FolderImageSource(Path.GetDirectoryName(path) ?? throw new InvalidOperationException(), decoder),
            _ => throw new NotSupportedException($"未対応のフォーマットです: {ext}")
        };
    }
}