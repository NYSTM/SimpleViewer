using SimpleViewer.Models.Imaging.Decoders;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models.ImageSources;

/// <summary>
/// ZIP アーカイブ内の画像エントリを読み出して BitmapSource として提供する画像ソース実装。
/// IImageDecoder を注入可能にしてデコード責務を分離します。
/// </summary>
public class ArchiveImageSource : ImageSourceBase, IImageSource
{
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _entries;
    private readonly object _zipLock = new();
    private readonly IImageDecoder _decoder;
    private readonly string _zipPath;

    /// <summary>
    /// このソースを一意に識別するキー（ZIPファイルパス）
    /// </summary>
    public string SourceIdentifier => _zipPath;

    /// <summary>
    /// 指定した ZIP ファイルを開き、画像エントリ一覧を準備します。
    /// </summary>
    /// <param name="zipPath">ZIP ファイルのパス</param>
    /// <param name="decoder">使用するデコーダのインスタンス</param>
    public ArchiveImageSource(string zipPath, IImageDecoder? decoder = null)
    {
        _zipPath = Path.GetFullPath(zipPath);
        _decoder = decoder ?? new SkiaImageDecoder();
        _archive = ZipFile.OpenRead(zipPath);
        _entries = _archive.Entries
            .Where(e => ImageSourceBase.IsStaticImageFile(e.FullName))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// ページ数（アーカイブ内の画像エントリ数）を非同期で返します。
    /// </summary>
    public Task<int> GetPageCountAsync() => Task.FromResult(_entries.Count);

    /// <summary>
    /// 指定インデックスの画像をフルサイズで読み込み、BitmapSource を返します。
    /// エントリのストリームを直接デコーダに渡し、不要なメモリコピーを避けます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;

        try
        {
            byte[]? buffer = null;
            // ZipArchive の内部ストリームを使う間はロックする
            lock (_zipLock)
            {
                using var entryStream = _entries[index].Open();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                buffer = ms.ToArray();
            }

            if (buffer == null || buffer.Length == 0) return null;

            using var input = new MemoryStream(buffer);
            var bmp = await _decoder.LoadImageAsync(input).ConfigureAwait(false);
            if (bmp != null && bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArchiveImageSource] アーカイブ読み込みエラー {index}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 指定インデックスのサムネイル（リサイズ済み）を取得します。
    /// デコーダ側でリサイズを行うことでメモリ消費を抑えます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">ターゲット幅（ピクセル）</param>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _entries.Count) return null;

        try
        {
            byte[]? buffer = null;
            lock (_zipLock)
            {
                using var entryStream = _entries[index].Open();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                buffer = ms.ToArray();
            }

            if (buffer == null || buffer.Length == 0) return null;

            using var input = new MemoryStream(buffer);
            var bmp = await _decoder.LoadThumbnailAsync(input, width).ConfigureAwait(false);
            if (bmp != null && bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArchiveImageSource] サムネイル読み込みエラー {index}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// アーカイブ内のファイルパス一覧（ツリー表示用）を返します。
    /// 返される順序はページインデックスと一致します。
    /// </summary>
    public Task<IReadOnlyList<string>> GetFileListAsync()
    {
        IReadOnlyList<string> list = _entries.Select(e => e.FullName).ToList();
        return Task.FromResult(list);
    }

    /// <summary>
    /// 使用中の ZipArchive を閉じ、内部リストをクリアします。
    /// </summary>
    public override void Dispose()
    {
        _archive.Dispose();
        _entries.Clear();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}