using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SimpleViewer.Utils
{
    public class SidebarManager
    {
        private readonly SimpleViewerPresenter _presenter;
        private readonly ItemsControl _thumbnailSidebar;
        private readonly Dispatcher _dispatcher;
        private readonly Func<int, Task> _jumpToPageCallback;
        private readonly Action _focusWindowCallback;
        private readonly Style _thumbnailButtonStyle;

        private readonly Dictionary<int, Button> _sidebarItems = new();
        private int _lastHighlightedIndex = -1;
        private CancellationTokenSource? _sidebarCts;

        public SidebarManager(SimpleViewerPresenter presenter, ItemsControl thumbnailSidebar, Dispatcher dispatcher, Func<int, Task> jumpToPageCallback, Action focusWindowCallback, Style thumbnailButtonStyle)
        {
            _presenter = presenter;
            _thumbnailSidebar = thumbnailSidebar;
            _dispatcher = dispatcher;
            _jumpToPageCallback = jumpToPageCallback;
            _focusWindowCallback = focusWindowCallback;
            _thumbnailButtonStyle = thumbnailButtonStyle;
        }

        // サイドバーが既に構築済みかを確認し、必要な場合のみ BuildSidebarAsync を呼ぶヘルパー
        public async Task EnsureSidebarAsync(int totalPages, int currentPageIndex)
        {
            if (totalPages <= 0)
            {
                ClearSidebar();
                return;
            }

            // 既にサイドバーが総ページ数分構築されている場合は再構築しない
            if (_sidebarItems.Count == totalPages && _sidebarItems.Count > 0)
            {
                HighlightThumbnail(currentPageIndex);
                return;
            }

            await BuildSidebarAsync(totalPages, currentPageIndex);
        }

        public async Task BuildSidebarAsync(int totalPages, int currentPageIndex)
        {
            ClearSidebar();

            _sidebarCts = new CancellationTokenSource();
            CancellationToken token = _sidebarCts.Token;

            for (int i = 0; i < totalPages; i++)
            {
                if (token.IsCancellationRequested) return;

                var thumb = await _presenter.GetThumbnailAsync(i, 160, token);

                if (token.IsCancellationRequested) return;

                if (thumb != null)
                {
                    var item = CreateThumbnailElement(thumb, i, 150);
                    _sidebarItems[i] = item;
                    _dispatcher.Invoke(() => _thumbnailSidebar.Items.Add(item));

                    if (i == currentPageIndex)
                    {
                        HighlightThumbnail(i);
                    }
                }
                if (i % 5 == 0) await Task.Yield();
            }
        }

        public void HighlightThumbnail(int index)
        {
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (_lastHighlightedIndex != -1 && _sidebarItems.TryGetValue(_lastHighlightedIndex, out var oldBtn))
                    oldBtn.BorderBrush = Brushes.Transparent;

                _thumbnailSidebar.UpdateLayout(); // レイアウト更新を強制
                if (_sidebarItems.TryGetValue(index, out var currentBtn))
                {
                    currentBtn.BorderBrush = SystemColors.HighlightBrush;
                    currentBtn.BringIntoView();
                    _lastHighlightedIndex = index;

                }
            }, DispatcherPriority.Render); // UI要素が描画準備完了後で実行
        }

        public void ClearSidebar()
        {
            _sidebarCts?.Cancel();
            _sidebarCts = null;

            _dispatcher.Invoke(() =>
            {
                _sidebarItems.Clear();
                _lastHighlightedIndex = -1;
                _thumbnailSidebar.Items.Clear();
            });
        }

        private Button CreateThumbnailElement(BitmapSource source, int index, double width)
        {
            var btn = new Button
            {
                Content = new Image { Source = source, Width = width },
                Tag = index,
                Margin = new Thickness(4),
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                Focusable = false,
                Style = _thumbnailButtonStyle
            };
            btn.Click += async (s, _) =>
            {
                int idx = (int)((Button)s).Tag;
                HighlightThumbnail(idx);
                await _jumpToPageCallback.Invoke(idx);
                _focusWindowCallback?.Invoke();
            };
            return btn;
        }
    }
}