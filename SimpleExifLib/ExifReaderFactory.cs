namespace SimpleExifLib
{
    /// <summary>
    /// 画像ストリームから適切な <see cref="IExifReader"/> を選択して EXIF を読み取る簡易ファクトリ。
    /// 現在は JPEG のみサポートしている。
    /// </summary>
    public static class ExifReaderFactory
    {
        /// <summary>
        /// ストリームから EXIF を読み取る。内部で適切なリーダーを選択して実行する。
        /// </summary>
        /// <param name="stream">画像データを含むシーク可能なストリーム。</param>
        /// <returns>解析した <see cref="ExifData"/>。見つからない場合は null。</returns>
        public static async Task<ExifData?> ReadAsync(Stream stream)
        {
            if (stream == null) return null;
            // シンプル実装: JPEGのみサポート
            return await JpegExifReader.ReadAsync(stream).ConfigureAwait(false);
        }

        /// <summary>
        /// ファイルパスから EXIF を非同期に読み取る便利メソッド。
        /// ファイルが存在しない場合は null を返します。
        /// </summary>
        public static async Task<ExifData?> ReadAsync(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) return null;

            using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await ReadAsync(fs).ConfigureAwait(false);
        }

        /// <summary>
        /// ストリームから EXIF を同期的に読み取る。
        /// 注: 内部的には非同期メソッドを同期実行しています。
        /// </summary>
        /// <param name="stream">画像データを含むシーク可能なストリーム。</param>
        /// <returns>解析した <see cref="ExifData"/>。見つからない場合は null。</returns>
        public static ExifData? Read(Stream stream)
        {
            if (stream == null) return null;
            return ReadAsync(stream).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ファイルパスから EXIF を同期的に読み取る便利メソッド。
        /// </summary>
        public static ExifData? Read(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) return null;

            // JpegExifReader には同期版があるのでそれを利用
            return JpegExifReader.Read(filePath);
        }

        // --- Orientation convenience APIs ---

        /// <summary>
        /// ストリームから EXIF の Orientation 値を非同期に取得します。
        /// ストリームの位置は呼び出し前の位置に復元されます（可能な場合）。
        /// </summary>
        public static async Task<int> ReadOrientationAsync(Stream stream)
        {
            if (stream == null) return 1;

            long originalPosition = stream.CanSeek ? stream.Position : 0;
            try
            {
                if (stream.CanSeek) stream.Position = 0;
                var exif = await ReadAsync(stream).ConfigureAwait(false);
                if (exif?.Orientation != null) return exif.Orientation.Value;
            }
            catch
            {
                // ignore
            }
            finally
            {
                if (stream.CanSeek)
                {
                    try { stream.Position = originalPosition; } catch { }
                }
            }

            return 1;
        }

        /// <summary>
        /// ファイルパスから EXIF の Orientation 值を非同期に取得します。
        /// </summary>
        public static async Task<int> ReadOrientationAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 1;

            await using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await ReadOrientationAsync(fs).ConfigureAwait(false);
        }

        /// <summary>
        /// ストリームから EXIF の Orientation 値を同期的に取得します。
        /// </summary>
        public static int ReadOrientation(Stream stream)
        {
            if (stream == null) return 1;
            return ReadOrientationAsync(stream).GetAwaiter().GetResult();
        }

        /// <summary>
        /// ファイルパスから EXIF の Orientation 値を同期的に取得します。
        /// </summary>
        public static int ReadOrientation(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return 1;
            return ReadOrientationAsync(filePath).GetAwaiter().GetResult();
        }
    }
}
