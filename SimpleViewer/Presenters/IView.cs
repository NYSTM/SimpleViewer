using System.Windows.Media.Imaging;

namespace SimpleViewer.Presenters;

/// <summary>
/// Presenter から View（UI） を操作するためのインターフェース。
/// MainWindow はこのインターフェースを実装し、Presenter は UI 更新をこのインターフェース経由で行います。
/// 実装は UI スレッドでの更新を前提とするため、Presenter 側は必要に応じて Dispatcher を使って
/// UI スレッドでこれらのメソッドを呼び出すか、UI 側がスレッドセーフに扱えるよう配慮すること。
/// </summary>
public interface IView
{
    /// <summary>
    /// 画面に画像を表示します。
    /// 通常は UI スレッドで呼ばれることを想定しています。
    /// </summary>
    /// <param name="left">左側に表示する画像（単ページ表示時はこれのみ使用）</param>
    /// <param name="right">右側に表示する画像（見開き表示時に使用。無い場合は null）</param>
    void SetImages(BitmapSource? left, BitmapSource? right);

    /// <summary>
    /// ページ番号や総ページ数などの進捗情報を UI に反映します。
    /// Presenter は current を 1 始まりで渡すことを想定しています（UI 表示のため）。
    /// </summary>
    /// <param name="current">現在のページ番号（1 始まり）</param>
    /// <param name="total">総ページ数</param>
    void UpdateProgress(int current, int total);

    /// <summary>
    /// ユーザーに対してエラーメッセージを表示します。
    /// 実装はダイアログ表示やステータスメッセージ領域への表示など自由に行って構いません。
    /// </summary>
    /// <param name="message">表示するエラーメッセージ</param>
    void ShowError(string message);
}