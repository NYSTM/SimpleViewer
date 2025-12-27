using SimpleViewer.Models;
using System;
using System.IO;
using System.Text.Json;

namespace SimpleViewer.Utils
{
    public class SettingsManager
    {
        private readonly string _settingsFilePath;

        public SettingsManager(string baseDirectory)
        {
            _settingsFilePath = Path.Combine(baseDirectory, "settings.json");
        }

        /// <summary>
        /// 設定ファイルを読み込み、AppSettingsオブジェクトを返します。
        /// ファイルが存在しない、または読み込みに失敗した場合はデフォルト設定を返します。
        /// </summary>
        public AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return new AppSettings(); // ファイルが存在しない場合はデフォルト設定を返す
                }

                string json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings(); // デシリアライズに失敗した場合もデフォルト設定を返す
            }
            catch (Exception ex)
            {
                // エラーログなどを記録することも可能ですが、ここではシンプルにデフォルト設定を返します
                Console.WriteLine($"Failed to load settings: {ex.Message}");
                return new AppSettings();
            }
        }

        /// <summary>
        /// AppSettingsオブジェクトをファイルに保存します。
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, options));
            }
            catch (Exception ex)
            {
                // エラーログなどを記録することも可能ですが、ここでは何もしません
                Console.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
