using BenchmarkDotNet.Running;

namespace SimpleExifLib.Benchmarks
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<ExifReadersBenchmarks>();
            Console.WriteLine(summary);
        }
    }
}
