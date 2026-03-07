using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.Imaging.Processing;

/// <summary>
/// EXIF Orientationに基づいて画像を回転/反転するクラス。
/// </summary>
public class ImageOrientationHandler
{
    /// <summary>
    /// Orientationが1でなければ回転/反転を適用します。
    /// </summary>
    /// <param name="source">元画像</param>
    /// <param name="orientation">EXIF Orientation値（1-8）</param>
    /// <returns>変換後の画像、sourceがnullの場合はnull</returns>
    public BitmapSource? ApplyOrientation(BitmapSource? source, int orientation)
    {
        if (source == null) return null;
        if (orientation <= 1) return source;

        Transform? transform = CreateTransform(orientation);
        if (transform == null) return source;

        return ApplyTransform(source, transform);
    }

    /// <summary>
    /// Orientation値に対応するTransformを生成します。
    /// </summary>
    /// <param name="orientation">EXIF Orientation値（1-8）</param>
    /// <returns>対応するTransform、該当なしの場合はnull</returns>
    private Transform? CreateTransform(int orientation)
    {
        return orientation switch
        {
            2 => new ScaleTransform(-1, 1),  // Flip horizontal
            3 => new RotateTransform(180),   // Rotate 180
            4 => new ScaleTransform(1, -1),  // Flip vertical
            5 => CreateTranspose(),          // Transpose: flip horizontal + rotate 90
            6 => new RotateTransform(90),    // Rotate 90
            7 => CreateTransverse(),         // Transverse: flip horizontal + rotate 270
            8 => new RotateTransform(270),   // Rotate 270
            _ => null
        };
    }

    /// <summary>
    /// Transpose変換を生成します（水平反転 + 90度回転）。
    /// </summary>
    private Transform CreateTranspose()
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(-1, 1));
        group.Children.Add(new RotateTransform(90));
        return group;
    }

    /// <summary>
    /// Transverse変換を生成します（水平反転 + 270度回転）。
    /// </summary>
    private Transform CreateTransverse()
    {
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(-1, 1));
        group.Children.Add(new RotateTransform(270));
        return group;
    }

    /// <summary>
    /// 画像にTransformを適用します。
    /// </summary>
    /// <param name="source">元画像</param>
    /// <param name="transform">適用するTransform</param>
    /// <returns>変換後の画像、失敗時は元画像</returns>
    private BitmapSource ApplyTransform(BitmapSource source, Transform transform)
    {
        try
        {
            var transformedBitmap = new TransformedBitmap(source, transform);
            transformedBitmap.Freeze();
            return transformedBitmap;
        }
        catch
        {
            return source;
        }
    }
}
