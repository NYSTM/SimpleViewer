using System.IO;

namespace SimpleViewer.Models;

public interface IImageSource : IDisposable
{
    /// <summary>
    /// 総ページ数（画像ファイル数）を取得する
    /// </summary>
    Task<int> GetPageCountAsync();

    /// <summary>
    /// 指定したインデックスの画像データをStreamとして取得する
    /// </summary>
    Stream? GetPageStream(int index);
}