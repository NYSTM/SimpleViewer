using SimpleViewer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

public class FolderImageSource : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths;

    public FolderImageSource(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _filePaths = new List<string>();
            return;
        }

        // 修正ポイント1: 画像ファイルの列挙とソートを確実に行う
        _filePaths = Directory.GetFiles(folderPath)
            .Where(path => IsImageFile(path))
            .OrderBy(path => path, new NaturalStringComparer())
            .ToList();
    }

    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    /// <summary>
    /// 指定インデックスの画像をフルサイズでデコード
    /// </summary>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                // 修正ポイント2: File.ReadAllBytes を使用して SkiaImageLoader に渡す
                byte[] data = File.ReadAllBytes(_filePaths[index]);
                return SkiaImageLoader.LoadImage(data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FolderImageSource Error (Page): {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// 指定インデックスの画像をサムネイルとしてデコード
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                // 修正ポイント3: サムネイル用のデコード時リサイズを適用
                byte[] data = File.ReadAllBytes(_filePaths[index]);
                return SkiaImageLoader.LoadThumbnail(data, width);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FolderImageSource Error (Thumb): {ex.Message}");
                return null;
            }
        });
    }

    public void Dispose() { }
}