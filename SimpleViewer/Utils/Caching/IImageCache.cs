using System.Windows.Media.Imaging;

namespace SimpleViewer.Utils.Caching;

/// <summary>
/// 画像キャッシュの抽象インターフェイス。
/// Presenter などはこのインターフェイス経由でキャッシュ操作を行います。
/// </summary>
public interface IImageCache
{
    bool TryGet(int index, out BitmapSource? bitmap);
    void Add(int index, BitmapSource bitmap);
    bool TryRemove(int index);
    void Clear();
    int Count { get; }
    void PurgeIfNeeded(int centerIndex);
}
