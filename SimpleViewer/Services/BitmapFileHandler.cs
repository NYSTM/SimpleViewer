using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Models;
using System.Windows.Media.Imaging;
using System.IO;

namespace SimpleViewer.Services;

/// <summary>
/// ビットマップのファイル入出力を担当するクラス。
/// - ビットマップのロードと保存を行います
/// - デコーダの抽象化をサポートします
/// </summary>
public class BitmapFileHandler
{
    private readonly IImageDecoder? _imageDecoder;

    /// <summary>
    /// BitmapFileHandlerを初期化します。
    /// </summary>
    /// <param name="imageDecoder">オプションのイメージデコーダ</param>
    public BitmapFileHandler(IImageDecoder? imageDecoder = null)
    {
        _imageDecoder = imageDecoder;
    }

    /// <summary>
    /// 指定パスからBitmapSourceを読み込みます。
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>読み込まれたBitmapSource、失敗時はnull</returns>
    public async Task<BitmapSource?> LoadBitmapFromFileAsync(string path, CancellationToken ct)
    {
        try
        {
            if (_imageDecoder != null)
            {
                // IImageDecoderを使用して非同期読み込み
                using var fs = File.OpenRead(path);
                return await _imageDecoder.LoadImageAsync(fs, ct).ConfigureAwait(false);
            }
            else
            {
                // フォールバック: WPFのデコーダを使用
                return await LoadWithWpfDecoderAsync(path, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    /// <summary>
    /// BitmapSourceをPNGファイルとして保存します。
    /// </summary>
    /// <param name="bmp">保存するビットマップ</param>
    /// <param name="path">保存先のファイルパス</param>
    public void SaveBitmapToFile(BitmapSource bmp, string path)
    {
        try
        {
            // ディレクトリが存在することを確認
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 一時ファイルへ書き出してから置換（アトミック操作）
            var temp = path + ".tmp";
            using (var fs = File.OpenWrite(temp))
            {
                fs.SetLength(0);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BitmapFileHandler] ファイル保存エラー: {path}, {ex.Message}");
            // 一時ファイルが残っていたら削除を試みる
            try
            {
                var temp = path + ".tmp";
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch { /* 無視 */ }
        }
    }

    /// <summary>
    /// WPFのPngBitmapDecoderを使用してビットマップを読み込みます。
    /// </summary>
    /// <param name="path">ファイルパス</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>読み込まれたBitmapSource、失敗時はnull</returns>
    private static async Task<BitmapSource?> LoadWithWpfDecoderAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var fs = File.OpenRead(path);
                var decoder = new PngBitmapDecoder(
                    fs,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.FirstOrDefault();
                return frame;
            }
            catch
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
    }
}
