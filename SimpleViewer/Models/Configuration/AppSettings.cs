using System.Text.Json.Serialization;

namespace SimpleViewer.Models.Configuration;

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

    /// <summary>
    /// サムネイルディスクキャッシュの上限サイズ（MB）。settings.json にここを設定することで
    /// キャッシュの最大容量を制御できます。既定は 1024 (1GB) とします。
    /// </summary>
    public int ThumbnailCacheMaxMB { get; set; } = 1024;

    /// <summary>
    /// ClearCache 呼び出し時にディスクキャッシュも同時に削除するかどうか。
    /// - false の場合、ClearCache はメモリキャッシュのみをクリアします（既定）。
    /// </summary>
    public bool ThumbnailClearDiskOnClear { get; set; } = false;

    /// <summary>
    /// ディスク上のサムネイル削除時にセキュア削除（内容を上書きしてから削除）を行うかどうか。
    /// - 完全な保証はできませんが、一般的な用途で復元を難しくするためのオプションです（既定は true）。
    /// </summary>
    public bool ThumbnailUseSecureDelete { get; set; } = true;

    /// <summary>
    /// EXIF Orientationタグの内容を画像表示に反映するかどうか。
    /// - true の場合、EXIF情報に基づいて画像を自動的に回転・反転します（既定）。
    /// - false の場合、EXIF Orientationを無視して元の画像データをそのまま表示します。
    /// </summary>
    public bool ApplyExifOrientation { get; set; } = true;
}