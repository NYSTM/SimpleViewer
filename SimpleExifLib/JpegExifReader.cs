using System.IO;
using System.Text;

namespace SimpleExifLib
{
    /// <summary>
    /// JPEG ストリームから最小限の EXIF を高速に読み取る実装。
    /// TIFF/IFD を限定的にパースして主要タグを取得し、必要に応じて文字列検索をフォールバックとして用いる。
    /// </summary>
    internal static class JpegExifReader
    {
        private const ushort MarkStart = 0xFFD8;
        private const ushort MarkApp1 = 0xFFE1;

        /// <summary>
        /// ストリームから EXIF を非同期に読み取る。
        /// ストリームはシーク可能である必要があり、この メソッドは内部でストリームを先頭に戻す。
        /// </summary>
        /// <param name="stream">画像データを含むシーク可能なストリーム。</param>
        /// <returns>パースに成功した場合は <see cref="ExifData"/> を返し、失敗または EXIF が存在しない場合は null を返す。</returns>
        /// <exception cref="ArgumentNullException">stream が null の場合。</exception>
        /// <exception cref="ArgumentException">stream がシーク不可能な場合。</exception>
        public static async Task<ExifData?> ReadAsync(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("ストリームはシーク可能である必要があります", nameof(stream));

            var settings = ExifSettings.Load();
            stream.Seek(0, SeekOrigin.Begin);

            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            // SOI (Start Of Image) マーカーの確認
            if (ReadBigEndianUInt16(br) != MarkStart) return null;

            return await Task.FromResult(ProcessJpegMarkers(br, settings));
        }

        /// <summary>
        /// 指定ファイルパスから EXIF を非同期に読み取る便利メソッド。
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
        /// ストリームから EXIF を同期的に読み取る便利メソッド（内部で非同期 API を同期実行します）。
        /// </summary>
        public static ExifData? Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("ストリームはシーク可能である必要があります", nameof(stream));

            return ReadAsync(stream).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 指定ファイルパスから EXIF を同期的に読み取る便利メソッド。
        /// </summary>
        public static ExifData? Read(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) return null;

            using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(fs);
        }

        /// <summary>
        /// JPEG マーカーを処理して EXIF データを抽出する。
        /// APP1 セグメントを探索し、EXIF データが見つかった場合はパースして返す。
        /// </summary>
        /// <param name="br">JPEG ストリームを読み取る BinaryReader。</param>
        /// <param name="settings">EXIF 読み取り設定。</param>
        /// <returns>EXIF データが見つかった場合は <see cref="ExifData"/>, 見つからない場合は null。</returns>
        private static ExifData? ProcessJpegMarkers(BinaryReader br, ExifSettings settings)
        {
            while (true)
            {
                ushort marker = ReadBigEndianUInt16(br);
                if (marker == 0xFFDA) break; // SOS (Start Of Scan): 画像データ開始

                if ((marker & 0xFFF0) == 0xFFE0) // APPn マーカー
                {
                    var size = ReadBigEndianUInt16(br);
                    if (marker == MarkApp1)
                    {
                        var exif = ProcessApp1Segment(br, size, settings);
                        if (exif != null) return exif;
                    }
                    else
                    {
                        // APP1 以外のセグメントはスキップ
                        br.BaseStream.Seek(size - 2, SeekOrigin.Current);
                    }
                }
                else
                {
                    // その他のマーカーもスキップ
                    var size = ReadBigEndianUInt16(br);
                    br.BaseStream.Seek(size - 2, SeekOrigin.Current);
                }
            }
            return null;
        }

        /// <summary>
        /// APP1 セグメントを処理して EXIF データを抽出する。
        /// EXIF ヘッダーの妥当性を確認した後、TIFF/IFD パーサーで解析を試み、
        /// 失敗した場合はASCII フォールバック解析を行う。
        /// </summary>
        /// <param name="br">JPEG ストリームを読み取る BinaryReader。</param>
        /// <param name="size">APP1 セグメントのサイズ（バイト）。</param>
        /// <param name="settings">EXIF 読み取り設定。</param>
        /// <returns>EXIF データが取得できた場合は <see cref="ExifData"/>, 取得できない場合は null。</returns>
        private static ExifData? ProcessApp1Segment(BinaryReader br, ushort size, ExifSettings settings)
        {
            // EXIF ヘッダー ("Exif\0\0") の読み取り
            var header = br.ReadBytes(6);
            if (!IsValidExifHeader(header))
            {
                // EXIF ヘッダーが無効な場合、残りのセグメントをスキップ
                br.BaseStream.Seek(size - 8, SeekOrigin.Current);
                return null;
            }

            // TIFF データ部分の読み取り
            var remaining = br.ReadBytes(size - 8);
            
            // TIFF/IFD パーサーで解析を試み、失敗したら ASCII フォールバックを使用
            var exif = ExifParser.ParseFromApp1(remaining, settings) ?? 
                       ExifParser.ParseAsciiFallback(remaining, settings);
            
            // 追加タグが設定されている場合は収集
            if (exif != null && settings.AdditionalTagIds.Count > 0)
            {
                ExifParser.PopulateAdditionalTags(exif, remaining, settings.AdditionalTagIds);
            }
            
            return exif;
        }

        /// <summary>
        /// EXIF ヘッダーの妥当性を確認する。
        /// ヘッダーは "Exif\0\0" の 6 バイトである必要がある。
        /// </summary>
        /// <param name="header">検証するヘッダーバイト配列。</param>
        /// <returns>有効な EXIF ヘッダーの場合は true、それ以外は false。</returns>
        private static bool IsValidExifHeader(byte[] header)
        {
            return header.Length == 6 && 
                   header[0] == (byte)'E' && 
                   header[1] == (byte)'x' &&
                   header[2] == (byte)'i' && 
                   header[3] == (byte)'f';
        }

        /// <summary>
        /// ビッグエンディアン形式で UInt16 値を読み取る。
        /// JPEG マーカーはビッグエンディアンで格納されている。
        /// </summary>
        /// <param name="br">読み取り元の BinaryReader。</param>
        /// <returns>読み取った UInt16 値。</returns>
        private static ushort ReadBigEndianUInt16(BinaryReader br)
        {
            return (ushort)((br.ReadByte() << 8) | br.ReadByte());
        }
    }
}
