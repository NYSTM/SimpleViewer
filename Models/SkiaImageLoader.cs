using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.IO;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

/// <summary>
/// SkiaSharpを使用して画像デコード（WebP, JPG, PNG等）を高速に行うユーティリティクラス
/// </summary>
public static class SkiaImageLoader
{
    /// <summary>
    /// バイト配列からフルサイズのBitmapSourceを高速にデコードします。
    /// </summary>
    /// <param name="data">画像のバイナリデータ</param>
    /// <returns>Freeze済みのBitmapSource（UIスレッド以外でも利用可能）</returns>
    public static BitmapSource? LoadImage(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        try
        {
            // SkiaSharpでデコード
            using var skBitmap = SKBitmap.Decode(data);
            if (skBitmap == null) return null;

            // SkiaのBitmapをWPFのBitmapSource（WriteableBitmap）に変換
            var bitmapSource = skBitmap.ToWriteableBitmap();

            // 重要：Freezeすることで、生成したスレッド以外（UIスレッド等）からアクセス可能にし、描画性能を向上させる
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] フルデコードエラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 指定した幅に合わせて、デコード時にリサイズを行いながらBitmapSourceを生成します。
    /// メモリ消費を大幅に抑え、サムネイル表示を劇的に高速化します。
    /// </summary>
    /// <param name="data">画像のバイナリデータ</param>
    /// <param name="targetWidth">リサイズ後の幅（ピクセル）</param>
    /// <returns>Freeze済みのサムネイル用BitmapSource</returns>
    public static BitmapSource? LoadThumbnail(byte[] data, int targetWidth)
    {
        if (data == null || data.Length == 0 || targetWidth <= 0) return null;

        try
        {
            using var ms = new MemoryStream(data);
            using var codec = SKCodec.Create(ms);
            if (codec == null) return null;

            // 1. コーデックが許容する最適な縮小サイズを取得
            double ratio = (double)targetWidth / codec.Info.Width;
            SKSizeI supportedDimensions = codec.GetScaledDimensions((float)ratio);

            // 2. デコード用の Info を作成
            SKImageInfo decodeInfo = new SKImageInfo(
                supportedDimensions.Width,
                supportedDimensions.Height,
                codec.Info.ColorType,
                codec.Info.AlphaType,
                codec.Info.ColorSpace);

            // 3. デコード用の一時バッファを確保
            using var tempBitmap = new SKBitmap(decodeInfo);
            var result = codec.GetPixels(decodeInfo, tempBitmap.GetPixels());

            if (result != SKCodecResult.Success)
            {
                return LoadImage(data);
            }

            // 4. 指定サイズへの最終リサイズ（最新の SKSamplingOptions を使用）
            int targetHeight = (int)(codec.Info.Height * ((double)targetWidth / codec.Info.Width));
            var finalInfo = new SKImageInfo(targetWidth, targetHeight, decodeInfo.ColorType, decodeInfo.AlphaType, decodeInfo.ColorSpace);

            var finalBitmap = new SKBitmap(finalInfo);

            // 【修正ポイント】SKSamplingOptions の使用
            // SKFilterQuality.High に相当するのは SKCubicResampler.CatmullRom です。
            // 一般的な高品質リサイズには、SKFilterMode.Linear と SKMipmapMode.Linear の組み合わせも有効です。
            var sampling = new SKSamplingOptions(SKCubicResampler.CatmullRom);

            // 新しいオーバーロードを使用
            if (tempBitmap.ScalePixels(finalBitmap, sampling))
            {
                var source = finalBitmap.ToWriteableBitmap();
                finalBitmap.Dispose();
                source.Freeze();
                return source;
            }
            else
            {
                var source = tempBitmap.ToWriteableBitmap();
                source.Freeze();
                return source;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] サムネイル生成失敗: {ex.Message}");
            return null;
        }
    }
}