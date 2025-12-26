using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

public class ZipImageLoader : IImageLoader
{
    private ZipArchive? _archive;
    private List<ZipArchiveEntry> _entries = new();
    private readonly string[] _supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    public int TotalPages => _entries.Count;

    /// <summary>
    /// ZIPファイルを開き、中の画像エントリを自然順でリストアップする
    /// </summary>
    public async Task InitializeAsync(string path)
    {
        Dispose();

        await Task.Run(() =>
        {
            try
            {
                // ファイルを共有モードで開く（他アプリによる読み取りを許可）
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                _archive = new ZipArchive(stream, ZipArchiveMode.Read);

                // サポートされている画像ファイルを抽出
                _entries = _archive.Entries
                    .Where(e => _supportedExtensions.Contains(Path.GetExtension(e.FullName).ToLower()))
                    .ToList();

                // ファイル名で自然順ソート（1, 2, 10...）
                var comparer = new NaturalStringComparer();
                _entries.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));
            }
            catch (Exception ex)
            {
                throw new IOException("ZIPファイルの読み込みに失敗しました。", ex);
            }
        });
    }

    /// <summary>
    /// メイン表示用のフルサイズ画像を読み込む
    /// </summary>
    public async Task<BitmapSource?> LoadPageAsync(int index)
    {
        return await LoadImageInternalAsync(index, 0); // width=0 は制限なし
    }

    /// <summary>
    /// サイドバー用の高速サムネイル画像を読み込む
    /// </summary>
    public async Task<BitmapSource?> LoadThumbnailAsync(int index, int width)
    {
        return await LoadImageInternalAsync(index, width);
    }

    /// <summary>
    /// ZIP内の画像をデコードする共通内部メソッド
    /// </summary>
    private async Task<BitmapSource?> LoadImageInternalAsync(int index, int decodeWidth)
    {
        if (_archive == null || index < 0 || index >= _entries.Count)
            return null;

        return await Task.Run(() =>
        {
            try
            {
                var entry = _entries[index];
                using var entryStream = entry.Open();

                // メモリへ一旦コピー（Zipのストリームを直接BitmapImageに渡すと不安定になるため）
                var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();

                // 【重要】サムネイルの場合はデコードサイズを制限する
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }

                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze(); // UIスレッドへ渡せるように固定

                return (BitmapSource)bitmap;
            }
            catch
            {
                return null;
            }
        });
    }

    public void Dispose()
    {
        _archive?.Dispose();
        _archive = null;
        _entries.Clear();
    }
}