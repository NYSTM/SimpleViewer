using SimpleViewer.Models.Imaging.Processing;
using System.IO;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.Imaging.Decoders;

/// <summary>
/// SkiaSharpを使った画像デコードのユーティリティ（同期API）。
/// 内部でSkiaImageProcessorを使用して処理を委譲します。
/// 非同期で利用する場合はIImageDecoderを実装したラッパを使用してください。
/// </summary>
public static class SkiaImageLoader
{
    private static SkiaImageProcessor? _processor;
    private static Func<bool>? _getApplyExifOrientation;

    /// <summary>
    /// EXIF Orientation適用設定を取得するデリゲートを設定します。
    /// アプリケーション起動時に一度だけ呼び出してください。
    /// </summary>
    /// <param name="getApplyExifOrientation">EXIF適用設定を返すデリゲート</param>
    public static void Initialize(Func<bool> getApplyExifOrientation)
    {
        _getApplyExifOrientation = getApplyExifOrientation;
        _processor = new SkiaImageProcessor(getApplyExifOrientation);
    }

    private static SkiaImageProcessor GetProcessor()
    {
        if (_processor == null)
        {
            // 初期化されていない場合は既定値（常にtrueを返すデリゲート）で初期化
            _processor = new SkiaImageProcessor();
        }
        return _processor;
    }

    /// <summary>
    /// バイト配列からフルサイズのBitmapSourceをデコードして返します（同期）。
    /// </summary>
    /// <param name="data">画像データ</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    public static BitmapSource? LoadImage(byte[] data)
    {
        return GetProcessor().DecodeFullImage(data);
    }

    /// <summary>
    /// ストリームからフルサイズのBitmapSourceをデコードします（同期）。
    /// 非シーク可能ストリームは内部で一時コピーされます。
    /// EXIFのOrientationを確認して回転/反転を適用します。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <returns>デコードされた画像、失敗時はnull</returns>
    public static BitmapSource? LoadImage(Stream stream)
    {
        return GetProcessor().DecodeFullImage(stream);
    }

    /// <summary>
    /// バイト配列からターゲット幅に合わせたサムネイルを生成します（同期）。
    /// </summary>
    /// <param name="data">画像データ</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時はnull</returns>
    public static BitmapSource? LoadThumbnail(byte[] data, int targetWidth)
    {
        return GetProcessor().DecodeThumbnail(data, targetWidth);
    }

    /// <summary>
    /// ストリームからデコード時にリサイズを行いサムネイルを生成します（同期）。
    /// 非シーク可能ストリームは一時コピーしてから処理します。
    /// EXIFのOrientationを確認して回転/反転を適用します。
    /// </summary>
    /// <param name="stream">画像ストリーム</param>
    /// <param name="targetWidth">目標幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時はnull</returns>
    public static BitmapSource? LoadThumbnail(Stream stream, int targetWidth)
    {
        return GetProcessor().DecodeThumbnail(stream, targetWidth);
    }
}
