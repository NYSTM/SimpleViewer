using SimpleViewer.Utils;
using System.IO;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SimpleViewer.Models;

/// <summary>
/// フォルダ内の画像ファイルを管理するソース。
/// </summary>
/// <param name="folderPath">対象フォルダのパス</param>
public class FolderImageSource(string folderPath) : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths = Directory.Exists(folderPath)
        ? Directory.GetFiles(folderPath)
            .Where(IsStaticImageFile) // 基底クラスの静的メソッドを利用
            .OrderBy(path => path, new NaturalStringComparer())
            .ToList()
        : [];

    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    /// <summary>
    /// 指定インデックスの画像をフルサイズで非同期にロードします。
    /// </summary>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                // File.ReadAllBytes は内部で FileStream を適切に処理するため、
                // ロック競合を避けつつ高速にメモリへ展開できます。
                byte[] data = File.ReadAllBytes(_filePaths[index]);

                var bitmap = SkiaImageLoader.LoadImage(data);

                // UIスレッド以外で作成した Bitmap を、PresenterやViewで
                // 安全かつ高速に利用するために Freeze します。
                bitmap?.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FolderImageSource] Error loading page {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// サムネイル表示用にリサイズされた画像を非同期にロードします。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                byte[] data = File.ReadAllBytes(_filePaths[index]);

                // SkiaSharp側でデコード時にリサイズを行うことでメモリ消費を抑えます。
                var thumb = SkiaImageLoader.LoadThumbnail(data, width);

                thumb?.Freeze();
                return thumb;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FolderImageSource] Error loading thumbnail {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// ImageSourceBase.Dispose をオーバーライドしてリソースを解放します。
    /// </summary>
    public override void Dispose()
    {
        _filePaths.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}