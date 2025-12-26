using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

public class LocalImageLoader : IImageLoader
{
    private List<string> _filePaths = new();
    private readonly string[] _supportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    public int TotalPages => _filePaths.Count;

    /// <summary>
    /// フォルダ内の画像をスキャンし、自然順（エクスプローラー順）でソートして保持する
    /// </summary>
    public Task InitializeAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _filePaths = new List<string>();
            return Task.CompletedTask;
        }

        // サポートされている画像ファイルを列挙
        var files = Directory.GetFiles(folderPath)
            .Where(file => _supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
            .ToList();

        // 自然順ソート (1, 2, 10...)
        files.Sort(new NaturalStringComparer());

        _filePaths = files;
        return Task.CompletedTask;
    }

    /// <summary>
    /// ファイル名から、リスト内でのインデックスを特定する
    /// </summary>
    public int GetIndexByFileName(string fileName)
    {
        return _filePaths.FindIndex(path =>
            Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
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
    /// 画像読み込みの共通内部メソッド
    /// </summary>
    private async Task<BitmapSource?> LoadImageInternalAsync(int index, int decodeWidth)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        return await Task.Run(() =>
        {
            try
            {
                var path = _filePaths[index];
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);

                // 【重要】デコードサイズを制限（メモリ節約と高速化）
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }

                // ファイルロックを避けるためにキャッシュをメモリに作成
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze(); // UIスレッドで利用可能にする
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
        _filePaths.Clear();
    }
}

/// <summary>
/// 文字列を「自然順 (1, 2, 10...)」で比較するためのヘルパークラス
/// </summary>
public class NaturalStringComparer : IComparer<string>
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        return StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
    }
}