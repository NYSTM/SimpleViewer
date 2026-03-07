using System.Windows;
using System.Windows.Controls;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// MainWindowのサイズ取得ロジックを担当するクラス。
/// ビューサイズとコンテンツサイズの計算を責務とします。
/// </summary>
public class MainWindowSizeProvider
{
    private readonly ScrollViewer _mainScrollViewer;
    private readonly Image _imageLeft;
    private readonly Image _imageRight;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="mainScrollViewer">メインScrollViewer</param>
    /// <param name="imageLeft">左側画像</param>
    /// <param name="imageRight">右側画像</param>
    public MainWindowSizeProvider(
        ScrollViewer mainScrollViewer,
        Image imageLeft,
        Image imageRight)
    {
        _mainScrollViewer = mainScrollViewer;
        _imageLeft = imageLeft;
        _imageRight = imageRight;
    }

    /// <summary>
    /// ビューサイズを取得します。
    /// </summary>
    /// <returns>ビューサイズ</returns>
    public Size GetViewSize()
    {
        double width = _mainScrollViewer.ViewportWidth > 0
            ? _mainScrollViewer.ViewportWidth
            : _mainScrollViewer.ActualWidth;

        double height = _mainScrollViewer.ViewportHeight > 0
            ? _mainScrollViewer.ViewportHeight
            : _mainScrollViewer.ActualHeight;

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>
    /// コンテンツサイズを取得します。
    /// </summary>
    /// <returns>コンテンツサイズ</returns>
    public Size GetContentSize()
    {
        if (_imageLeft.Source == null)
        {
            return new Size(0, 0);
        }

        double totalWidth = _imageLeft.Source.Width +
            (_imageRight.Visibility == Visibility.Visible ? (_imageRight.Source?.Width ?? 0) : 0);

        double maxHeight = Math.Max(
            _imageLeft.Source.Height,
            _imageRight.Visibility == Visibility.Visible ? (_imageRight.Source?.Height ?? 0) : 0);

        return new Size(totalWidth, maxHeight);
    }
}
