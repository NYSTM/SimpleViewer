using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace SimpleViewer.Models;

/// <summary>
/// Windows.Data.Pdf を使って PDF の各ページを BitmapSource としてレンダリングする実装。
/// WinRT の PdfDocument を内部に保持し、ページごとにレンダリングして Skia 経由でデコードします。
/// </summary>
/// <param name="pdfDoc">初期化済みの PdfDocument インスタンス</param>
public class PdfImageSource(PdfDocument pdfDoc) : ImageSourceBase, IImageSource
{
    private readonly PdfDocument _pdfDoc = pdfDoc;

    /// <summary>
    /// 指定パスから StorageFile を経由して PdfDocument をロードし、PdfImageSource を生成します。
    /// WinRT の API を扱うため非同期で初期化されます。
    /// </summary>
    public static async Task<PdfImageSource> CreateAsync(string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var doc = await PdfDocument.LoadFromFileAsync(file);
        return new PdfImageSource(doc);
    }

    /// <summary>
    /// PDF のページ数を返します。
    /// </summary>
    public Task<int> GetPageCountAsync() => Task.FromResult((int)_pdfDoc.PageCount);

    /// <summary>
    /// 指定インデックスの PDF ページをレンダリングして BitmapSource を返します。
    /// 実際のレンダリングはバックグラウンドスレッドで行われ、UI スレッドをブロックしない設計です。
    /// </summary>
    /// <remarks>
    /// 処理フロー:
    /// 1) PdfPage.RenderToStreamAsync で WinRT の IRandomAccessStream に描画
    /// 2) IRandomAccessStream を .NET の Stream に変換（AsStreamForRead）
    /// 3) SkiaImageLoader によりストリームからデコード
    /// 4) UI スレッドで安全に共有するため BitmapSource を Freeze
    /// </remarks>
    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _pdfDoc.PageCount) return null;

        // レンダリングは重い処理なのでバックグラウンドで実行する
        return await Task.Run(async () =>
        {
            try
            {
                // PdfPage は IDisposable なので using で確実に破棄
                using var page = _pdfDoc.GetPage((uint)index);

                // WinRT の InMemoryRandomAccessStream を作成して描画結果を受け取る
                using var ms = new InMemoryRandomAccessStream();

                // PDF ページをストリームへレンダリング（非同期）
                await page.RenderToStreamAsync(ms);

                // ストリームを先頭に戻す
                ms.Seek(0);

                // WinRT ストリームを .NET の Stream に変換して Skia に渡す
                // 変換後のストリームは読み取り専用ストリームとして扱う
                using var netStream = ms.AsStreamForRead();

                // Skia に直接ストリームを渡してデコードする（不要な ToArray を避ける）
                var bitmap = SkiaImageLoader.LoadImage(netStream);

                // UI スレッド以外で作成した BitmapSource は Freeze して共有可能にする
                bitmap?.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                // ログに出力して null を返す設計（UI でエラーハンドリングする）
                Debug.WriteLine($"[PdfImageSource] レンダリングエラー page={index}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// サムネイル取得。現状は GetPageImageAsync を流用していますが、
    /// RenderToStreamAsync の描画設定を変更して縮小レンダリングするなどの最適化が可能です。
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        return await GetPageImageAsync(index);
    }

    /// <summary>
    /// ツリー表示用にページラベル一覧を返します（例: "Page 001"）。
    /// </summary>
    public Task<IReadOnlyList<string>> GetFileListAsync()
    {
        int pages = (int)_pdfDoc.PageCount;
        int digits = Math.Max(1, pages.ToString().Length);
        IReadOnlyList<string> list = Enumerable.Range(1, pages).Select(i => $"Page {i.ToString().PadLeft(digits, '0')}").ToList();
        return Task.FromResult(list);
    }

    /// <summary>
    /// 必要に応じてリソースを解放します。
    /// PdfDocument 自体は IDisposable ではないため、ここでは基底の Dispose を呼び出すのみです。
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}