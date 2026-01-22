using SimpleViewer.Models.ImageSources;
using System.Security.Cryptography;
using System.Text;

namespace SimpleViewer.Services;

/// <summary>
/// キャッシュキーの生成を担当するクラス。
/// - ソースとインデックスからユニークなキーを生成します
/// - SHA256ハッシュを使用して衝突を回避します
/// </summary>
public class CacheKeyGenerator
{
    /// <summary>
    /// サムネイルキャッシュ用のキーを生成します。
    /// ソースの識別子（通常はファイルパス）とインデックスから一意なキーを生成します。
    /// </summary>
    /// <param name="source">画像ソース</param>
    /// <param name="index">ページ/エントリのインデックス</param>
    /// <returns>キャッシュ用の一意な文字列キー</returns>
    public static string MakeCacheKey(IImageSource source, int index)
    {
        // SourceIdentifierを使用して永続的なキーを生成
        var identifier = source.SourceIdentifier;
        var raw = $"{identifier}:{index}";
        
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
