namespace SimpleViewer.Models;

/// <summary>
/// アプリケーションの表示レイアウト状態を定義します。
/// Presenter や View はこの列挙子に基づいてページの描画や遷移量（1ページ or 2ページ）を決定します。
/// </summary>
public enum DisplayMode
{
    /// <summary>
    /// 1ページずつ表示します。
    /// 単一ページ表示ではページ移動時にインデックスが 1 ずつ変化します。
    /// </summary>
    Single,

    /// <summary>
    /// 見開き表示（右から左へ進む、右綴じ向け）。
    /// 通常は右側に現在ページ、左側に次ページ（または見開きの順序）を表示します。
    /// ページ移動では 2 ページ単位で移動する実装が一般的です。
    /// </summary>
    SpreadRTL,

    /// <summary>
    /// 見開き表示（左から右へ進む、左綴じ向け）。
    /// 通常は左側に現在ページ、右側に次ページを表示します。
    /// </summary>
    SpreadLTR
}