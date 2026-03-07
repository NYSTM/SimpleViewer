using SimpleViewer.Models;
using SimpleViewer.Presenters.Navigation;

namespace SimpleViewer.Tests.Presenters.Navigation;

public class PageNavigationManagerTests
{
    // ---- SetTotalPageCount ----

    [Fact]
    public void SetTotalPageCount_SetsPositiveValueToTotalPageCount()
    {
        // 正の値を設定すると TotalPageCount に反映される
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);

        Assert.Equal(10, mgr.TotalPageCount);
    }

    [Fact]
    public void SetTotalPageCount_ClampsNegativeValueToZero()
    {
        // 負の値は 0 にクランプされる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(-5);

        Assert.Equal(0, mgr.TotalPageCount);
        // TotalPageCount が 0 の場合、CurrentPageIndex は 0 のまま
        Assert.Equal(0, mgr.CurrentPageIndex);
    }

    [Fact]
    public void SetTotalPageCount_ClampsCurrentPageIndexWhenDecreasingPageCount()
    {
        // ページ数を減らすと CurrentPageIndex もクランプされる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(9);

        mgr.SetTotalPageCount(5);

        Assert.True(mgr.CurrentPageIndex <= 4);
    }

    // ---- Reset ----

    [Fact]
    public void Reset_ResetsCurrentPageIndexToZero()
    {
        // CurrentPageIndex が 0 に戻る
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(5);

        mgr.Reset();

        Assert.Equal(0, mgr.CurrentPageIndex);
    }

    [Fact]
    public void Reset_ResetsTotalPageCountToZero()
    {
        // TotalPageCount が 0 に戻る
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);

        mgr.Reset();

        Assert.Equal(0, mgr.TotalPageCount);
    }

    // ---- JumpToPage ----

    [Fact]
    public void JumpToPage_JumpsToValidIndex()
    {
        // 有効なインデックスへジャンプできる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);

        mgr.JumpToPage(7);

        Assert.Equal(7, mgr.CurrentPageIndex);
    }

    [Fact]
    public void JumpToPage_ClampsIndexAboveMaximumToLastPage()
    {
        // 上限を超えたインデックスは最終ページにクランプされる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);

        mgr.JumpToPage(100);

        Assert.Equal(4, mgr.CurrentPageIndex);
    }

    [Fact]
    public void JumpToPage_ClampsNegativeIndexToZero()
    {
        // 負のインデックスは 0 にクランプされる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);

        mgr.JumpToPage(-3);

        Assert.Equal(0, mgr.CurrentPageIndex);
    }

    [Fact]
    public void JumpToPage_ReturnsTrueForForwardNavigation()
    {
        // 前方への移動は true を返す
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(2);

        var result = mgr.JumpToPage(5);

        Assert.True(result);
    }

    [Fact]
    public void JumpToPage_ReturnsFalseForBackwardNavigation()
    {
        // 後方への移動は false を返す
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(5);

        var result = mgr.JumpToPage(2);

        Assert.False(result);
    }

    [Fact]
    public void JumpToPage_ReturnsTrueWhenTotalPageCountIsZero()
    {
        // 総ページ数 0 のときは true を返す
        var mgr = new PageNavigationManager();

        var result = mgr.JumpToPage(0);

        Assert.True(result);
    }

    // ---- MoveToNextPage / MoveToPreviousPage ----

    [Fact]
    public void MoveToNextPage_MovesOnePageForwardInSingleDisplayMode()
    {
        // 単一表示では 1 ページ進む
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.SetDisplayMode(DisplayMode.Single);

        mgr.MoveToNextPage();

        Assert.Equal(1, mgr.CurrentPageIndex);
    }

    [Fact]
    public void MoveToNextPage_MovesTwoPagesForwardInSpreadDisplayMode()
    {
        // 見開き表示では 2 ページ進む
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.SetDisplayMode(DisplayMode.SpreadRTL);

        mgr.MoveToNextPage();

        Assert.Equal(2, mgr.CurrentPageIndex);
    }

    [Fact]
    public void MoveToNextPage_DoesNotExceedLastPage()
    {
        // 最終ページを超えない
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(4);

        mgr.MoveToNextPage();

        Assert.Equal(4, mgr.CurrentPageIndex);
    }

    [Fact]
    public void MoveToPreviousPage_MovesOnePageBackwardInSingleDisplayMode()
    {
        // 単一表示では 1 ページ戻る
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(5);

        mgr.MoveToPreviousPage();

        Assert.Equal(4, mgr.CurrentPageIndex);
    }

    [Fact]
    public void MoveToPreviousPage_MovesTwoPagesBackwardInSpreadDisplayMode()
    {
        // 見開き表示では 2 ページ戻る
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.SetDisplayMode(DisplayMode.SpreadLTR);
        mgr.JumpToPage(6);

        mgr.MoveToPreviousPage();

        Assert.Equal(4, mgr.CurrentPageIndex);
    }

    [Fact]
    public void MoveToPreviousPage_DoesNotGoBelowFirstPage()
    {
        // 先頭ページを下回らない
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);

        mgr.MoveToPreviousPage();

        Assert.Equal(0, mgr.CurrentPageIndex);
    }

    // ---- ToggleDisplayMode ----

    [Fact]
    public void ToggleDisplayMode_ChangesFromSingleToSpreadRTL()
    {
        // Single から SpreadRTL へ切り替わる
        var mgr = new PageNavigationManager();
        Assert.Equal(DisplayMode.Single, mgr.CurrentDisplayMode);

        var mode = mgr.ToggleDisplayMode();

        Assert.Equal(DisplayMode.SpreadRTL, mode);
        Assert.Equal(DisplayMode.SpreadRTL, mgr.CurrentDisplayMode);
    }

    [Fact]
    public void ToggleDisplayMode_ChangesFromSpreadRTLToSpreadLTR()
    {
        // SpreadRTL から SpreadLTR へ切り替わる
        var mgr = new PageNavigationManager();
        mgr.SetDisplayMode(DisplayMode.SpreadRTL);

        var mode = mgr.ToggleDisplayMode();

        Assert.Equal(DisplayMode.SpreadLTR, mode);
    }

    [Fact]
    public void ToggleDisplayMode_ChangesFromSpreadLTRToSingle()
    {
        // SpreadLTR から Single へ切り替わる
        var mgr = new PageNavigationManager();
        mgr.SetDisplayMode(DisplayMode.SpreadLTR);

        var mode = mgr.ToggleDisplayMode();

        Assert.Equal(DisplayMode.Single, mode);
    }

    // ---- CalculatePageIndices ----

    [Fact]
    public void CalculatePageIndices_ReturnsBothNullWhenPageCountIsZero()
    {
        // ページ数 0 のとき両方 null
        var mgr = new PageNavigationManager();

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Null(left);
        Assert.Null(right);
    }

    [Fact]
    public void CalculatePageIndices_ReturnsLeftIndexOnlyInSingleDisplayMode()
    {
        // 単一表示では左に CurrentPageIndex、右は null
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(2);
        mgr.SetDisplayMode(DisplayMode.Single);

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Equal(2, left);
        Assert.Null(right);
    }

    [Fact]
    public void CalculatePageIndices_ReturnsCurrentPageOnRightAndNextPageOnLeftInSpreadRTL()
    {
        // SpreadRTL: 右に現在ページ、左に次ページ
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(2);
        mgr.SetDisplayMode(DisplayMode.SpreadRTL);

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Equal(3, left);  // 次ページが左
        Assert.Equal(2, right); // 現在ページが右
    }

    [Fact]
    public void CalculatePageIndices_ReturnsLeftNullOnLastPageInSpreadRTL()
    {
        // SpreadRTL: 最終ページなら左は null
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(4);
        mgr.SetDisplayMode(DisplayMode.SpreadRTL);

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Null(left);
        Assert.Equal(4, right);
    }

    [Fact]
    public void CalculatePageIndices_ReturnsCurrentPageOnLeftAndNextPageOnRightInSpreadLTR()
    {
        // SpreadLTR: 左に現在ページ、右に次ページ
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(2);
        mgr.SetDisplayMode(DisplayMode.SpreadLTR);

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Equal(2, left);
        Assert.Equal(3, right);
    }

    [Fact]
    public void CalculatePageIndices_ReturnsRightNullOnLastPageInSpreadLTR()
    {
        // SpreadLTR: 最終ページなら右は null
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(4);
        mgr.SetDisplayMode(DisplayMode.SpreadLTR);

        var (left, right) = mgr.CalculatePageIndices();

        Assert.Equal(4, left);
        Assert.Null(right);
    }

    // ---- GetPrefetchIndices ----

    [Fact]
    public void GetPrefetchIndices_IncludesForwardPagesWhenMovingForward()
    {
        // 前方移動では後続ページが含まれる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(3);

        var indices = mgr.GetPrefetchIndices(isForward: true);

        Assert.Contains(5, indices); // CurrentPageIndex + 2
    }

    [Fact]
    public void GetPrefetchIndices_IncludesBackwardPagesWhenMovingBackward()
    {
        // 後方移動では前のページが含まれる
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(10);
        mgr.JumpToPage(5);

        var indices = mgr.GetPrefetchIndices(isForward: false);

        Assert.Contains(4, indices); // CurrentPageIndex - 1
    }

    [Fact]
    public void GetPrefetchIndices_ExcludesOutOfRangeIndices()
    {
        // 範囲外インデックスは含まれない
        var mgr = new PageNavigationManager();
        mgr.SetTotalPageCount(5);
        mgr.JumpToPage(0);

        var indices = mgr.GetPrefetchIndices(isForward: false);

        Assert.All(indices, idx => Assert.True(idx >= 0 && idx < 5));
    }

    // ---- GetNewNavigationToken ----

    [Fact]
    public void GetNewNavigationToken_ReturnsNewTokenEachTime()
    {
        // 呼ぶたびに新しいトークンが返る
        var mgr = new PageNavigationManager();

        var token1 = mgr.GetNewNavigationToken();
        var token2 = mgr.GetNewNavigationToken();

        // 2回目の呼び出しで1回目のトークンはキャンセルされる
        Assert.True(token1.IsCancellationRequested);
        Assert.False(token2.IsCancellationRequested);
    }

    [Fact]
    public void CancelNavigation_CancelsToken()
    {
        // トークンがキャンセルされる
        var mgr = new PageNavigationManager();
        var token = mgr.GetNewNavigationToken();

        mgr.CancelNavigation();

        Assert.True(token.IsCancellationRequested);
    }
}
