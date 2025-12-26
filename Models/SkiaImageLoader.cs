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
            // コーデックを作成（まだ全データは展開されない）
            using var codec = SKCodec.Create(ms);
            if (codec == null) return null;

            // 元のアスペクト比を維持してターゲットの高さを計算
            double ratio = (double)targetWidth / codec.Info.Width;
            int targetHeight = (int)(codec.Info.Height * ratio);

            // デコード後の情報を定義
            var info = new SKImageInfo(targetWidth, targetHeight);

            // 指定サイズでBitmapのメモリを確保
            using var skBitmap = new SKBitmap(info);

            // 【高速化の肝】GetPixelsを呼び出す際、SkiaSharpがデコードとリサイズを同時に行う
            // これにより、フルサイズのメモリを消費することなく縮小画像を得られる
            var result = codec.GetPixels(skBitmap.Info, skBitmap.GetPixels());

            if (result != SKCodecResult.Success) return null;

            var bitmapSource = skBitmap.ToWriteableBitmap();
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] サムネイルデコードエラー: {ex.Message}");
            return null;
        }
    }
}