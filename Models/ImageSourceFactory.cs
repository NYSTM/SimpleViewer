using System.IO;

namespace SimpleViewer.Models;

/// <summary>
/// 指定されたパスに応じて適切な IImageSource 実装を生成する静的ファクトリ。
/// フォルダ・ZIP/CBZ・PDF・単一画像ファイルなどに対応し、非同期初期化が必要なものは
/// 非同期メソッドを用いて生成します。
/// </summary>
public static class ImageSourceFactory
{
    /// <summary>
    /// 指定パスから適切な画像ソースを生成して返します。
    /// 例:
    /// - フォルダパス -> FolderImageSource
    /// - .zip/.cbz -> ArchiveImageSource
    /// - .pdf -> PdfImageSource (非同期生成)
    /// - 単一画像ファイル -> その親フォルダを FolderImageSource として開く（既存の UI 想定）
    /// サポート外の拡張子の場合は NotSupportedException を投げます。
    /// </summary>
    /// <param name="path">ファイルまたはフォルダのパス</param>
    /// <returns>IImageSource のインスタンス（必要なら非同期で初期化されたもの）</returns>
    public static async Task<IImageSource> CreateSourceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        // 1. フォルダ判定: フォルダが存在すればフォルダソースを返す
        if (Directory.Exists(path)) return new FolderImageSource(path);

        // 2. 拡張子に基づいて分岐
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext switch
        {
            // アーカイブ（CBZ など）は ArchiveImageSource で処理
            ".zip" or ".cbz" => new ArchiveImageSource(path),

            // PDF は非同期生成を行う（WinRT API を利用するため）
            ".pdf" => await PdfImageSource.CreateAsync(path),

            // 3. 単一画像ファイルの場合: 使い勝手のため親フォルダを開く実装にする
            //    （アプリの UI はフォルダ単位でツリーを構築する想定のため）
            _ when ImageSourceBase.IsStaticImageFile(path) =>
                new FolderImageSource(Path.GetDirectoryName(path) ?? throw new InvalidOperationException()),

            // サポート外
            _ => throw new NotSupportedException($"未対応のフォーマットです: {ext}")
        };
    }
}