using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleViewer.Models;

/// <summary>
/// アプリケーションの設定情報を保持し、永続化（保存・読み込み）を担当するクラス
/// </summary>
public class AppSettings
{
    // --- 永続化するプロパティ ---

    /// <summary> 表示モード（単一、見開きRTL、見開きLTR） </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))] // Enumを文字列で保存して可読性を上げる
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Single;

    /// <summary> ズームモード（Manual, FitWidth, FitPage） </summary>
    public string ZoomMode { get; set; } = "Manual";

    /// <summary> ズーム倍率 </summary>
    public double ZoomFactor { get; set; } = 1.0;

    /// <summary> サイドバーの表示状態 </summary>
    public bool IsSidebarVisible { get; set; } = true;

    /// <summary> ウィンドウの横幅 </summary>
    public double WindowWidth { get; set; } = 1200;

    /// <summary> ウィンドウの高さ </summary>
    public double WindowHeight { get; set; } = 800;

    /// <summary> 
    /// ウィンドウの表示状態 (0: Normal, 2: Maximized)
    /// System.Windows.WindowState とキャストして使用
    /// </summary>
    public int WindowState { get; set; } = 0;

    // --- 永続化ロジック ---

    // 設定ファイルの保存パス（実行ファイルと同じフォルダの settings.json）
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    /// <summary>
    /// settings.json から設定を読み込みます。
    /// ファイルが存在しない、または読み込みに失敗した場合はデフォルト値のインスタンスを返します。
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                // デシリアライズ（JSON文字列 -> オブジェクト）
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // 実際の開発ではここでログ出力等を行う
            System.Diagnostics.Debug.WriteLine($"設定の読み込みに失敗しました: {ex.Message}");
        }

        // ファイルがない、あるいは失敗した場合はデフォルト値を返す
        return new AppSettings();
    }

    /// <summary>
    /// 現在のインスタンスの状態を settings.json に保存します。
    /// </summary>
    public void Save()
    {
        try
        {
            // JSONの整形（インデント付与）オプション
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"設定の保存に失敗しました: {ex.Message}");
        }
    }
}