using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.Imaging.Decoders
{
    /// <summary>
    /// 画像デコード処理を抽象化するインターフェイス。
    /// 実装は任意のデコーダ（SkiaSharp 等）を提供でき、非同期 API を公開します。
    /// </summary>
    public interface IImageDecoder
    {
        /// <summary>
        /// ストリームからフルサイズ画像を非同期に読み込んで返します。
        /// </summary>
        Task<BitmapSource?> LoadImageAsync(Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// ストリームから指定幅のサムネイルを非同期に生成して返します。
        /// </summary>
        Task<BitmapSource?> LoadThumbnailAsync(Stream stream, int targetWidth, CancellationToken cancellationToken = default);
    }
}
