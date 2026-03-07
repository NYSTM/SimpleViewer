using SimpleViewer.Models;
using SimpleViewer.Models.Configuration;
using SimpleViewer.Utils.Configuration;
using System.IO;
using System.Text.Json;

namespace SimpleViewer.Tests.Utils.Configuration;

public class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsManager _manager;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SettingsManagerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = new SettingsManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---- コンストラクタ ----

    [Fact]
    public void Constructor_CreatesDirectoryAutomaticallyIfNotExists()
    {
        // 存在しないディレクトリを渡すと自動で作成される
        var newDir = Path.Combine(_tempDir, "autocreated");

        var mgr = new SettingsManager(newDir);

        Assert.True(Directory.Exists(newDir));
    }

    // ---- LoadSettings ----

    [Fact]
    public void LoadSettings_ReturnsDefaultSettingsWhenFileDoesNotExist()
    {
        // 設定ファイルが存在しない場合はデフォルト設定を返す
        var settings = _manager.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(DisplayMode.Single, settings.DisplayMode);
        Assert.Equal(1.0, settings.ZoomFactor);
        Assert.True(settings.IsSidebarVisible);
    }

    [Fact]
    public void LoadSettings_ReturnsSavedSettings()
    {
        // 保存した設定を読み込むと同じ値が返る
        var saved = new AppSettings
        {
            DisplayMode = DisplayMode.SpreadRTL,
            ZoomFactor = 1.5,
            IsSidebarVisible = false,
            WindowWidth = 1920,
            WindowHeight = 1080
        };
        _manager.SaveSettings(saved);

        var loaded = _manager.LoadSettings();

        Assert.Equal(DisplayMode.SpreadRTL, loaded.DisplayMode);
        Assert.Equal(1.5, loaded.ZoomFactor);
        Assert.False(loaded.IsSidebarVisible);
        Assert.Equal(1920, loaded.WindowWidth);
        Assert.Equal(1080, loaded.WindowHeight);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaultSettingsForInvalidJson()
    {
        // 不正な JSON ファイルの場合はデフォルト設定を返す
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json {{{{");

        var settings = _manager.LoadSettings();

        Assert.NotNull(settings);
        Assert.Equal(DisplayMode.Single, settings.DisplayMode);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaultSettingsForEmptyJsonObject()
    {
        // 空の JSON オブジェクトの場合はデフォルト設定を返す
        var settingsPath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(settingsPath, "{}");

        var settings = _manager.LoadSettings();

        Assert.NotNull(settings);
    }

    // ---- SaveSettings ----

    [Fact]
    public void SaveSettings_CreatesSettingsJsonFile()
    {
        // settings.json ファイルが作成される
        _manager.SaveSettings(new AppSettings());

        var settingsPath = Path.Combine(_tempDir, "settings.json");
        Assert.True(File.Exists(settingsPath));
    }

    [Fact]
    public void SaveSettings_WritesValidJson()
    {
        // 有効な JSON が書き込まれる
        _manager.SaveSettings(new AppSettings { ZoomFactor = 2.0 });

        var settingsPath = Path.Combine(_tempDir, "settings.json");
        var json = File.ReadAllText(settingsPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ZoomFactor", out var zoomEl));
        Assert.Equal(2.0, zoomEl.GetDouble());
    }

    [Fact]
    public void SaveSettings_OverwritesExistingFile()
    {
        // 既存ファイルを上書きできる
        _manager.SaveSettings(new AppSettings { ZoomFactor = 1.0 });
        _manager.SaveSettings(new AppSettings { ZoomFactor = 3.0 });

        var loaded = _manager.LoadSettings();
        Assert.Equal(3.0, loaded.ZoomFactor);
    }

    [Fact]
    public void SaveSettings_RoundTripsAllProperties()
    {
        // 全プロパティが往復シリアライズされる
        var original = new AppSettings
        {
            DisplayMode = DisplayMode.SpreadLTR,
            ZoomMode = "FitWidth",
            ZoomFactor = 0.75,
            IsSidebarVisible = false,
            WindowWidth = 800,
            WindowHeight = 600,
            WindowState = 1,
            ThumbnailCacheMaxMB = 512,
            ThumbnailClearDiskOnClear = true,
            ThumbnailUseSecureDelete = false,
            ApplyExifOrientation = false
        };
        _manager.SaveSettings(original);

        var loaded = _manager.LoadSettings();

        Assert.Equal(original.DisplayMode, loaded.DisplayMode);
        Assert.Equal(original.ZoomMode, loaded.ZoomMode);
        Assert.Equal(original.ZoomFactor, loaded.ZoomFactor);
        Assert.Equal(original.IsSidebarVisible, loaded.IsSidebarVisible);
        Assert.Equal(original.WindowWidth, loaded.WindowWidth);
        Assert.Equal(original.WindowHeight, loaded.WindowHeight);
        Assert.Equal(original.WindowState, loaded.WindowState);
        Assert.Equal(original.ThumbnailCacheMaxMB, loaded.ThumbnailCacheMaxMB);
        Assert.Equal(original.ThumbnailClearDiskOnClear, loaded.ThumbnailClearDiskOnClear);
        Assert.Equal(original.ThumbnailUseSecureDelete, loaded.ThumbnailUseSecureDelete);
        Assert.Equal(original.ApplyExifOrientation, loaded.ApplyExifOrientation);
    }

    // ---- SaveSettingsAsync ----

    [Fact]
    public async Task SaveSettingsAsync_SavesAndLoadsSettings()
    {
        // 非同期で保存した設定を読み込める
        var saved = new AppSettings { ZoomFactor = 2.5 };

        await _manager.SaveSettingsAsync(saved);

        var loaded = _manager.LoadSettings();
        Assert.Equal(2.5, loaded.ZoomFactor);
    }
}
