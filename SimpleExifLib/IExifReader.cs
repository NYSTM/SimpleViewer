namespace SimpleExifLib
{
    /// <summary>
    /// EXIFリーダーのインターフェイス。
    /// 実装はストリームから EXIF 情報を非同期に読み取る機能を提供する。
    /// 注: 現在の実装では <see cref="JpegExifReader"/> は静的クラスとして実装されており、
    /// このインターフェースは将来の拡張（PNG、TIFF等）のために保持されています。
    /// </summary>
    public interface IExifReader
    {
        /// <summary>
        /// ストリームから EXIF データを読み取る。
        /// 実装はストリームの先頭を読み取る必要があるため、呼び出し元で適切な位置に設定されていること。
        /// </summary>
        /// <param name="stream">画像データを含むシーク可能なストリーム。</param>
        /// <returns>パースに成功した場合は <see cref="ExifData"/> を返し、失敗または EXIF が存在しない場合は null を返す。</returns>
        Task<ExifData?> ReadAsync(Stream stream);
    }
}
