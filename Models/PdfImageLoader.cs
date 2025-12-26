using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace SimpleViewer.Models;

public class PdfImageLoader : IImageLoader
{
    private PdfDocument? _pdfDocument;
    public int TotalPages => _pdfDocument != null ? (int)_pdfDocument.PageCount : 0;

    public async Task InitializeAsync(string path)
    {
        Dispose();
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            _pdfDocument = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception ex)
        {
            throw new IOException("PDFの読み込みに失敗しました。", ex);
        }
    }

    /// <summary>
    /// メイン表示用の高精細レンダリング
    /// </summary>
    public async Task<BitmapSource?> LoadPageAsync(int index)
    {
        // 高精細にするため、元サイズの2倍程度でレンダリング
        return await RenderPdfPageInternalAsync(index, 2.0);
    }

    /// <summary>
    /// サイドバー用の高速サムネイルレンダリング
    /// </summary>
    /// <param name="width">指定された幅に合わせて倍率を自動計算</param>
    public async Task<BitmapSource?> LoadThumbnailAsync(int index, int width)
    {
        if (_pdfDocument == null || index < 0 || index >= TotalPages) return null;

        using var page = _pdfDocument.GetPage((uint)index);
        // ターゲットの幅(width)からレンダリング倍率(scale)を算出
        double scale = (double)width / page.Size.Width;

        return await RenderPdfPageInternalAsync(index, scale);
    }

    /// <summary>
    /// PDFページを指定した倍率で画像化する内部メソッド
    /// </summary>
    private async Task<BitmapSource?> RenderPdfPageInternalAsync(int index, double scale)
    {
        if (_pdfDocument == null || index < 0 || index >= TotalPages)
            return null;

        try
        {
            using var page = _pdfDocument.GetPage((uint)index);
            using var stream = new InMemoryRandomAccessStream();

            var options = new PdfPageRenderOptions
            {
                // 指定された倍率でレンダリングサイズを決定
                DestinationWidth = (uint)(page.Size.Width * scale),
                DestinationHeight = (uint)(page.Size.Height * scale)
            };

            await page.RenderToStreamAsync(stream, options);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream.AsStream();
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _pdfDocument = null;
    }
}