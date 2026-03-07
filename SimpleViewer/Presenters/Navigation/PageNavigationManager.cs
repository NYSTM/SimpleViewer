using SimpleViewer.Models;

namespace SimpleViewer.Presenters.Navigation;

/// <summary>
/// ページナビゲーションを管理するクラス。
/// ページ遷移、インデックス計算、表示モードに応じた画像配置を担当します。
/// </summary>
public class PageNavigationManager
{
    /// <summary>
    /// 現在のページインデックス（0 始まり）
    /// </summary>
    public int CurrentPageIndex { get; private set; }

    /// <summary>
    /// 総ページ数
    /// </summary>
    public int TotalPageCount { get; private set; }

    /// <summary>
    /// 現在の表示モード
    /// </summary>
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Single;

    /// <summary>
    /// ナビゲーション操作のキャンセルトークンソース
    /// </summary>
    private CancellationTokenSource? _navigationCts;

    /// <summary>
    /// PageNavigationManagerを初期化します。
    /// </summary>
    public PageNavigationManager()
    {
        CurrentPageIndex = 0;
        TotalPageCount = 0;
    }

    /// <summary>
    /// 総ページ数を設定します。
    /// </summary>
    /// <param name="totalCount">総ページ数</param>
    public void SetTotalPageCount(int totalCount)
    {
        TotalPageCount = Math.Max(0, totalCount);
        if (TotalPageCount > 0)
        {
            CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, TotalPageCount - 1);
        }
        else
        {
            CurrentPageIndex = 0;
        }
    }

    /// <summary>
    /// 現在のページインデックスをリセットします。
    /// </summary>
    public void Reset()
    {
        CurrentPageIndex = 0;
        TotalPageCount = 0;
        CancelNavigation();
    }

    /// <summary>
    /// 進行中のナビゲーション操作をキャンセルします。
    /// </summary>
    public void CancelNavigation()
    {
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = null;
    }

    /// <summary>
    /// 新しいナビゲーション用キャンセルトークンを取得します。
    /// 既存のナビゲーションは自動的にキャンセルされます。
    /// </summary>
    /// <returns>新しいキャンセルトークン</returns>
    public CancellationToken GetNewNavigationToken()
    {
        CancelNavigation();
        _navigationCts = new CancellationTokenSource();
        return _navigationCts.Token;
    }

    /// <summary>
    /// 指定インデックスへジャンプします。
    /// </summary>
    /// <param name="targetIndex">ジャンプ先のページインデックス</param>
    /// <returns>進行方向（true=前方、false=後方）</returns>
    public bool JumpToPage(int targetIndex)
    {
        if (TotalPageCount == 0) return true;

        int clampedIndex = Math.Clamp(targetIndex, 0, TotalPageCount - 1);
        bool isForward = clampedIndex >= CurrentPageIndex;
        CurrentPageIndex = clampedIndex;
        return isForward;
    }

    /// <summary>
    /// 次のページへ移動します。
    /// </summary>
    /// <returns>移動後のページインデックス</returns>
    public int MoveToNextPage()
    {
        int step = GetPageStep();
        return JumpToPage(CurrentPageIndex + step) ? CurrentPageIndex : CurrentPageIndex;
    }

    /// <summary>
    /// 前のページへ移動します。
    /// </summary>
    /// <returns>移動後のページインデックス</returns>
    public int MoveToPreviousPage()
    {
        int step = GetPageStep();
        return JumpToPage(CurrentPageIndex - step) ? CurrentPageIndex : CurrentPageIndex;
    }

    /// <summary>
    /// 表示モードに応じたページ移動量を取得します。
    /// </summary>
    /// <returns>移動量（単一表示=1、見開き表示=2）</returns>
    private int GetPageStep()
    {
        return CurrentDisplayMode == DisplayMode.Single ? 1 : 2;
    }

    /// <summary>
    /// 表示モードを設定します。
    /// </summary>
    /// <param name="mode">設定する表示モード</param>
    public void SetDisplayMode(DisplayMode mode)
    {
        CurrentDisplayMode = mode;
    }

    /// <summary>
    /// 表示モードを順次切り替えます。
    /// </summary>
    /// <returns>切り替え後の表示モード</returns>
    public DisplayMode ToggleDisplayMode()
    {
        CurrentDisplayMode = CurrentDisplayMode switch
        {
            DisplayMode.Single => DisplayMode.SpreadRTL,
            DisplayMode.SpreadRTL => DisplayMode.SpreadLTR,
            _ => DisplayMode.Single
        };
        return CurrentDisplayMode;
    }

    /// <summary>
    /// 現在の表示モードに基づいて、表示すべきページインデックスを計算します。
    /// </summary>
    /// <returns>左ページインデックスと右ページインデックスのタプル（null は表示なし）</returns>
    public (int? leftIndex, int? rightIndex) CalculatePageIndices()
    {
        if (TotalPageCount == 0) return (null, null);

        if (CurrentDisplayMode == DisplayMode.Single)
        {
            // 単一表示: 左側のみ使用
            return (CurrentPageIndex, null);
        }
        else
        {
            // 見開き表示
            int firstIdx = CurrentPageIndex;
            int secondIdx = CurrentPageIndex + 1;

            if (CurrentDisplayMode == DisplayMode.SpreadRTL)
            {
                // 右綴じ: 右に現在ページ、左に次ページ
                int? leftIdx = secondIdx < TotalPageCount ? secondIdx : (int?)null;
                int? rightIdx = firstIdx;
                return (leftIdx, rightIdx);
            }
            else
            {
                // 左綴じ: 左に現在ページ、右に次ページ
                int? leftIdx = firstIdx;
                int? rightIdx = secondIdx < TotalPageCount ? secondIdx : (int?)null;
                return (leftIdx, rightIdx);
            }
        }
    }

    /// <summary>
    /// 進行方向を考慮したプリフェッチ対象ページのインデックスリストを生成します。
    /// </summary>
    /// <param name="isForward">進行方向（true=前方）</param>
    /// <param name="prefetchCount">プリフェッチするページ数</param>
    /// <returns>プリフェッチ対象のページインデックスリスト</returns>
    public List<int> GetPrefetchIndices(bool isForward, int prefetchCount = 4)
    {
        var indices = new List<int>();

        if (isForward)
        {
            // 前方優先: 次の2,3ページ、後方1ページ、さらに次の4ページ
            indices.Add(CurrentPageIndex + 2);
            indices.Add(CurrentPageIndex + 3);
            indices.Add(CurrentPageIndex - 1);
            if (prefetchCount > 3)
                indices.Add(CurrentPageIndex + 4);
        }
        else
        {
            // 後方優先: 前の1,2ページ、前方2ページ、さらに前の3ページ
            indices.Add(CurrentPageIndex - 1);
            indices.Add(CurrentPageIndex - 2);
            indices.Add(CurrentPageIndex + 2);
            if (prefetchCount > 3)
                indices.Add(CurrentPageIndex - 3);
        }

        // 有効範囲内のインデックスのみを返す
        return indices.Where(idx => idx >= 0 && idx < TotalPageCount).ToList();
    }
}
