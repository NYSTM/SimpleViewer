using SimpleViewer.Models;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using System.Diagnostics;

namespace SimpleViewer.Models;

/// <summary>
/// Windows.Data.Pdf を使用して PDF ページを画像としてレンダリングするソース。
/// </summary>
/// <param name="pdfDoc">初期化済みの PdfDocument インスタンス</param>
public class PdfImageSource(PdfDocument pdfDoc) : ImageSourceBase, IImageSource
{
    private readonly PdfDocument _pdfDoc = pdfDoc;

    /// <summary>
    /// 静的ファクトリメソッド: StorageFile 経由で PDF を非同期に読み込みます。
    /// </summary>
    public static async Task<PdfImageSource> CreateAsync(string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var doc = await PdfDocument.LoadFromFileAsync(file);
        return new PdfImageSource(doc);
    }

    public Task<int> GetPageCountAsync() => Task.FromResult((int)_pdfDoc.PageCount);

    /// <summary>
    /// 指定されたインデックスの PDF ページを BitmapSource としてレンダリングします。
    /// </summary>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _pdfDoc.PageCount) return null;

        return await Task.Run(async () =>
        {
            try
            {
                using var page = _pdfDoc.GetPage((uint)index);

                // WinRT のストリームを作成
                using var ms = new InMemoryRandomAccessStream();

                // PDF ページをストリームへレンダリング
                // 必要に応じて PdfPageRenderOptions でレンダリングサイズ（DPI）を指定可能
                await page.RenderToStreamAsync(ms);

                // WinRT ストリームを .NET ストリームに変換してバイト配列として読み込み
                using var netStream = ms.AsStreamForRead();
                using var tempMs = new MemoryStream();
                await netStream.CopyToAsync(tempMs);

                // SkiaSharp を使用して高品質デコード
                var bitmap = SkiaImageLoader.LoadImage(tempMs.ToArray());

                // 重要: UIスレッド以外で作成した Bitmap を共有可能にする
                bitmap?.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PdfImageSource] Render Error at page {index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// サムネイル取得。PDF の場合は描画プロセスが同じため GetPageImageAsync を流用。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        // 高速化が必要な場合は、RenderToStreamAsync のオプションで 
        // 描画サイズ自体を小さく指定する実装への変更を検討してください。
        return await GetPageImageAsync(index);
    }

    /// <summary>
    /// ImageSourceBase.Dispose をオーバーライドしてリソースを解放します。
    /// </summary>
    public override void Dispose()
    {
        // PdfDocument 自体は IDisposable ではないが、
        // 内部で保持しているリソースのクリーンアップが必要な場合に備え、
        // 規定の Dispose パターンに従います。
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}