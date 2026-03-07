using SimpleViewer.Utils.Comparers;

namespace SimpleViewer.Tests.Utils.Comparers;

public class NaturalStringComparerTests
{
    private readonly NaturalStringComparer _comparer = new();

    [Fact]
    public void Compare_SortsFileNamesWithNumbersNaturally()
    {
        // 数値を含むファイル名を自然順でソートできる
        var files = new[] { "image10.jpg", "image2.jpg", "image1.jpg", "image20.jpg" };
        var sorted = files.OrderBy(f => f, _comparer).ToList();

        Assert.Equal("image1.jpg", sorted[0]);
        Assert.Equal("image2.jpg", sorted[1]);
        Assert.Equal("image10.jpg", sorted[2]);
        Assert.Equal("image20.jpg", sorted[3]);
    }

    [Fact]
    public void Compare_ReturnsZeroForIdenticalStrings()
    {
        // 同一文字列はゼロを返す
        var result = _comparer.Compare("abc.jpg", "abc.jpg");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Compare_ReturnsNegativeForAlphabeticallyEarlierString()
    {
        // アルファベット順で前の文字列は負の値を返す
        var result = _comparer.Compare("a.jpg", "b.jpg");
        Assert.True(result < 0);
    }

    [Fact]
    public void Compare_ReturnsPositiveForAlphabeticallyLaterString()
    {
        // アルファベット順で後の文字列は正の値を返す
        var result = _comparer.Compare("b.jpg", "a.jpg");
        Assert.True(result > 0);
    }

    [Theory]
    [InlineData(null, "a.jpg")]
    [InlineData("a.jpg", null)]
    [InlineData(null, null)]
    public void Compare_DoesNotThrowExceptionForNullValues(string? x, string? y)
    {
        // null 値でも例外をスローしない
        var ex = Record.Exception(() => _comparer.Compare(x, y));
        Assert.Null(ex);
    }

    [Fact]
    public void Compare_ComparesNullAsEqualToNull()
    {
        // null と null をゼロで比較する
        var result = _comparer.Compare(null, null);
        Assert.Equal(0, result);
    }

    [Fact]
    public void Compare_SortsNumericOnlyFileNamesCorrectly()
    {
        // 数字のみのファイル名を正しくソートできる
        var files = new[] { "003.jpg", "10.jpg", "1.jpg", "002.jpg" };
        var sorted = files.OrderBy(f => f, _comparer).ToList();

        Assert.Equal("1.jpg", sorted[0]);
        Assert.Equal("002.jpg", sorted[1]);
        Assert.Equal("003.jpg", sorted[2]);
        Assert.Equal("10.jpg", sorted[3]);
    }
}
