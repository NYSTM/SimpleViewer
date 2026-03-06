using SimpleViewer.Models.ImageSources;
using System.Security.Cryptography;
using System.Text;

namespace SimpleViewer.Services;

/// <summary>
/// キャッシュキーの生成を担当するクラス。
/// - ソースとインデックスからユニークなキーを生成します
/// - SHA256ハッシュを使用して衝突を回避します
/// - .NET 8 の静的メソッドを使用してパフォーマンスを最適化します
/// </summary>
public static class CacheKeyGenerator
{
    /// <summary>
    /// サムネイルキャッシュ用のキーを生成します。
    /// ソースの識別子（通常はファイルパス）とインデックスから一意なキーを生成します。
    /// .NET 8 の SHA256.HashData を使用してインスタンス生成のオーバーヘッドを削減します。
    /// </summary>
    /// <param name="source">画像ソース</param>
    /// <param name="index">ページ/エントリのインデックス</param>
    /// <returns>キャッシュ用の一意な文字列キー</returns>
    public static string MakeCacheKey(IImageSource source, int index)
    {
        // SourceIdentifierを使用して永続的なキーを生成
        var identifier = source.SourceIdentifier;
        var raw = $"{identifier}:{index}";
        
        // .NET 8 の静的メソッドを使用してインスタンス生成を回避
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
