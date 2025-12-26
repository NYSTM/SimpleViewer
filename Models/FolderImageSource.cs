using System.IO;

namespace SimpleViewer.Models;

public class FolderImageSource : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="folderPath">スキャン対象のフォルダパス</param>
    public FolderImageSource(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _filePaths = new List<string>();
            return;
        }

        // ImageSourceBase で定義された SupportedExtensions を使用してファイルをフィルタリング
        _filePaths = Directory.GetFiles(folderPath)
            .Where(path => IsImageFile(path))
            .OrderBy(path => path, new NaturalStringComparer()) // 自然順ソートを適用
            .ToList();
    }

    /// <summary>
    /// 総画像数を取得
    /// </summary>
    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    /// <summary>
    /// 指定インデックスの画像をファイルストリームとして開く
    /// </summary>
    public Stream? GetPageStream(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;

        try
        {
            // FileShare.Read を指定することで、他のビューアなどで開いていても読み込み可能にする
            return new FileStream(_filePaths[index], FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // フォルダ読み込みの場合は保持しているハンドルがないため、処理なし
    }
}

/// <summary>
/// ファイル名を人間が期待する順序（1.jpg -> 2.jpg -> 10.jpg）で並べるための比較器
/// </summary>
public class NaturalStringComparer : IComparer<string>
{
    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y)
    {
        return StrCmpLogicalW(x ?? "", y ?? "");
    }
}