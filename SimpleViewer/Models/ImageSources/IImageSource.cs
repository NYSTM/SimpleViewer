using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// 画像ソースの共通インターフェース。
/// 各実装はファイル・アーカイブ・PDF など異なるバックエンドからページ（画像）を提供します。
/// Presenter 側はこのインターフェースを通じてページ数取得・ページ画像取得・サムネイル取得を行います。
/// </summary>
public interface IImageSource : IDisposable
{
    /// <summary>
    /// このソースを一意に識別するキー（通常はファイルパス）を取得します。
    /// キャッシュキーの生成に使用されます。
    /// </summary>
    string SourceIdentifier { get; }

    /// <summary>
    /// 総ページ数（またはエントリ数）を非同期で返します。
    /// 戻り値は 0 以上の整数を想定します。
    /// </summary>
    Task<int> GetPageCountAsync();

    /// <summary>
    /// 指定インデックスのフルサイズ画像を非同期で取得します（高速フルデコード経路）。
    /// 実装はデコード処理をバックグラウンドスレッドで行い、UI スレッドをブロックしないことが期待されます。
    /// 失敗時は null を返します。
    /// </summary>
    Task<BitmapSource?> GetPageImageAsync(int index);

    /// <summary>
    /// 指定インデックスのサムネイルを非同期で取得します。
    /// デコード時にリサイズを行うことでメモリ使用量を抑える実装が推奨されます。
    /// 失敗時は null を返します。
    /// </summary>
    Task<BitmapSource?> GetThumbnailAsync(int index, int width);

    /// <summary>
    /// ツリー表示など UI 用に、ページやエントリを示す相対パス一覧を返します。
    /// 返される順序はページインデックスと一致することを保証してください。
    /// </summary>
    Task<IReadOnlyList<string>> GetFileListAsync();
}