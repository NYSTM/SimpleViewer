using SkiaSharp;
using SkiaSharp.Views.WPF;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.IO;

namespace SimpleViewer.Models;

/// <summary>
/// SkiaSharp を使った画像デコードのユーティリティ。
/// 主にストリーム経由で Skia の SKCodec を使用して高速にデコードし、
/// WPF の BitmapSource に変換して返します。
///
/// 最適化ポイント:
/// - 大量の一時的な MemoryStream の割当を避けるため RecyclableMemoryStreamManager を共有利用します。
/// - SKCodec はランダムアクセス（シーク可能）ストリームを要求することがあるため、
///   非シーク可能ストリームは一時的にメモリへコピーしてからデコードします。
/// - UI スレッド以外で生成した BitmapSource は Freeze してスレッド間で安全に共有します。
/// </summary>
public static class SkiaImageLoader
{
    // RecyclableMemoryStreamManager を共有してメモリストリームの再利用を行う
    private static readonly RecyclableMemoryStreamManager recyclableManager = new();

    /// <summary>
    /// バイト配列からフルサイズの BitmapSource をデコードして返します。
    /// 内部では RecyclableMemoryStream を使って一時ストリームを作成し、
    /// `LoadImage(Stream)` に処理を委譲します。
    /// </summary>
    public static BitmapSource? LoadImage(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        try
        {
            // RecyclableMemoryStream を使用して一時バッファを確保
            using var ms = recyclableManager.GetStream();
            ms.Write(data, 0, data.Length);
            ms.Position = 0;
            return LoadImage(ms);
        }
        catch (Exception ex)
        {
            // 例外はログに記録して null を返す（呼び出し側で適切に扱う）
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] フルデコードエラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ストリームからフルサイズの BitmapSource をデコードします。
    /// - 引数のストリームがシーク可能であれば直接 SKCodec に渡します。
    /// - 非シーク可能な場合は RecyclableMemoryStream にコピーしてからデコードします。
    ///
    /// このメソッドは I/O とデコードを行うため重い処理です。呼び出しはバックグラウンドスレッドで行うことを想定しています。
    /// </summary>
    public static BitmapSource? LoadImage(Stream stream)
    {
        if (stream == null) return null;
        try
        {
            // SKCodec は内部でランダムアクセスを要求する場合があり、非シーク可能ストリームだと
            // 正常にデコードできず画像の一部しか描画されない事象が発生することがあるため対処する。
            if (!stream.CanSeek)
            {
                // 非シーク可能ストリームは RecyclableMemoryStream にコピーしてから処理
                using var temp = recyclableManager.GetStream();
                stream.CopyTo(temp);
                temp.Position = 0;

                using var codec = SKCodec.Create(temp);
                if (codec == null) return null;

                // デコード用の情報を取得して SKBitmap を作成
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, codec.Info.ColorType, codec.Info.AlphaType, codec.Info.ColorSpace);
                using var bitmap = new SKBitmap(info);
                var result = codec.GetPixels(info, bitmap.GetPixels());

                if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                {
                    var bmp = bitmap.ToWriteableBitmap();
                    // UI スレッド以外で作成した BitmapSource は Freeze して共有可能にする
                    bmp.Freeze();
                    return bmp;
                }

                return null;
            }
            else
            {
                // シーク可能ストリームは直接 SKCodec に渡す
                using var codec = SKCodec.Create(stream);
                if (codec == null) return null;

                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, codec.Info.ColorType, codec.Info.AlphaType, codec.Info.ColorSpace);
                using var bitmap = new SKBitmap(info);
                var result = codec.GetPixels(info, bitmap.GetPixels());
                if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                {
                    var bmp = bitmap.ToWriteableBitmap();
                    bmp.Freeze();
                    return bmp;
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] ストリームデコードエラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// バイト配列からターゲット幅に合わせたサムネイルを生成します。
    /// 内部で RecyclableMemoryStream を利用して一時ストリームを作成します。
    /// </summary>
    public static BitmapSource? LoadThumbnail(byte[] data, int targetWidth)
    {
        if (data == null || data.Length == 0 || targetWidth <= 0) return null;
        try
        {
            using var ms = recyclableManager.GetStream();
            ms.Write(data, 0, data.Length);
            ms.Position = 0;
            return LoadThumbnail(ms, targetWidth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] サムネイル生成失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ストリームからデコード時にリサイズを行いサムネイルを生成します。
    /// - 非シーク可能ストリームは一時コピーしてから処理する。
    /// - SKCodec がサポートするスケールを活用し、可能な限りメモリ消費を抑えます。
    /// </summary>
    public static BitmapSource? LoadThumbnail(Stream stream, int targetWidth)
    {
        if (stream == null || targetWidth <= 0) return null;

        try
        {
            // 非シーク可能ストリームは一旦コピーしてから処理する
            Stream procStream = stream;
            bool disposeProcStream = false;

            if (!stream.CanSeek)
            {
                var tmp = recyclableManager.GetStream();
                stream.CopyTo(tmp);
                tmp.Position = 0;
                procStream = tmp;
                disposeProcStream = true;
            }

            try
            {
                using var codec = SKCodec.Create(procStream);
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

                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    // フォールバック: 全体をデコードしてからリサイズ
                    // procStream がシーク可能なら先頭へ戻して LoadImage を使う
                    if (procStream.CanSeek) procStream.Position = 0;
                    var full = LoadImage(procStream);
                    return full;
                }

                // 4. 指定サイズへの最終リサイズ（最新の SKSamplingOptions を使用）
                int targetHeight = (int)(codec.Info.Height * ((double)targetWidth / codec.Info.Width));
                var finalInfo = new SKImageInfo(targetWidth, targetHeight, decodeInfo.ColorType, decodeInfo.AlphaType, decodeInfo.ColorSpace);

                using var finalBitmap = new SKBitmap(finalInfo);
                var sampling = new SKSamplingOptions(SKCubicResampler.CatmullRom);

                if (tempBitmap.ScalePixels(finalBitmap, sampling))
                {
                    var source = finalBitmap.ToWriteableBitmap();
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
            finally
            {
                if (disposeProcStream) procStream.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageLoader] サムネイル生成失敗: {ex.Message}");
            return null;
        }
    }
}