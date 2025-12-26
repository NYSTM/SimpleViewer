namespace SimpleViewer.Models;

public enum DisplayMode { Single, SpreadLTR, SpreadRTL }

public class SpreadManager
{
    public (int LeftIndex, int RightIndex) GetPageIndices(int currentIndex, int totalPages, DisplayMode mode)
    {
        if (mode == DisplayMode.Single) return (currentIndex, -1);

        int first = currentIndex;
        int second = currentIndex + 1;
        if (second >= totalPages) second = -1;

        return mode == DisplayMode.SpreadRTL ? (second, first) : (first, second);
    }
}