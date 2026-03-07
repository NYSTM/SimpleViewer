using Moq;
using SimpleViewer.Models.ImageSources;
using SimpleViewer.Services;

namespace SimpleViewer.Tests.Services;

public class CacheKeyGeneratorTests
{
    private static Mock<IImageSource> CreateSource(string identifier)
    {
        var mock = new Mock<IImageSource>();
        mock.Setup(s => s.SourceIdentifier).Returns(identifier);
        return mock;
    }

    [Fact]
    public void MakeCacheKey_ReturnsSameKeyForSameSourceAndIndex()
    {
        // 同じソースと同じインデックスは同じキーを返す
        var source = CreateSource(@"C:\images\test.zip");
        var key1 = CacheKeyGenerator.MakeCacheKey(source.Object, 0);
        var key2 = CacheKeyGenerator.MakeCacheKey(source.Object, 0);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void MakeCacheKey_ReturnsDifferentKeyForDifferentIndex()
    {
        // 異なるインデックスは異なるキーを返す
        var source = CreateSource(@"C:\images\test.zip");
        var key0 = CacheKeyGenerator.MakeCacheKey(source.Object, 0);
        var key1 = CacheKeyGenerator.MakeCacheKey(source.Object, 1);

        Assert.NotEqual(key0, key1);
    }

    [Fact]
    public void MakeCacheKey_ReturnsDifferentKeyForDifferentSource()
    {
        // 異なるソースは異なるキーを返す
        var source1 = CreateSource(@"C:\images\a.zip");
        var source2 = CreateSource(@"C:\images\b.zip");
        var key1 = CacheKeyGenerator.MakeCacheKey(source1.Object, 0);
        var key2 = CacheKeyGenerator.MakeCacheKey(source2.Object, 0);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void MakeCacheKey_Returns64CharacterHexString()
    {
        // 戻り値は 64 文字の 16 進数文字列
        var source = CreateSource(@"C:\images\test.zip");
        var key = CacheKeyGenerator.MakeCacheKey(source.Object, 0);

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9A-F]{64}$", key);
    }

    [Fact]
    public void MakeCacheKey_HandlesEmptySourceIdentifier()
    {
        // 空のソース識別子でも処理する
        var source = CreateSource(string.Empty);
        var key = CacheKeyGenerator.MakeCacheKey(source.Object, 0);

        Assert.NotNull(key);
        Assert.Equal(64, key.Length);
    }
}
