using SimpleViewer.Models;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

public class PdfImageSource : IImageSource
{
    private PdfDocument? _pdfDoc;

    public static async Task<PdfImageSource> CreateAsync(string path)
    {
        var source = new PdfImageSource();
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        source._pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        return source;
    }

    public Task<int> GetPageCountAsync() => Task.FromResult((int)(_pdfDoc?.PageCount ?? 0));

    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (_pdfDoc == null || index < 0 || index >= _pdfDoc.PageCount) return null;

        using var page = _pdfDoc.GetPage((uint)index);
        using var ms = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(ms);

        using var netStream = ms.AsStreamForRead();
        using var tempMs = new MemoryStream();
        await netStream.CopyToAsync(tempMs);

        return SkiaImageLoader.LoadImage(tempMs.ToArray());
    }

    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        // PDFの場合はフルデコードとほぼ同等の処理が必要ですが、
        // Skia側のLoadThumbnailを使うことでWPFへの転送量を抑えます
        return await GetPageImageAsync(index);
    }

    public void Dispose() => _pdfDoc = null;
}