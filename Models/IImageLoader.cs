using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

/// <summary>
/// 画像ソース（フォルダ、ZIP、PDFなど）を抽象化するインターフェース
/// </summary>
public interface IImageLoader : IDisposable
{
    /// <summary>
    /// 総ページ（画像）数
    /// </summary>
    int TotalPages { get; }

    /// <summary>
    /// 指定されたパスからリソースを初期化する
    /// </summary>
    /// <param name="path">ファイルパスまたはディレクトリパス</param>
    Task InitializeAsync(string path);

    /// <summary>
    /// メイン表示用のフルサイズ画像を非同期で読み込む
    /// </summary>
    /// <param name="index">ページインデックス（0〜）</param>
    /// <returns>読み込まれた画像。失敗時はnull</returns>
    Task<BitmapSource?> LoadPageAsync(int index);

    /// <summary>
    /// サイドバーやカタログ表示用の縮小画像を非同期で読み込む
    /// </summary>
    /// <param name="index">ページインデックス（0〜）</param>
    /// <param name="decodeWidth">デコード時の希望幅（ピクセル単位）</param>
    /// <returns>縮小された画像。失敗時はnull</returns>
    /// <remarks>
    /// 巨大な画像をそのまま読み込むとメモリを圧迫するため、
    /// このメソッドでは内部的に DecodePixelWidth 等を使用して高速化します。
    /// </remarks>
    Task<BitmapSource?> LoadThumbnailAsync(int index, int decodeWidth);
}