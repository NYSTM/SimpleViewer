using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

public interface IImageSource : IDisposable
{
    Task<int> GetPageCountAsync();

    /// <summary> 高速フルデコードによる画像取得 </summary>
    Task<BitmapSource?> GetPageImageAsync(int index);

    /// <summary> デコード時リサイズによる高速サムネイル取得 </summary>
    Task<BitmapSource?> GetThumbnailAsync(int index, int width);
}