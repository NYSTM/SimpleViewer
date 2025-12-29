using System.Diagnostics;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

/// <summary>
/// ZIP アーカイブ内の画像エントリを読み出して BitmapSource として提供する画像ソース実装。
/// アーカイブ内の画像はエントリ順（アルファベット順）でページインデックスが割り当てられます。
/// </summary>
public class ArchiveImageSource : ImageSourceBase, IImageSource
{
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _entries;

    // ZipArchive はスレッドセーフではないため、エントリ読み出し時にロックを行う
    private readonly object _zipLock = new();

    /// <summary>
    /// 指定した ZIP ファイルを開き、画像エントリ一覧を準備します。
    /// </summary>
    /// <param name="zipPath">ZIP ファイルのパス</param>
    public ArchiveImageSource(string zipPath)
    {
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
    /// エントリのストリームを直接 Skia に渡し、不要なメモリコピーを避けます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                // ZipArchive の内部ストリームは同時アクセスが不安定なため排他で扱う
                lock (_zipLock)
                {
                    // ZipArchiveEntry.Open() はストリームを返すので using で確実に閉じる
                    using var entryStream = _entries[index].Open();

                    // SkiaImageLoader にストリームを渡してデコードする
                    var bitmap = SkiaImageLoader.LoadImage(entryStream);

                    // UI スレッド以外で作成された BitmapSource は Freeze して共有可能にする
                    bitmap?.Freeze();

                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                // エラーはログに出すが UI 操作には例外を投げない
                Debug.WriteLine($"[ArchiveImageSource] アーカイブ読み込みエラー {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// 指定インデックスのサムネイル（リサイズ済み）を取得します。
    /// Skia 側でデコード時にリサイズを行うことでメモリ消費を抑えます。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">ターゲット幅（ピクセル）</param>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _entries.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                lock (_zipLock)
                {
                    using var entryStream = _entries[index].Open();
                    var thumb = SkiaImageLoader.LoadThumbnail(entryStream, width);
                    thumb?.Freeze();
                    return thumb;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ArchiveImageSource] サムネイル読み込みエラー {index}: {ex.Message}");
                return null;
            }
        });
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