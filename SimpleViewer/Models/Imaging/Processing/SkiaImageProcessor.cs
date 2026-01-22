using SimpleViewer.Models.Imaging.Decoders;
using SkiaSharp;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.IO;
using SimpleExifLib;

namespace SimpleViewer.Models.Imaging.Processing;

/// <summary>
/// SkiaSharpを使用した画像デコード処理を担当するクラス。
/// フルサイズの画像読み込みとサムネイル生成をサポートします。
/// </summary>
public class SkiaImageProcessor
{
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly ImageOrientationHandler _orientationHandler;
    private readonly SkiaBitmapDecoder _bitmapDecoder;
    private readonly Func<bool> _getApplyExifOrientation;

    /// <summary>
    /// SkiaImageProcessorを初期化します。
    /// </summary>
    /// <param name="getApplyExifOrientation">EXIF Orientationを適用するかどうかを返すデリゲート。nullの場合は常にtrueとして扱います。</param>
    public SkiaImageProcessor(Func<bool>? getApplyExifOrientation = null)
    {
        _memoryStreamManager = new RecyclableMemoryStreamManager();
        _orientationHandler = new ImageOrientationHandler();
        _bitmapDecoder = new SkiaBitmapDecoder();
        _getApplyExifOrientation = getApplyExifOrientation ?? (() => true);
    }

    /// <summary>
    /// バイト配列からフルサイズのBitmapSourceをデコードします。
    /// </summary>
    /// <param name="data">画像データ</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    public BitmapSource? DecodeFullImage(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        try
        {
            using var stream = _memoryStreamManager.GetStream();
            stream.Write(data, 0, data.Length);
            stream.Position = 0;
            return DecodeFullImage(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageProcessor] フルデコードエラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ストリームからフルサイズのBitmapSourceをデコードします。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    public BitmapSource? DecodeFullImage(Stream stream)
    {
        if (stream == null) return null;

        try
        {
            if (!stream.CanSeek)
            {
                return DecodeFromNonSeekableStream(stream);
            }
            else
            {
                return DecodeFromSeekableStream(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageProcessor] ストリームデコードエラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// バイト配列からサムネイルを生成します。
    /// </summary>
    /// <param name="data">画像データ</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時はnull</returns>
    public BitmapSource? DecodeThumbnail(byte[] data, int targetWidth)
    {
        if (data == null || data.Length == 0 || targetWidth <= 0) return null;

        try
        {
            using var stream = _memoryStreamManager.GetStream();
            stream.Write(data, 0, data.Length);
            stream.Position = 0;
            return DecodeThumbnail(stream, targetWidth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageProcessor] サムネイル生成失敗: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ストリームからサムネイルを生成します。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時はnull</returns>
    public BitmapSource? DecodeThumbnail(Stream stream, int targetWidth)
    {
        if (stream == null || targetWidth <= 0) return null;

        Stream processStream = stream;
        bool shouldDisposeStream = false;

        try
        {
            if (!stream.CanSeek)
            {
                var tempStream = _memoryStreamManager.GetStream();
                stream.CopyTo(tempStream);
                tempStream.Position = 0;
                processStream = tempStream;
                shouldDisposeStream = true;
            }

            return DecodeThumbnailFromStream(processStream, targetWidth);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkiaImageProcessor] サムネイル生成失敗: {ex.Message}");
            return null;
        }
        finally
        {
            if (shouldDisposeStream)
            {
                processStream.Dispose();
            }
        }
    }

    /// <summary>
    /// シーク不可能なストリームから画像をデコードします。
    /// 内部で一時ストリームにコピーしてから処理します。
    /// </summary>
    /// <param name="stream">シーク不可能なストリーム</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    private BitmapSource? DecodeFromNonSeekableStream(Stream stream)
    {
        using var tempStream = _memoryStreamManager.GetStream();
        stream.CopyTo(tempStream);
        tempStream.Position = 0;

        int orientation = ReadOrientationIfEnabled(tempStream);
        tempStream.Position = 0;

        var bitmap = _bitmapDecoder.DecodeBitmap(tempStream);
        if (bitmap == null) return null;

        var result = ConvertAndApplyOrientation(bitmap, orientation);
        bitmap.Dispose();
        return result;
    }

    /// <summary>
    /// シーク可能なストリームから画像をデコードします。
    /// ストリーム位置を保持したままEXIF読み取りとデコードを行います。
    /// </summary>
    /// <param name="stream">シーク可能なストリーム</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    private BitmapSource? DecodeFromSeekableStream(Stream stream)
    {
        long originalPosition = stream.Position;
        int orientation = ReadOrientationIfEnabled(stream);
        stream.Position = originalPosition;

        var bitmap = _bitmapDecoder.DecodeBitmap(stream);
        if (bitmap == null) return null;

        var result = ConvertAndApplyOrientation(bitmap, orientation);
        bitmap.Dispose();
        return result;
    }

    /// <summary>
    /// ストリームからサムネイルをデコードします。
    /// SKCodecを使用して効率的にスケールダウンしながら読み込みます。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時はnull</returns>
    private BitmapSource? DecodeThumbnailFromStream(Stream stream, int targetWidth)
    {
        long originalPosition = stream.CanSeek ? stream.Position : 0;
        
        int orientation = ReadOrientationIfEnabled(stream);
        if (stream.CanSeek) stream.Position = originalPosition;

        using var codec = SKCodec.Create(stream);
        if (codec == null) return null;

        // SKCodec.Createがストリームを消費するため、位置をリセット
        if (stream.CanSeek) stream.Position = originalPosition;

        var scaledBitmap = _bitmapDecoder.DecodeScaledBitmap(stream, targetWidth);
        if (scaledBitmap == null)
        {
            // スケールデコードに失敗した場合はフルサイズデコードにフォールバック
            if (stream.CanSeek) stream.Position = originalPosition;
            return DecodeFullImage(stream);
        }

        int targetHeight = (int)(codec.Info.Height * ((double)targetWidth / codec.Info.Width));
        var finalBitmap = _bitmapDecoder.ResizeBitmap(scaledBitmap, targetWidth, targetHeight);
        
        if (finalBitmap != scaledBitmap)
        {
            scaledBitmap.Dispose();
        }

        var result = ConvertAndApplyOrientation(finalBitmap, orientation);
        finalBitmap.Dispose();
        return result;
    }

    /// <summary>
    /// 設定に応じてEXIF Orientationを読み取ります。
    /// 設定が無効な場合は常に1（回転なし）を返します。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <returns>Orientation値（1-8）、設定無効時は常に1</returns>
    private int ReadOrientationIfEnabled(Stream stream)
    {
        if (!_getApplyExifOrientation())
        {
            // EXIF適用無効の場合は常に1（回転なし）を返す
            return 1;
        }
        return ExifReaderFactory.ReadOrientation(stream);
    }

    /// <summary>
    /// SKBitmapをBitmapSourceに変換し、EXIF Orientationを適用します。
    /// </summary>
    /// <param name="bitmap">変換元のSKBitmap</param>
    /// <param name="orientation">EXIF Orientation値（1-8）</param>
    /// <returns>変換および回転・反転が適用されたBitmapSource</returns>
    private BitmapSource? ConvertAndApplyOrientation(SKBitmap bitmap, int orientation)
    {
        var writeableBitmap = _bitmapDecoder.ConvertToWriteableBitmap(bitmap);
        var bitmapSource = _orientationHandler.ApplyOrientation(writeableBitmap, orientation);
        bitmapSource.Freeze();
        return bitmapSource;
    }
}
