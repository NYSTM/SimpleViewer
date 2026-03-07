using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Utils.UI;

/// <summary>
/// サムネイル用のUI要素を生成するファクトリクラス。
/// Button要素の作成とイベントハンドラの登録を担当します。
/// </summary>
public class ThumbnailElementFactory
{
    private readonly Style _thumbnailButtonStyle;
    private readonly Func<int, Task> _jumpToPageCallback;
    private readonly Action? _focusWindowCallback;
    private readonly Action<int>? _highlightCallback;

    /// <summary>
    /// ThumbnailElementFactory を初期化します。
    /// </summary>
    /// <param name="thumbnailButtonStyle">ボタンに適用するスタイル</param>
    /// <param name="jumpToPageCallback">ページジャンプ用コールバック</param>
    /// <param name="focusWindowCallback">フォーカス復帰用コールバック（オプション）</param>
    /// <param name="highlightCallback">ハイライト用コールバック（オプション）</param>
    public ThumbnailElementFactory(
        Style thumbnailButtonStyle,
        Func<int, Task> jumpToPageCallback,
        Action? focusWindowCallback = null,
        Action<int>? highlightCallback = null)
    {
        _thumbnailButtonStyle = thumbnailButtonStyle;
        _jumpToPageCallback = jumpToPageCallback;
        _focusWindowCallback = focusWindowCallback;
        _highlightCallback = highlightCallback;
    }

    /// <summary>
    /// サムネイルボタンを生成します。
    /// </summary>
    /// <param name="thumbnail">サムネイル画像</param>
    /// <param name="index">ページインデックス</param>
    /// <returns>生成したButton要素</returns>
    public Button CreateSimpleButton(BitmapSource? thumbnail, int index)
    {
        var btn = new Button
        {
            Style = _thumbnailButtonStyle,
            Content = thumbnail != null ? new Image { Source = thumbnail } : null,
            Tag = index,
            Margin = new Thickness(4),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Focusable = false
        };
        btn.Click += async (s, e) =>
        {
            _highlightCallback?.Invoke(index);

            try
            {
                await _jumpToPageCallback(index);
                _highlightCallback?.Invoke(index);
                _focusWindowCallback?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThumbnailElementFactory] ページジャンプエラー: {ex.Message}");
            }
        };
        return btn;
    }

    /// <summary>
    /// サムネイル要素（Button）を生成します。
    /// ボタンクリックでページジャンプとハイライト更新を行います。
    /// </summary>
    /// <param name="source">サムネイル画像ソース</param>
    /// <param name="index">サムネイルのインデックス</param>
    /// <param name="width">サムネイル幅</param>
    /// <returns>生成した Button 要素</returns>
    public Button CreateThumbnailElement(BitmapSource source, int index, double width)
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

        btn.Click += async (s, e) =>
        {
            int idx = (int)((Button)s!).Tag;

            _highlightCallback?.Invoke(idx);

            try
            {
                await _jumpToPageCallback.Invoke(idx);
                _highlightCallback?.Invoke(idx);
                _focusWindowCallback?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ThumbnailElementFactory] ページジャンプエラー: {ex.Message}");
            }
        };

        return btn;
    }

    /// <summary>
    /// 既存のボタンの画像を更新します。
    /// </summary>
    /// <param name="btn">更新対象のボタン</param>
    /// <param name="thumbnail">新しいサムネイル画像</param>
    public static void UpdateButtonImage(Button btn, BitmapSource? thumbnail)
    {
        if (btn.Content is Image img)
        {
            img.Source = thumbnail;
        }
        else if (thumbnail != null)
        {
            btn.Content = new Image { Source = thumbnail };
        }
    }

    /// <summary>
    /// 既存のボタンの画像と幅を更新します。
    /// </summary>
    /// <param name="btn">更新対象のボタン</param>
    /// <param name="thumbnail">新しいサムネイル画像</param>
    /// <param name="width">新しい幅</param>
    public static void UpdateButtonImageAndWidth(Button btn, BitmapSource? thumbnail, int width)
    {
        if (btn.Content is Image img)
        {
            img.Source = thumbnail;
            img.Width = width;
        }
        else if (thumbnail != null)
        {
            btn.Content = new Image { Source = thumbnail, Width = width };
        }
    }
}
