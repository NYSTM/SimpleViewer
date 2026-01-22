using System.Globalization;
using System.Text;

namespace SimpleExifLib
{
    /// <summary>
    /// EXIF データの解析を担当する内部クラス。
    /// TIFF/IFD パースと文字列検索フォールバックの両方を提供する。
    /// </summary>
    internal static class ExifParser
    {
        /// <summary>
        /// APP1 セグメントから EXIF データをパースする。
        /// TIFF ヘッダーのエンディアン判定後、IFD0 と Exif Sub IFD を順次解析する。
        /// </summary>
        /// <param name="data">APP1 セグメントの TIFF データ部分（EXIF ヘッダー除く）。</param>
        /// <param name="settings">読み取る EXIF フィールドを制御する設定。</param>
        /// <returns>パースに成功した場合は <see cref="ExifData"/>, 失敗した場合は null。</returns>
        public static ExifData? ParseFromApp1(byte[] data, ExifSettings settings)
        {
            if (data == null || data.Length < 8) return null;

            try
            {
                // TIFF ヘッダーのエンディアン判定
                if (!TryGetEndianness(data, out bool isLittleEndian)) return null;
                
                // TIFF マジックナンバー (0x002A) の確認
                ushort magic = TiffBinaryReader.ReadUInt16(data, 2, isLittleEndian);
                if (magic != 0x002A) return null;

                // IFD0 のオフセット取得
                uint ifd0Offset = TiffBinaryReader.ReadUInt32(data, 4, isLittleEndian);
                if (ifd0Offset >= data.Length) return null;

                var exif = new ExifData();
                
                // IFD0 をパースし、Exif Sub IFD のオフセットを取得
                uint exifSubIfdOffset = ParseIfd0(data, ifd0Offset, isLittleEndian, settings, exif);

                // Exif Sub IFD が存在する場合はパース
                if (exifSubIfdOffset != 0 && exifSubIfdOffset < data.Length)
                {
                    ParseExifSubIfd(data, exifSubIfdOffset, isLittleEndian, settings, exif);
                }

                return exif;
            }
            catch { return null; }
        }

        /// <summary>
        /// TIFF パースに失敗した場合のフォールバック処理。
        /// バイト配列を UTF-8 文字列として解釈し、タグ名を検索してフィールド値を抽出する。
        /// </summary>
        /// <param name="data">検索対象のバイト配列。</param>
        /// <param name="settings">読み取る EXIF フィールドを制御する設定。</param>
        /// <returns>抽出できた EXIF データ、何も見つからない場合は空の <see cref="ExifData"/>。</returns>
        public static ExifData? ParseAsciiFallback(byte[] data, ExifSettings settings)
        {
            if (data == null || data.Length == 0) return null;
            
            var exif = new ExifData();
            // フォールバックでは UTF-8 としてデコードする（非 ASCII 文字を扱えるようにする）
            var s = Encoding.UTF8.GetString(data);
            
            // 各フィールドを 文字列検索で取得
            if (settings.ReadCameraMake) 
                exif.CameraMake = FindAsciiTagValue(s, "Make");
            
            if (settings.ReadCameraModel) 
                exif.CameraModel = FindAsciiTagValue(s, "Model");
            
            if (settings.ReadDateTimeOriginal)
            {
                var dateStr = FindAsciiTagValue(s, "DateTimeOriginal");
                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    exif.DateTimeOriginal = dt;
            }
            
            if (settings.ReadOrientation)
            {
                var orientationStr = FindAsciiTagValue(s, "Orientation");
                if (int.TryParse(orientationStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var or))
                    exif.Orientation = or;
            }
            
            return exif;
        }

        /// <summary>
        /// 追加タグ ID リストに基づいて、EXIF データに追加フィールドを設定する。
        /// IFD0 および Exif Sub IFD を再帰的に走査して指定されたタグを収集する。
        /// </summary>
        /// <param name="exif">追加タグを設定する <see cref="ExifData"/> オブジェクト。</param>
        /// <param name="app1Data">APP1 セグメントの TIFF データ部分。</param>
        /// <param name="tags">収集するタグ ID のリスト。</param>
        public static void PopulateAdditionalTags(ExifData exif, byte[] app1Data, System.Collections.Generic.IList<int> tags)
        {
            if (tags == null || tags.Count == 0) return;

            try
            {
                // エンディアン判定
                if (!TryGetEndianness(app1Data, out bool isLittleEndian)) return;
                
                // IFD0 のオフセット取得
                var ifd0Offset = TiffBinaryReader.ReadUInt32(app1Data, 4, isLittleEndian);
                if (ifd0Offset >= app1Data.Length) return;

                // IFD0 から追加タグを収集
                IfdProcessor.CollectTagsRecursive(app1Data, ifd0Offset, isLittleEndian, tags, exif);

                // Exif Sub IFD のオフセットを検索
                uint exifSubIfdOffset = FindExifSubIfdOffset(app1Data, ifd0Offset, isLittleEndian);
                if (exifSubIfdOffset != 0 && exifSubIfdOffset < app1Data.Length)
                {
                    // Exif Sub IFD から追加タグを収集
                    IfdProcessor.CollectTagsRecursive(app1Data, exifSubIfdOffset, isLittleEndian, tags, exif);
                }
            }
            catch { }
        }

