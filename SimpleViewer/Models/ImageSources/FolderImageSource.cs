using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Utils.Comparers;
using System.IO;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// 指定フォルダ内の画像ファイルをページとして扱う画像ソース実装。
/// IImageDecoder を注入可能にしてデコード責務を切り離します。
/// </summary>
public class FolderImageSource : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths;
    private readonly IImageDecoder _decoder;
    private readonly string _folderPath;

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
    /// ファイル読み込みとデコードはバックグラウンドスレッドで実行され、UI スレッドをブロックしません。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        try
        {
            // ファイルをバイト配列に読み込んでからデコードする
            // これにより、デコード中にファイルストリームが閉じられる問題を回避
            byte[] fileData = await File.ReadAllBytesAsync(_filePaths[index]).ConfigureAwait(false);
            
            using var ms = new MemoryStream(fileData);
            var bitmap = await _decoder.LoadImageAsync(ms).ConfigureAwait(false);
            if (bitmap != null && bitmap.CanFreeze) bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FolderImageSource] ページ読み込みエラー {index}: {ex.Message}");
            return null;
        }
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

        string filePath = _filePaths[index];
        try
        {
            Debug.WriteLine($"[FolderImageSource] サムネイル生成開始: index={index}, file={Path.GetFileName(filePath)}, width={width}");
            
            // ファイルをバイト配列に読み込んでからデコードする
            // これにより、デコード中にファイルストリームが閉じられる問題を回避
            byte[] fileData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            
            Debug.WriteLine($"[FolderImageSource] ファイル読み込み完了: {fileData.Length} bytes");
            
            using var ms = new MemoryStream(fileData);
            var thumb = await _decoder.LoadThumbnailAsync(ms, width).ConfigureAwait(false);
            
            if (thumb != null)
            {
                if (thumb.CanFreeze) thumb.Freeze();
                Debug.WriteLine($"[FolderImageSource] サムネイル生成成功: {thumb.PixelWidth}x{thumb.PixelHeight}");
            }
            else
            {
                Debug.WriteLine($"[FolderImageSource] サムネイル生成失敗: デコーダがnullを返しました");
            }
            
            return thumb;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FolderImageSource] サムネイル読み込みエラー index={index}, file={Path.GetFileName(filePath)}: {ex.GetType().Name} - {ex.Message}");
            Debug.WriteLine($"[FolderImageSource] スタックトレース: {ex.StackTrace}");
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