using SimpleViewer.Utils;
using System.IO;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SimpleViewer.Models;

/// <summary>
/// 指定フォルダ内の画像ファイルをページとして扱う画像ソース実装。
/// フォルダ内の静的画像ファイルを収集し、インデックス順でページを提供します。
/// </summary>
/// <param name="folderPath">対象フォルダのパス</param>
public class FolderImageSource(string folderPath) : ImageSourceBase, IImageSource
{
    // フォルダが存在する場合は画像拡張子のファイル一覧を自然順で取得する
    // Directory.GetFiles の結果に LINQ の Where/OrderBy を適用している
    private readonly List<string> _filePaths = Directory.Exists(folderPath)
        ? Directory.GetFiles(folderPath)
            .Where(IsStaticImageFile) // 基底クラスの拡張子判定を利用
            .OrderBy(path => path, new NaturalStringComparer())
            .ToList()
        : new List<string>();

    /// <summary>
    /// フォルダ内の画像ファイル数を返します。
    /// </summary>
    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    /// <summary>
    /// 指定インデックスの画像をフルサイズで非同期にロードします。
    /// ファイル読み込みとデコードはバックグラウンドスレッドで実行され、UI スレッドをブロックしません。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                // ファイルストリームを直接 Skia に渡してデコードすることで
                // 不要なバイト配列コピーを避けてメモリ効率を向上させる
                using var fs = File.OpenRead(_filePaths[index]);
                var bitmap = SkiaImageLoader.LoadImage(fs);

                // UI スレッド以外で作成された BitmapSource は Freeze して共有可能にする
                bitmap?.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                // ロード失敗はログに記録して null を返す（UI 側で適切に扱う）
                Debug.WriteLine($"[FolderImageSource] ページ読み込みエラー {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// 指定インデックスのサムネイルを非同期に取得します。
    /// Skia 側でデコード時にリサイズを行うことでメモリ消費を抑えます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">ターゲット幅（ピクセル）</param>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                using var fs = File.OpenRead(_filePaths[index]);

                // デコード時のリサイズによりメモリ/CPU を節約する
                var thumb = SkiaImageLoader.LoadThumbnail(fs, width);

                // UI で共有するため Freeze
                thumb?.Freeze();
                return thumb;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FolderImageSource] サムネイル読み込みエラー {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// リソース解放。内部リストをクリアして基底クラスの Dispose を呼び出す。
    /// </summary>
    public override void Dispose()
    {
        _filePaths.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// ツリー表示用にファイル名の相対パス一覧を返します。
    /// 返却順はページインデックスと一致します。
    /// </summary>
    public Task<IReadOnlyList<string>> GetFileListAsync()
    {
        IReadOnlyList<string> list = _filePaths.Select(p => Path.GetFileName(p)).ToList();
        return Task.FromResult(list);
    }
}