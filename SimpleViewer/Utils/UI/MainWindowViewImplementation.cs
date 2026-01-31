using SimpleViewer.Presenters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// IView実装を担当するクラス。
/// MainWindowから画像表示、進捗更新、エラー表示の責務を分離します。
/// </summary>
public class MainWindowViewImplementation : IView
{
    private readonly Image _imageLeft;
    private readonly Image _imageRight;
    private readonly ScrollViewer _mainScrollViewer;
    private readonly TextBlock _statusText;
    private readonly TextBlock _modeText;
    private readonly Slider _pageSlider;
    private readonly Window _owner;
    private readonly Func<SidebarManager> _getSidebarManagerFunc;
    private FileOpenHandler? _fileOpenHandler;
    private readonly Action<Func<Size>, Func<Size>> _onViewSizeChangedCallback;
    private readonly Func<SimpleViewer.Models.DisplayMode> _getDisplayModeFunc;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="imageLeft">左側画像表示用Image</param>
    /// <param name="imageRight">右側画像表示用Image</param>
    /// <param name="mainScrollViewer">メインScrollViewer</param>
    /// <param name="statusText">ステータス表示用TextBlock</param>
    /// <param name="modeText">モード表示用TextBlock</param>
    /// <param name="pageSlider">ページスライダー</param>
    /// <param name="owner">オーナーウィンドウ</param>
    /// <param name="getSidebarManagerFunc">SidebarManager取得関数</param>
    /// <param name="fileOpenHandler">ファイルオープンハンドラー（nullの場合、SetFileOpenHandlerで後から設定）</param>
    /// <param name="onViewSizeChangedCallback">ビューサイズ変更時のコールバック</param>
    /// <param name="getDisplayModeFunc">表示モード取得関数</param>
    public MainWindowViewImplementation(
        Image imageLeft,
        Image imageRight,
        ScrollViewer mainScrollViewer,
        TextBlock statusText,
        TextBlock modeText,
        Slider pageSlider,
        Window owner,
        Func<SidebarManager> getSidebarManagerFunc,
        FileOpenHandler? fileOpenHandler,
        Action<Func<Size>, Func<Size>> onViewSizeChangedCallback,
        Func<SimpleViewer.Models.DisplayMode> getDisplayModeFunc)
    {
        _imageLeft = imageLeft;
        _imageRight = imageRight;
        _mainScrollViewer = mainScrollViewer;
        _statusText = statusText;
        _modeText = modeText;
        _pageSlider = pageSlider;
        _owner = owner;
        _getSidebarManagerFunc = getSidebarManagerFunc;
        _fileOpenHandler = fileOpenHandler;
        _onViewSizeChangedCallback = onViewSizeChangedCallback;
        _getDisplayModeFunc = getDisplayModeFunc;
    }

    /// <summary>
    /// FileOpenHandlerを設定します（初期化時に未設定の場合に使用）。
    /// </summary>
    /// <param name="fileOpenHandler">設定するFileOpenHandler</param>
    public void SetFileOpenHandler(FileOpenHandler fileOpenHandler)
    {
        _fileOpenHandler = fileOpenHandler;
    }

    /// <summary>
    /// 画像を設定します。
    /// </summary>
    /// <param name="left">左側画像</param>
    /// <param name="right">右側画像</param>
    public void SetImages(BitmapSource? left, BitmapSource? right)
    {
        _imageLeft.Source = left;
        _imageRight.Source = right;
        _imageRight.Visibility = (right == null) ? Visibility.Collapsed : Visibility.Visible;
        _mainScrollViewer.ScrollToHome();
        
        _onViewSizeChangedCallback(
            () => new Size(_mainScrollViewer.ActualWidth - 4, _mainScrollViewer.ActualHeight - 4),
            GetContentSize);
    }

    /// <summary>
    /// 進捗情報を更新します。
    /// </summary>
    /// <param name="current">現在のページ番号（1始まり）</param>
    /// <param name="total">総ページ数</param>
    public void UpdateProgress(int current, int total)
    {
        _statusText.Text = $"{current} / {total}";
        _pageSlider.Maximum = Math.Max(0, total - 1);
        _pageSlider.Value = current - 1;
        UpdateModeDisplay();

        // タイトルを非同期で更新
        if (_fileOpenHandler != null)
        {
            _ = _fileOpenHandler.UpdateTitleForPageAsync(current);
        }

        // サイドバーを更新
        var sidebarManager = _getSidebarManagerFunc();
        if (total > 0)
        {
            _ = sidebarManager.EnsureSidebarAsync(total, current - 1);
            sidebarManager.HighlightThumbnail(current - 1);
        }
        else
        {
            sidebarManager.ClearSidebar();
        }
    }

    /// <summary>
    /// エラーメッセージを表示します。
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public void ShowError(string message)
    {
        MessageBox.Show(_owner, message, "SimpleViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// モード表示を更新します。
    /// </summary>
    private void UpdateModeDisplay()
    {
        if (_modeText == null) return;

        _modeText.Text = _getDisplayModeFunc() switch
        {
            SimpleViewer.Models.DisplayMode.Single => "単一表示",
            SimpleViewer.Models.DisplayMode.SpreadRTL => "見開き(右)",
            SimpleViewer.Models.DisplayMode.SpreadLTR => "見開き(左)",
            _ => "---"
        };
    }

    /// <summary>
    /// コンテンツサイズを取得します。
    /// </summary>
    /// <returns>コンテンツサイズ</returns>
    private Size GetContentSize()
    {
        if (_imageLeft.Source == null) return new Size(0, 0);

        double totalWidth = _imageLeft.Source.Width +
            (_imageRight.Visibility == Visibility.Visible ? (_imageRight.Source?.Width ?? 0) : 0);
        double maxHeight = Math.Max(
            _imageLeft.Source.Height,
            _imageRight.Visibility == Visibility.Visible ? (_imageRight.Source?.Height ?? 0) : 0);

        return new Size(totalWidth, maxHeight);
    }
}
