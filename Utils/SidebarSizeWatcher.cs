using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SimpleViewer.Utils
{
    // サイドバーのサイズ変更を監視し、適切な幅を通知する責務
    public class SidebarSizeWatcher
    {
        // サムネイルを表示するサイドバーの ItemsControl
        private readonly ItemsControl _thumbnailSidebar;
        // サイズ変更通知をデバウンスするためのタイマー
        private readonly DispatcherTimer _debounceTimer;
        // デバウンス後に呼び出されるコールバック（幅: int）
        private readonly Action<int> _onDebouncedWidth;
        // 最後に計算した幅（内部保持、double）
        private double _currentWidth;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="thumbnailSidebar">監視対象の ItemsControl</param>
        /// <param name="onDebouncedWidth">デバウンス後に幅を受け取るコールバック</param>
        /// <param name="debounceMs">デバウンス間隔（ミリ秒、既定:200ms）</param>
        public SidebarSizeWatcher(ItemsControl thumbnailSidebar, Action<int> onDebouncedWidth, int debounceMs = 200)
        {
            _thumbnailSidebar = thumbnailSidebar;
            _onDebouncedWidth = onDebouncedWidth;
            // Dispatcher の優先度を指定してタイマーを初期化
            _debounceTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(debounceMs) };
            _debounceTimer.Tick += DebounceTimer_Tick;
            // SizeChanged イベントを購読してサイズ変化を検出する
            _thumbnailSidebar.SizeChanged += ThumbnailSidebar_SizeChanged;
        }

        /// <summary>
        /// サイドバーのサイズが変更された際に呼び出されるハンドラ
        /// _currentWidth を計算し、デバウンス用タイマーをリスタートする
        /// （実際の即時描画は呼び出し元で行われるため、このクラスでは遅延通知のみを担う）
        /// </summary>
        private void ThumbnailSidebar_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            // 最小幅を確保しつつ、内側の余白を差し引いた実際のサムネイル幅を計算
            _currentWidth = Math.Max(32, _thumbnailSidebar.ActualWidth - 12);

            // 即時の見た目反映は呼び出し元（ThumbnailController）で行う
            // デバウンス：タイマーを停止して再スタートすることで、頻繁なイベント発生時に通知をまとめる
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        /// <summary>
        /// デバウンスタイマーの Tick ハンドラ
        /// タイマーが発火したら停止し、コールバックに対して四捨五入した幅を通知する
        /// </summary>
        private void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _onDebouncedWidth?.Invoke((int)Math.Round(_currentWidth));
        }
    }
}