        /// <summary>
        /// IFD0 (Image File Directory 0) をパースして基本的な EXIF フィールドを取得する。
        /// カメラメーカー、モデル、向き、幅、高さなどの情報を抽出する。
        /// </summary>
        /// <param name="data">TIFF データ。</param>
        /// <param name="ifd0Offset">IFD0 の開始オフセット。</param>
        /// <param name="isLittleEndian">リトルエンディアンの場合 true。</param>
        /// <param name="settings">読み取る EXIF フィールドを制御する設定。</param>
        /// <param name="exif">パース結果を格納する <see cref="ExifData"/> オブジェクト。</param>
        /// <returns>Exif Sub IFD のオフセット。存在しない場合は 0。</returns>
        private static uint ParseIfd0(byte[] data, uint ifd0Offset, bool isLittleEndian, ExifSettings settings, ExifData exif)
        {
            uint exifSubIfdOffset = 0;
            
            IfdProcessor.ForEachEntry(data, ifd0Offset, isLittleEndian, (entryBase, tag, type, count, valueOrOffset) =>
            {
                uint valueOffset = GetValueOffset(entryBase, type, count, valueOrOffset);

                switch (tag)
                {
                    case 0x010F when settings.ReadCameraMake: // Make タグ
                        exif.CameraMake = TiffBinaryReader.ReadAsciiValue(data, valueOffset, count);
                        break;
                    case 0x0110 when settings.ReadCameraModel: // Model タグ
                        exif.CameraModel = TiffBinaryReader.ReadAsciiValue(data, valueOffset, count);
                        break;
                    case 0x0112 when settings.ReadOrientation: // Orientation タグ
                        exif.Orientation = (int)TiffBinaryReader.ReadUInt16(data, (int)valueOffset, isLittleEndian);
                        break;
                    case 0x0100 when settings.ReadWidth: // ImageWidth タグ
                        exif.Width = (int)TiffBinaryReader.ReadUInt32(data, (int)valueOffset, isLittleEndian);
                        break;
                    case 0x0101 when settings.ReadHeight: // ImageHeight タグ
                        exif.Height = (int)TiffBinaryReader.ReadUInt32(data, (int)valueOffset, isLittleEndian);
                        break;
                    case 0x8769: // ExifIFDPointer タグ
                        exifSubIfdOffset = valueOrOffset;
                        break;
                }
            });
            
            return exifSubIfdOffset;
        }

