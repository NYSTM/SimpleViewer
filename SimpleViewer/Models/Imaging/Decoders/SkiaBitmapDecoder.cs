using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.IO;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.Imaging.Decoders;

/// <summary>
/// SkiaSharpを使用したビットマップのデコードとリサイズを担当するクラス。
/// </summary>
public class SkiaBitmapDecoder
{
    /// <summary>
    /// ストリームからSKBitmapをデコードします。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <returns>デコードされたSKBitmap、失敗時はnull</returns>
    public SKBitmap? DecodeBitmap(Stream stream)
    {
        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;

        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            codec.Info.ColorType,
            codec.Info.AlphaType,
            codec.Info.ColorSpace);

        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());

        if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>
    /// ストリームからスケール済みのSKBitmapをデコードします。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>デコードされたSKBitmap、失敗時はnull</returns>
    public SKBitmap? DecodeScaledBitmap(Stream stream, int targetWidth)
    {
        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;

        double ratio = (double)targetWidth / codec.Info.Width;
        var scaledSize = codec.GetScaledDimensions((float)ratio);

        var decodeInfo = new SKImageInfo(
            scaledSize.Width,
            scaledSize.Height,
            codec.Info.ColorType,
            codec.Info.AlphaType,
            codec.Info.ColorSpace);

        var bitmap = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());

        if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>
    /// SKBitmapを指定サイズへリサイズします。
    /// </summary>
    /// <param name="source">元のビットマップ</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <param name="targetHeight">目標高さ（ピクセル）</param>
    /// <returns>リサイズされたSKBitmap、失敗時は元のビットマップ</returns>
    public SKBitmap ResizeBitmap(SKBitmap source, int targetWidth, int targetHeight)
    {
        try
        {
            var finalInfo = new SKImageInfo(
                targetWidth,
                targetHeight,
                source.Info.ColorType,
                source.Info.AlphaType,
                source.Info.ColorSpace);

            var finalBitmap = new SKBitmap(finalInfo);
            var sampling = new SKSamplingOptions(SKCubicResampler.CatmullRom);

            if (source.ScalePixels(finalBitmap, sampling))
            {
                return finalBitmap;
            }

            finalBitmap.Dispose();
            return source;
        }
        catch
        {
            return source;
        }
    }

    /// <summary>
    /// SKBitmapをWriteableBitmapに変換します。
    /// </summary>
    /// <param name="bitmap">SKBitmap</param>
    /// <returns>WriteableBitmap</returns>
    public WriteableBitmap ConvertToWriteableBitmap(SKBitmap bitmap)
    {
        return bitmap.ToWriteableBitmap();
    }
}
