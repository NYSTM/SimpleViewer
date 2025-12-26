using System.IO;

namespace SimpleViewer.Models;

public static class ImageSourceFactory
{
    public static async Task<IImageSource> CreateSourceAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

        // 1. フォルダの場合
        if (Directory.Exists(path))
        {
            return new FolderImageSource(path);
        }

        var ext = Path.GetExtension(path).ToLower();

        // 2. アーカイブ（Zip）の場合
        if (ext == ".zip")
        {
            return new ArchiveImageSource(path);
        }

        // 3. PDFの場合 (非同期作成を正しく待機)
        if (ext == ".pdf")
        {
            return await PdfImageSource.CreateAsync(path);
        }

        // 4. 単一画像ファイルの場合
        // そのファイルがあるフォルダを FolderImageSource として開き、
        // Presenter側でその画像のインデックスから開始できるようにするのが一般的です
        var directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            return new FolderImageSource(directory);
        }

        throw new NotSupportedException($"未対応のフォーマットです: {ext}");
    }
}