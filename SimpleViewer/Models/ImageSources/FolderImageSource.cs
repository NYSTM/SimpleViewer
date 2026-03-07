using Microsoft.IO;
using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Utils.Comparers;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// 指定フォルダ内の画像ファイルをページとして扱う画像ソース実装。
/// IImageDecoder を注入可能にしてデコード責務を切り離します。
/// RecyclableMemoryStream を使用してメモリ割り当てを最適化します。
/// </summary>
public class FolderImageSource : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths;
    private readonly IImageDecoder _decoder;
    private readonly string _folderPath;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;

    /// <summary>
    /// このソースを一意に識別するキー（フォルダパス）
    /// </summary>
    public string SourceIdentifier => _folderPath;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="folderPath">対象フォルダのパス</param>
    /// <param name="decoder">画像デコーダ（省略時は SkiaImageDecoder が利用されます）</param>
    public FolderImageSource(string folderPath, IImageDecoder? decoder = null)
    {
        _folderPath = Path.GetFullPath(folderPath);
        _decoder = decoder ?? new SkiaImageDecoder();
        _memoryStreamManager = new RecyclableMemoryStreamManager();

        _filePaths = Directory.Exists(folderPath)
            ? Directory.GetFiles(folderPath)
                .Where(IsStaticImageFile)
                .OrderBy(path => path, new NaturalStringComparer())
                .ToList()
            : new List<string>();
    }

    /// <summary>
    /// フォルダ内の画像ファイル数を返します。
    /// </summary>
    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    /// <summary>
    /// 指定インデックスの画像をフルサイズで非同期にロードします。
    /// RecyclableMemoryStream を使用してメモリプールから効率的にストリームを取得し、
    /// デコーダがストリームを安全に使用できるようにします。
    /// ファイル読み込みとデコードはバックグラウンドスレッドで実行され、UI スレッドをブロックしません。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <returns>画像、失敗時は null</returns>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        try
        {
            // RecyclableMemoryStream を使用してメモリプールから取得
            using var ms = _memoryStreamManager.GetStream();
            
            // FileStream から RecyclableMemoryStream にコピー
            using (var fileStream = new FileStream(
                _filePaths[index],
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                useAsync: true))
            {
                await fileStream.CopyToAsync(ms).ConfigureAwait(false);
            }
            
            ms.Position = 0;
            var bitmap = await _decoder.LoadImageAsync(ms).ConfigureAwait(false);
            if (bitmap != null && bitmap.CanFreeze) bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 指定インデックスのサムネイルを非同期に取得します。
    /// RecyclableMemoryStream を使用してメモリプールから効率的にストリームを取得します。
    /// Skia 側でデコード時にリサイズを行うことでメモリ消費を抑えます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">ターゲット幅（ピクセル）</param>
    /// <returns>サムネイル画像、失敗時は null</returns>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        string filePath = _filePaths[index];
        try
        {
            // RecyclableMemoryStream を使用してメモリプールから取得
            using var ms = _memoryStreamManager.GetStream();
            
            // FileStream から RecyclableMemoryStream にコピー
            using (var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                useAsync: true))
            {
                await fileStream.CopyToAsync(ms).ConfigureAwait(false);
            }
            
            ms.Position = 0;
            var thumb = await _decoder.LoadThumbnailAsync(ms, width).ConfigureAwait(false);
            
            if (thumb != null && thumb.CanFreeze)
            {
                thumb.Freeze();
            }
            
            return thumb;
        }
        catch
        {
            return null;
        }
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