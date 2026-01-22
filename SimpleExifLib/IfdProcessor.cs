using System.Globalization;

namespace SimpleExifLib
{
    /// <summary>
    /// TIFF IFD（Image File Directory）の処理を担当するクラス。
    /// IFD のエントリ列挙、タグ収集、NextIFD チェーンのトラバースを行う。
    /// </summary>
    internal static class IfdProcessor
    {
        /// <summary>
        /// 指定された IFD のエントリを列挙してコールバックを呼び出す。
        /// </summary>
        /// <param name="span">TIFF データ全体のバイト配列。</param>
        /// <param name="ifdOffset">IFD の開始オフセット。</param>
        /// <param name="isLittleEndian">リトルエンディアンかどうか。</param>
        /// <param name="onEntry">各エントリごとに呼び出されるコールバック（entryBase, tag, type, count, valueOrOffset）。</param>
        public static void ForEachEntry(byte[] span, uint ifdOffset, bool isLittleEndian, Action<uint, ushort, ushort, uint, uint> onEntry)
        {
            if (ifdOffset + 2 > span.Length) return;
            var entryCount = TiffBinaryReader.ReadUInt16(span, (int)ifdOffset, isLittleEndian);
            var offset = ifdOffset + 2;
            
            for (int i = 0; i < entryCount; i++)
            {
                var entryBase = offset + (uint)(i * 12);
                if (entryBase + 12 > span.Length) break;
                
                var tag = TiffBinaryReader.ReadUInt16(span, (int)entryBase, isLittleEndian);
                var type = TiffBinaryReader.ReadUInt16(span, (int)(entryBase + 2), isLittleEndian);
                var count = TiffBinaryReader.ReadUInt32(span, (int)(entryBase + 4), isLittleEndian);
                var valueOrOffset = TiffBinaryReader.ReadUInt32(span, (int)(entryBase + 8), isLittleEndian);

                onEntry?.Invoke(entryBase, tag, type, count, valueOrOffset);
            }
        }

        /// <summary>
        /// 指定 IFD の直後に配置される NextIFD オフセットを取得する。
        /// </summary>
        public static uint GetNextIfdOffset(byte[] span, uint ifdOffset, bool isLittleEndian)
        {
            if (ifdOffset + 2 > span.Length) return 0;
            var entryCount = TiffBinaryReader.ReadUInt16(span, (int)ifdOffset, isLittleEndian);
            var nextIfdPos = ifdOffset + 2 + (uint)(entryCount * 12);
            if (nextIfdPos + 4 > span.Length) return 0;
            return TiffBinaryReader.ReadUInt32(span, (int)nextIfdPos, isLittleEndian);
        }

        /// <summary>
        /// 指定 IFD から開始して NextIFD 連鎖を辿りつつ tags 内のタグを収集する。
        /// </summary>
        public static void CollectTagsRecursive(byte[] span, uint startIfdOffset, bool isLittleEndian, IList<int> tags, ExifData exif)
        {
            var visited = new HashSet<uint>();
            uint currentIfd = startIfdOffset;
            
            while (currentIfd != 0 && currentIfd < span.Length && !visited.Contains(currentIfd))
            {
                visited.Add(currentIfd);

                ForEachEntry(span, currentIfd, isLittleEndian, (entryBase, tag, type, count, valueOrOffset) =>
                {
                    // サブIFDポインタを検出して再帰走査
                    if (tag == 0x8769 && valueOrOffset != 0 && valueOrOffset < span.Length)
                    {
                        CollectTagsRecursive(span, valueOrOffset, isLittleEndian, tags, exif);
                    }

                    if (!tags.Contains((int)tag)) return;

                    var valueStr = ReadTagValue(span, entryBase, type, count, valueOrOffset, isLittleEndian);
                    exif.AdditionalTags[(int)tag] = valueStr;
                });

                var nextIfd = GetNextIfdOffset(span, currentIfd, isLittleEndian);
                if (nextIfd == 0 || nextIfd >= span.Length) break;
                currentIfd = nextIfd;
            }
        }

        /// <summary>
        /// IFD エントリから値を文字列として読み取る。
        /// </summary>
        private static string? ReadTagValue(byte[] span, uint entryBase, ushort type, uint count, uint valueOrOffset, bool isLittleEndian)
        {
            uint valueOffset = valueOrOffset;
            if (TiffBinaryReader.GetTypeUnitLength(type) * count <= 4)
            {
                valueOffset = entryBase + 8;
            }

            return type switch
            {
                2 => TiffBinaryReader.ReadAsciiValue(span, valueOffset, count), // ASCII
                3 => TiffBinaryReader.ReadUInt16(span, (int)valueOffset, isLittleEndian).ToString(CultureInfo.InvariantCulture), // SHORT
                4 => TiffBinaryReader.ReadUInt32(span, (int)valueOffset, isLittleEndian).ToString(CultureInfo.InvariantCulture), // LONG
                5 => TiffBinaryReader.ReadRational(span, valueOffset, isLittleEndian)?.ToString(CultureInfo.InvariantCulture), // RATIONAL
                1 or 7 => TiffBinaryReader.BytesToHex(TiffBinaryReader.ReadBytes(span, valueOffset, count)), // BYTE/UNDEFINED
                9 => TiffBinaryReader.ReadInt32(span, (int)valueOffset, isLittleEndian).ToString(CultureInfo.InvariantCulture), // SLONG
                10 => TiffBinaryReader.ReadSignedRational(span, valueOffset, isLittleEndian)?.ToString(CultureInfo.InvariantCulture), // SRATIONAL
                _ => ReadDefaultTagValue(span, valueOffset, count, type)
            };
        }

        /// <summary>
        /// 未対応型のタグ値をバイナリ表現で読み取る。
        /// </summary>
        private static string? ReadDefaultTagValue(byte[] span, uint valueOffset, uint count, ushort type)
        {
            long byteLenLong = (long)count * TiffBinaryReader.GetTypeUnitLength(type);
            uint byteLen = byteLenLong <= 0 ? 0u : (byteLenLong > uint.MaxValue ? uint.MaxValue : (uint)byteLenLong);
            var bytes = TiffBinaryReader.ReadBytes(span, valueOffset, byteLen);
            return TiffBinaryReader.BytesToHex(bytes);
        }
    }
}
