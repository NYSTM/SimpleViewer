using System.Globalization;
using System.Text;

namespace SimpleExifLib
{
    /// <summary>
    /// TIFF バイナリデータの読み取りを高速に行うユーティリティクラス。
    /// unsafe コードを使用してパフォーマンスを最適化している。
    /// </summary>
    internal static class TiffBinaryReader
    {
        /// <summary>
        /// UInt16 値を指定されたエンディアンで読み取る。
        /// </summary>
        public static unsafe ushort ReadUInt16(byte[] data, int offset, bool isLittleEndian)
        {
            if (offset < 0 || offset + 2 > data.Length) return 0;
            
            fixed (byte* p = &data[offset])
            {
                return isLittleEndian
                    ? (ushort)(p[0] | (p[1] << 8))
                    : (ushort)((p[0] << 8) | p[1]);
            }
        }

        /// <summary>
        /// UInt32 値を指定されたエンディアンで読み取る。
        /// </summary>
        public static unsafe uint ReadUInt32(byte[] data, int offset, bool isLittleEndian)
        {
            if (offset < 0 || offset + 4 > data.Length) return 0;
            
            fixed (byte* p = &data[offset])
            {
                return isLittleEndian
                    ? (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24))
                    : (uint)((p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3]);
            }
        }

        /// <summary>
        /// Int32 値を指定されたエンディアンで読み取る（符号付き）。
        /// </summary>
        public static unsafe int ReadInt32(byte[] data, int offset, bool isLittleEndian)
        {
            if (offset < 0 || offset + 4 > data.Length) return 0;
            
            fixed (byte* p = &data[offset])
            {
                return isLittleEndian
                    ? p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24)
                    : (p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3];
            }
        }

        /// <summary>
        /// TIFF データ内の valueOffset から ASCII/UTF-8 文字列を取得する（境界チェックあり）。
        /// ASCII 範囲外のバイトが含まれる場合は UTF-8 としてデコードする。
        /// </summary>
        public static unsafe string? ReadAsciiValue(byte[] data, uint valueOffset, uint count)
        {
            try
            {
                var off = (int)valueOffset;
                if (off < 0 || off >= data.Length) return null;
                var len = (int)count;
                if (len <= 0) return null;
                if (off + len > data.Length) len = data.Length - off;

                // null終端文字の検索とASCII範囲チェックを1回のループで実施
                int actualLen = len;
                bool hasNonAscii = false;
                
                fixed (byte* basePtr = &data[off])
                {
                    for (int i = 0; i < len; i++)
                    {
                        byte b = basePtr[i];
                        if (b == 0)
                        {
                            actualLen = i;
                            break;
                        }
                        if (b > 127)
                        {
                            hasNonAscii = true;
                        }
                    }

                    if (actualLen == 0) return string.Empty;

                    // ASCII のみの場合は高速なポインタベース処理
                    if (!hasNonAscii)
                    {
                        return new string((sbyte*)basePtr, 0, actualLen, Encoding.ASCII);
                    }
                }

                // UTF-8 としてデコード（非ASCII文字を含む場合）
                return Encoding.UTF8.GetString(data, off, actualLen);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// RATIONAL 値を読み出す（分子/分母）。
        /// </summary>
        public static double? ReadRational(byte[] data, uint valueOffset, bool isLittleEndian)
        {
            var off = (int)valueOffset;
            if (off < 0 || off + 8 > data.Length) return null;
            
            var numerator = ReadUInt32(data, off, isLittleEndian);
            var denominator = ReadUInt32(data, off + 4, isLittleEndian);
            
            return denominator == 0 ? null : (double)numerator / denominator;
        }

        /// <summary>
        /// 符号付き RATIONAL 値を読み出す。
        /// </summary>
        public static double? ReadSignedRational(byte[] data, uint valueOffset, bool isLittleEndian)
        {
            var off = (int)valueOffset;
            if (off < 0 || off + 8 > data.Length) return null;
            
            var numerator = ReadInt32(data, off, isLittleEndian);
            var denominator = ReadInt32(data, off + 4, isLittleEndian);
            
            return denominator == 0 ? null : (double)numerator / denominator;
        }

        /// <summary>
        /// 指定位置からバイト列を安全に抽出する。
        /// </summary>
        public static byte[] ReadBytes(byte[] data, uint offset, uint count)
        {
            var off = (int)offset;
            if (off < 0 || count == 0 || off >= data.Length) return Array.Empty<byte>();
            
            var available = data.Length - off;
            var take = (int)Math.Min(available, count);
            if (take <= 0) return Array.Empty<byte>();
            
            var result = new byte[take];
            Buffer.BlockCopy(data, off, result, 0, take);
            return result;
        }

        /// <summary>
        /// バイト配列を 16 進表記で返す（スペース区切り）。
        /// </summary>
        public static string BytesToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            
            // 事前に必要なサイズを計算（各バイト2文字 + スペース）
            var sb = new StringBuilder(bytes.Length * 3 - 1);
            
            sb.Append(bytes[0].ToString("X2", CultureInfo.InvariantCulture));
            for (int i = 1; i < bytes.Length; i++)
            {
                sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// TIFF タイプごとの単位バイト長を返す。
        /// </summary>
        public static int GetTypeUnitLength(ushort type)
        {
            return type switch
            {
                1 => 1,  // BYTE
                2 => 1,  // ASCII
                3 => 2,  // SHORT
                4 => 4,  // LONG
                5 => 8,  // RATIONAL
                7 => 1,  // UNDEFINED
                9 => 4,  // SLONG
                10 => 8, // SRATIONAL
                _ => 1,
            };
        }
    }
}
