namespace SimpleViewer.Models;

/// <summary>
/// アプリケーションの設定および前回の状態を保持します。
/// </summary>
public class AppSettings
{
    // stringではなく列挙型にすることで型安全にする
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Single;

    public string ZoomMode { get; set; } = "Manual";
    public double ZoomFactor { get; set; } = 1.0;
    public bool IsSidebarVisible { get; set; } = true;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
}