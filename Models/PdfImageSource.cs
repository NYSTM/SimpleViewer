using SimpleViewer.Models;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
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

    public Stream? GetPageStream(int index)
    {
        if (_pdfDoc == null || index < 0 || index >= _pdfDoc.PageCount) return null;

        return Task.Run(async () =>
        {
            using var page = _pdfDoc.GetPage((uint)index);
            var ms = new InMemoryRandomAccessStream();

            // PDFページを画像としてレンダリング
            await page.RenderToStreamAsync(ms);

            var netStream = ms.AsStreamForRead();
            var outStream = new MemoryStream();
            await netStream.CopyToAsync(outStream);
            outStream.Position = 0;
            return (Stream)outStream;
        }).GetAwaiter().GetResult();
    }

    public void Dispose() => _pdfDoc = null;
}