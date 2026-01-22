using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.Imaging.Decoders
{
    /// <summary>
    /// SkiaImageLoader の同期 API をラップして IImageDecoder の非同期 API を実現するアダプタ。
    /// 元の SkiaImageLoader はスレッドセーフなため、バックグラウンドスレッドで実行することで UI スレッドをブロックしません。
    /// </summary>
    public class SkiaImageDecoder : IImageDecoder
    {
        public Task<BitmapSource?> LoadImageAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            // SkiaImageLoader.LoadImage は同期 API のため Task.Run でラップする
            return Task.Run(() => SkiaImageLoader.LoadImage(stream), cancellationToken);
        }

        public Task<BitmapSource?> LoadThumbnailAsync(Stream stream, int targetWidth, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => SkiaImageLoader.LoadThumbnail(stream, targetWidth), cancellationToken);
        }
    }
}