        /// <summary>
        /// Exif Sub IFD をパースして詳細な撮影情報を取得する。
        /// 撮影日時、露出時間、F値、ISO感度、焦点距離などの情報を抽出する。
        /// </summary>
        /// <param name="data">TIFF データ。</param>
        /// <param name="offset">Exif Sub IFD の開始オフセット。</param>
        /// <param name="isLittleEndian">リトルエンディアンの場合 true。</param>
        /// <param name="settings">読み取る EXIF フィールドを制御する設定。</param>
        /// <param name="exif">パース結果を格納する <see cref="ExifData"/> オブジェクト。</param>
        private static void ParseExifSubIfd(byte[] data, uint offset, bool isLittleEndian, ExifSettings settings, ExifData exif)
        {
            IfdProcessor.ForEachEntry(data, offset, isLittleEndian, (entryBase, tag, type, count, valueOrOffset) =>
            {
                uint valueOffset = GetValueOffset(entryBase, type, count, valueOrOffset);

                switch (tag)
                {
                    case 0x9003 when settings.ReadDateTimeOriginal: // DateTimeOriginal タグ
                        var dtStr = TiffBinaryReader.ReadAsciiValue(data, valueOffset, count);
                        if (DateTime.TryParse(dtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                            exif.DateTimeOriginal = dt;
                        break;
                    case 0x829A when settings.ReadExposureTime: // ExposureTime タグ
                        exif.ExposureTime = TiffBinaryReader.ReadRational(data, valueOffset, isLittleEndian);
                        break;
                    case 0x829D when settings.ReadFNumber: // FNumber タグ
                        exif.FNumber = TiffBinaryReader.ReadRational(data, valueOffset, isLittleEndian);
                        break;
                    case 0x8827 when settings.ReadISOSpeed: // ISOSpeedRatings タグ
                        exif.ISOSpeed = (int)TiffBinaryReader.ReadUInt16(data, (int)valueOffset, isLittleEndian);
                        break;
                    case 0x920A when settings.ReadFocalLength: // FocalLength タグ
                        exif.FocalLength = TiffBinaryReader.ReadRational(data, valueOffset, isLittleEndian);
                        break;
                }
            });
        }

        /// <summary>
        /// TIFF ヘッダーからエンディアン（バイトオーダー）を判定する。
        /// "II" (0x4949) の場合はリトルエンディアン、"MM" (0x4D4D) の場合はビッグエンディアン。
        /// </summary>
        /// <param name="data">TIFF データ（最低2バイト必要）。</param>
        /// <param name="isLittleEndian">リトルエンディアンの場合 true、ビッグエンディアンの場合 false。</param>
        /// <returns>エンディアン判定に成功した場合 true、不正なヘッダーの場合 false。</returns>
        private static bool TryGetEndianness(byte[] data, out bool isLittleEndian)
        {
            isLittleEndian = false;
            if (data[0] == (byte)'I' && data[1] == (byte)'I') 
                isLittleEndian = true;
            else if (data[0] == (byte)'M' && data[1] == (byte)'M') 
                isLittleEndian = false;
            else 
                return false;
            return true;
        }

        /// <summary>
        /// IFD 内から Exif Sub IFD のオフセット (タグ 0x8769) を検索する。
        /// </summary>
        /// <param name="data">TIFF データ。</param>
        /// <param name="ifdOffset">IFD の開始オフセット。</param>
        /// <param name="isLittleEndian">リトルエンディアンの場合 true。</param>
        /// <returns>Exif Sub IFD のオフセット。見つからない場合は 0。</returns>
        private static uint FindExifSubIfdOffset(byte[] data, uint ifdOffset, bool isLittleEndian)
        {
            uint result = 0;
            IfdProcessor.ForEachEntry(data, ifdOffset, isLittleEndian, (entryBase, tag, type, count, valueOrOffset) =>
            {
                if (tag == 0x8769) result = valueOrOffset;
            });
            return result;
        }

        /// <summary>
        /// IFD エントリの値またはオフセットから実際の値の位置を計算する。
        /// データサイズが 4 バイト以下の場合はエントリ内に直接格納され、
        /// 4 バイトを超える場合は別の位置へのオフセットが格納される。
        /// </summary>
        /// <param name="entryBase">IFD エントリの開始オフセット。</param>
        /// <param name="type">TIFF データ型。</param>
        /// <param name="count">値の個数。</param>
        /// <param name="valueOrOffset">値または値へのオフセット。</param>
        /// <returns>実際の値が格納されている位置のオフセット。</returns>
        private static uint GetValueOffset(uint entryBase, ushort type, uint count, uint valueOrOffset)
        {
            // データサイズ = タイプの単位長 × 個数
            return TiffBinaryReader.GetTypeUnitLength(type) * count <= 4 
                ? entryBase + 8  // エントリ内に直接格納（オフセット +8 の位置）
                : valueOrOffset; // 別の位置へのオフセット
        }

        /// <summary>
        /// ASCII 文字列内からタグ名を検索し、その値を抽出する。
        /// フォールバック処理で使用され、タグ名の後ろに続く値を取得する。
        /// </summary>
        /// <param name="input">検索対象の ASCII 文字列。</param>
        /// <param name="tag">検索するタグ名（例: "Make", "Model"）。</param>
        /// <returns>見つかった値の文字列。見つからない場合は null。</returns>
        private static string? FindAsciiTagValue(string input, string tag)
        {
            // タグ名を大文字小文字を区別せずに検索
            var idx = input.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            
            // タグ名の後ろの空白文字やコロンをスキップ
            var start = idx + tag.Length;
            while (start < input.Length && (input[start] == '\0' || input[start] == ' ' || input[start] == ':')) 
                start++;
            if (start >= input.Length) return null;
            
            // 値の終端（null文字、改行文字）まで読み取り
            var end = start;
            while (end < input.Length && input[end] != '\0' && input[end] != '\r' && input[end] != '\n') 
                end++;
            
            return input.Substring(start, end - start).Trim();
        }
    }
}
