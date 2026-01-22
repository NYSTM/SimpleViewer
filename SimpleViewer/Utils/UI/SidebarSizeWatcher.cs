using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// サイドバー（サムネイルの領域）のサイズ変化を監視し、デバウンス後に確定幅を通知するユーティリティクラス。
    /// UI スレッドで DispatcherTimer を用いて連続なリサイズイベントを抑制します。
    /// </summary>
    public class SidebarSizeWatcher
    {
        // サムネイルを表示する ItemsControl
        private readonly ItemsControl _thumbnailSidebar;
        // デバウンス用タイマー
        private readonly DispatcherTimer _debounceTimer;
        // デバウンス完了時に確定幅を通知する非同期コールバック
        private readonly Func<int, Task>? _onDebouncedWidthAsync;
        // デバウンス完了時に確定幅を通知する同期コールバック
        private readonly Action<int>? _onDebouncedWidth;
        // 最新の計算幅を保持するフィールド
        private double _currentWidth;

        /// <summary>
        /// コンストラクタ: ItemsControl と非同期通知コールバックを受け取って監視を開始します。
        /// </summary>
        /// <param name="thumbnailSidebar">監視対象の ItemsControl</param>
        /// <param name="onDebouncedWidth">デバウンス後に確定幅を受け取る非同期コールバック</param>
        /// <param name="debounceMs">デバウンス待機時間（ミリ秒: 既定 200ms）</param>
        public SidebarSizeWatcher(ItemsControl thumbnailSidebar, Func<int, Task> onDebouncedWidth, int debounceMs = 200)
        {
            _thumbnailSidebar = thumbnailSidebar ?? throw new ArgumentNullException(nameof(thumbnailSidebar));
            _onDebouncedWidthAsync = onDebouncedWidth ?? throw new ArgumentNullException(nameof(onDebouncedWidth));

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Input) 
            { 
                Interval = TimeSpan.FromMilliseconds(debounceMs) 
            };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _thumbnailSidebar.SizeChanged += OnThumbnailSidebarSizeChanged;
        }

        /// <summary>
        /// コンストラクタ: ItemsControl と同期通知コールバックを受け取って監視を開始します。
        /// </summary>
        /// <param name="thumbnailSidebar">監視対象の ItemsControl</param>
        /// <param name="onDebouncedWidth">デバウンス後に確定幅を受け取る同期コールバック</param>
        /// <param name="debounceMs">デバウンス待機時間（ミリ秒: 既定 200ms）</param>
        public SidebarSizeWatcher(ItemsControl thumbnailSidebar, Action<int> onDebouncedWidth, int debounceMs = 200)
        {
            _thumbnailSidebar = thumbnailSidebar ?? throw new ArgumentNullException(nameof(thumbnailSidebar));
            _onDebouncedWidth = onDebouncedWidth ?? throw new ArgumentNullException(nameof(onDebouncedWidth));

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Input) 
            { 
                Interval = TimeSpan.FromMilliseconds(debounceMs) 
            };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _thumbnailSidebar.SizeChanged += OnThumbnailSidebarSizeChanged;
        }

        /// <summary>
        /// サムネイルの領域のサイズ変更イベントハンドラ。
        /// 実際の通知はデバウンスタイマによって遅延されます。
        /// </summary>
        private void OnThumbnailSidebarSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            _currentWidth = Math.Max(32, _thumbnailSidebar.ActualWidth - 12);

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>
        /// デバウンスタイマの Tick ハンドラ。確定した幅をコールバックへ通知します。
        /// </summary>
        private async void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            
            int width = (int)Math.Round(_currentWidth);
            
            try
            {
                if (_onDebouncedWidthAsync != null)
                {
                    await _onDebouncedWidthAsync(width);
                }
                else
                {
                    _onDebouncedWidth?.Invoke(width);
                }
            }
            catch
            {
                // コールバックの失敗は UI に影響しない
            }
        }
    }
}
