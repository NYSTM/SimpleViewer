using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Reflection;

namespace SimpleExifLib.Benchmarks
{
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    public class ExifReadersBenchmarks
    {
        private byte[] sampleWithExif = Array.Empty<byte>();
        private byte[] sampleWithoutExif = Array.Empty<byte>();

        [GlobalSetup]
        public void Setup()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var f1 = Path.Combine(dir, "test_with_exif.jpg");
            var f2 = Path.Combine(dir, "test_without_exif.jpg");

            // もしファイルが存在しなければ、ランタイムで生成する
            if (!File.Exists(f1))
            {
                var data = CreateSampleWithExif();
                File.WriteAllBytes(f1, data);
            }
            if (!File.Exists(f2))
            {
                var data = CreateSampleWithoutExif();
                File.WriteAllBytes(f2, data);
            }

            sampleWithExif = File.Exists(f1) ? File.ReadAllBytes(f1) : Array.Empty<byte>();
            sampleWithoutExif = File.Exists(f2) ? File.ReadAllBytes(f2) : Array.Empty<byte>();
        }

        [Benchmark(Baseline = true)]
        public void MetadataExtractor_WithExif()
        {
            if (sampleWithExif.Length == 0) return;
            using var ms = new MemoryStream(sampleWithExif);
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(ms);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var _ = exif?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
            }
            catch
            {
                // 解析失敗はスキップ（ベンチ中断を防止）
            }
        }

        [Benchmark]
        public void SimpleExifLib_WithExif()
        {
            if (sampleWithExif.Length == 0) return;
            using var ms = new MemoryStream(sampleWithExif);
            var _ = ExifReaderFactory.ReadAsync(ms).GetAwaiter().GetResult();
        }

        [Benchmark]
        public void MetadataExtractor_WithoutExif()
        {
            if (sampleWithoutExif.Length == 0) return;
            using var ms = new MemoryStream(sampleWithoutExif);
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(ms);
                var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var _ = exif?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
            }
            catch
            {
            }
        }

        [Benchmark]
        public void SimpleExifLib_WithoutExif()
        {
            if (sampleWithoutExif.Length == 0) return;
            using var ms = new MemoryStream(sampleWithoutExif);
            var _ = ExifReaderFactory.ReadAsync(ms).GetAwaiter().GetResult();
        }

        // シンプルなサンプルJPEG（APP1 に Exif\0\0 と ASCII タグを含む）を生成する
        private static byte[] CreateSampleWithExif()
        {
            using var ms = new MemoryStream();
            void Write(params byte[] b) => ms.Write(b, 0, b.Length);

            // SOI
            Write(new byte[] { 0xFF, 0xD8 });

            // APP1 ペイロードをバイトで構築
            var payloadBytes = new List<byte>();
            void AddAscii(string txt)
            {
                var b = System.Text.Encoding.ASCII.GetBytes(txt);
                payloadBytes.AddRange(b);
            }

            // Exif\0\0
            AddAscii("Exif");
            payloadBytes.Add(0);
            payloadBytes.Add(0);

            AddAscii("Make"); payloadBytes.Add(0); AddAscii("Sony"); payloadBytes.Add(0);
            AddAscii("Model"); payloadBytes.Add(0); AddAscii("X100"); payloadBytes.Add(0);
            AddAscii("DateTimeOriginal"); payloadBytes.Add(0); AddAscii("2010:01:01 12:00:00"); payloadBytes.Add(0);
            AddAscii("Orientation"); payloadBytes.Add(0); AddAscii("1"); payloadBytes.Add(0);

            var payload = payloadBytes.ToArray();
            var app1Length = payload.Length + 2; // length includes the two size bytes
            Write(new byte[] { 0xFF, 0xE1, (byte)(app1Length >> 8), (byte)(app1Length & 0xFF) });
            Write(payload);

            // SOS (画像データ開始) - これで JpegExifReader のループを抜けさせる
            Write(new byte[] { 0xFF, 0xDA });

            // minimal image data and EOI
            Write(new byte[] { 0x00, 0x3F, 0xFF, 0xD9 });

            return ms.ToArray();
        }

        // EXIF がないシンプルJPEGを生成する
        private static byte[] CreateSampleWithoutExif()
        {
            using var ms = new MemoryStream();
            void Write(params byte[] b) => ms.Write(b, 0, b.Length);

            // SOI
            Write(new byte[] { 0xFF, 0xD8 });
            // SOS すぐに配置
            Write(new byte[] { 0xFF, 0xDA });
            // minimal image data and EOI
            Write(new byte[] { 0x00, 0x3F, 0xFF, 0xD9 });

            return ms.ToArray();
        }
    }
}
