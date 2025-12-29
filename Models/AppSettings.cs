using System.Text.Json.Serialization;

namespace SimpleViewer.Models;

/// <summary>
/// アプリケーションのユーザー設定を表すデータクラス。
/// 実際の永続化は `Utils.SettingsManager` / `Utils.SettingsController` が担当するため、
/// このクラスはデータホルダとしての役割に限定します。
/// </summary>
public class AppSettings
{
    /// <summary>表示モード（単一表示 / 見開き）</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Single;

    /// <summary>ズームモードを示す文字列（既存実装互換）</summary>
    public string ZoomMode { get; set; } = "Manual";

    /// <summary>ズーム倍率（1.0 が 100%）</summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary>サイドバーの表示有無</summary>
    public bool IsSidebarVisible { get; set; } = true;

    /// <summary>ウィンドウ幅（ピクセル）</summary>
    public double WindowWidth { get; set; } = 1200;

    /// <summary>ウィンドウ高さ（ピクセル）</summary>
    public double WindowHeight { get; set; } = 800;

    /// <summary>ウィンドウ状態（0=通常, 1=最大化 等の符号化値）</summary>
    public int WindowState { get; set; } = 0;
}