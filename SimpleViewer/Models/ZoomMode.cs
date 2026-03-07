namespace SimpleViewer.Models;

/// <summary>
/// ズームのモードを表す列挙型。
/// </summary>
public enum ZoomMode
{
    /// <summary>ユーザーによる手動ズーム</summary>
    Manual,
    /// <summary>表示領域の幅に合わせる</summary>
    FitWidth,
    /// <summary>ページ全体が表示されるように合わせる</summary>
    FitPage
}
