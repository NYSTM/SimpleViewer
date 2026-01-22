using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Utils.Caching
{
    /// <summary>
    /// BitmapSource のインデックス付きメモリキャッシュ管理を行うユーティリティ。
    /// キャッシュ容量とメモリ閾値に応じて古いエントリを削除するロジックを提供します。
    /// </summary>
    public class ImageCacheManager : IImageCache
    {
        private readonly ConcurrentDictionary<int, BitmapSource> cache = new();
        private readonly int maxCachePages;
        private readonly long memoryThresholdBytes;

        public ImageCacheManager(int maxCachePages = 12, long memoryThresholdMB = 500)
        {
            this.maxCachePages = Math.Max(1, maxCachePages);
            this.memoryThresholdBytes = Math.Max(1, memoryThresholdMB) * 1024 * 1024;
        }

        /// <summary>
        /// 指定キーのキャッシュを取得します。存在すれば true と値を返します。
        /// </summary>
        public bool TryGet(int index, out BitmapSource? bitmap)
        {
            return cache.TryGetValue(index, out bitmap);
        }

        /// <summary>
        /// キャッシュへ追加または上書きします。
        /// </summary>
        public void Add(int index, BitmapSource bitmap)
        {
            if (bitmap == null) return;
            cache[index] = bitmap;
        }

        /// <summary>
        /// キャッシュ内のエントリ数を返します。
        /// </summary>
        public int Count => cache.Count;

        /// <summary>
        /// 指定キーを削除します。
        /// </summary>
        public bool TryRemove(int index)
        {
            return cache.TryRemove(index, out _);
        }

        /// <summary>
        /// キャッシュを完全にクリアします。
        /// </summary>
        public void Clear()
        {
            cache.Clear();
        }

        /// <summary>
        /// メモリ状況とキャッシュ容量に応じて、中心となるインデックスから遠い順に一部を削除します。
        /// </summary>
        public void PurgeIfNeeded(int centerIndex)
        {
            try
            {
                long availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (availableBytes < memoryThresholdBytes || cache.Count > maxCachePages)
                {
                    var keysToRemove = cache.Keys
                        .OrderByDescending(k => Math.Abs(k - centerIndex))
                        .Take(Math.Max(1, cache.Count / 2))
                        .ToList();

                    foreach (var k in keysToRemove) cache.TryRemove(k, out _);

                    // GC を促す判断は呼び出し側のポリシーに依存するが、ここでは軽く促す
                    GC.Collect();
                }
            }
            catch
            {
                // 失敗しても続行
            }
        }
    }
}
