namespace SimpleViewer.Models;

/// <summary>
/// アプリケーションの表示レイアウト状態を定義します。
/// </summary>
public enum DisplayMode
{
    /// <summary> 1ページずつ表示 </summary>
    Single,

    /// <summary> 右から左へ（右綴じ） </summary>
    SpreadRTL,

    /// <summary> 左から右へ（左綴じ） </summary>
    SpreadLTR
}