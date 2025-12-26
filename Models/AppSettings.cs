using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SimpleViewer.Models;

public class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Single;

    public string ZoomMode { get; set; } = "Manual";
    public double ZoomFactor { get; set; } = 1.0;
    public bool IsSidebarVisible { get; set; } = true;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public int WindowState { get; set; } = 0;

    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Save Settings Error: {ex.Message}");
        }
    }
}