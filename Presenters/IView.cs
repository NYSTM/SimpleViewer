using System.Windows.Media.Imaging;

namespace SimpleViewer.Presenters;

/// <summary>
/// PresenterからUI操作を行うためのインターフェース。
/// View(MainWindow)はこのインターフェースを実装します。
/// </summary>
public interface IView
{
    /// <summary>
    /// 画面に画像を表示します。
    /// </summary>
    /// <param name="left">左側に表示する画像（単ページ時はこれのみ使用）</param>
    /// <param name="right">右側に表示する画像（見開き時のみ使用。無い場合はnull）</param>
    void SetImages(BitmapSource? left, BitmapSource? right);

    /// <summary>
    /// ページ番号やズーム率などのステータス情報を更新します。
    /// </summary>
    /// <param name="current">現在のページ番号（1始まり）</param>
    /// <param name="total">総ページ数</param>
    void UpdateProgress(int current, int total);

    /// <summary>
    /// エラーメッセージをユーザーに通知します。
    /// </summary>
    /// <param name="message">エラー内容</param>
    void ShowError(string message);
}