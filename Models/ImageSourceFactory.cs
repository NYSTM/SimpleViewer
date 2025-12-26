using System.IO;

namespace SimpleViewer.Models;

/// <summary>
/// パスに応じて適切な IImageSource インスタンスを生成する静的ファクトリ
/// </summary>
public static class ImageSourceFactory
{
    public static async Task<IImageSource> CreateSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        // 1. フォルダ判定
        if (Directory.Exists(path)) return new FolderImageSource(path);

        // 2. 拡張子による分岐
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext switch
        {
            ".zip" or ".cbz" => new ArchiveImageSource(path),
            ".pdf" => await PdfImageSource.CreateAsync(path),

            // 3. 単一画像ファイルの場合は親フォルダを開く
            _ when ImageSourceBase.IsStaticImageFile(path) =>
                new FolderImageSource(Path.GetDirectoryName(path) ?? throw new InvalidOperationException()),

            _ => throw new NotSupportedException($"未対応のフォーマットです: {ext}")
        };
    }
}