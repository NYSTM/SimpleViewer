using SimpleViewer.Models.Configuration;
using System.IO;
using System.Text.Json;

namespace SimpleViewer.Utils.Configuration
{
    /// <summary>
    /// 設定ファイル（settings.json）の読み書きを担当するユーティリティクラス。
    /// <para>
    /// - ファイル I/O とシリアライズ/デシリアライズの責務を持ち、
    ///   実際の適用や UI との連携は SettingsController が担当します。
    /// - 保存は一時ファイルを用いたアトミック置換を試みており、書き込み失敗時に既存ファイルを壊さないよう配慮しています。
    /// - 公開 API 自体はスレッドセーフを厳密に保証しません。UI スレッドや専用のワーカースレッドから呼び出すことを想定しています。
    /// </para>
    /// </summary>
    public class SettingsManager
    {
        private readonly string _settingsFilePath;

        /// <summary>
        /// 指定した baseDirectory 配下に settings.json を配置する SettingsManager を作成します。
        /// ディレクトリが存在しなければ作成します。
        /// </summary>
        /// <param name="baseDirectory">設定ファイルを格納する基底ディレクトリ</param>
        public SettingsManager(string baseDirectory)
        {
            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
            }
            _settingsFilePath = Path.Combine(baseDirectory, "settings.json");
        }

        /// <summary>
        /// 設定ファイルを読み込み、<see cref="AppSettings"/> オブジェクトを返します。
        /// <para>
        /// - ファイルが存在しない、または読み込み/デシリアライズに失敗した場合は既定値の AppSettings インスタンスを返します。
        /// - 呼び出し側で例外を扱いたい場合は本メソッドをラップして例外を流すように変更してください。
        /// </para>
        /// </summary>
        public AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    // ファイルが存在しない場合はデフォルト設定を返す
                    return new AppSettings();
                }

                // ファイル全体を読み取り、Json から復元する
                string json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                // デシリアライズに失敗した場合は既定値を返す
                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                // 読み込み失敗はデバッグログに記録し、安全のため既定設定を返す
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                return new AppSettings();
            }
        }

        /// <summary>
        /// 指定の <see cref="AppSettings"/> を settings.json に保存します。
        /// <para>
        /// 実装は以下の方針で安全に保存を行います:
        /// - 一時ファイル（settings.json.tmp）へ書き込む
        /// - 既存ファイルがあれば File.Replace を用いてアトミックに置換する
        /// - 例外発生時は既存の settings.json を上書きしないようにする
        /// </para>
        /// </summary>
        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var tempPath = _settingsFilePath + ".tmp";

                // 一時ファイルへ書き込み（失敗した場合は既存ファイルに影響を与えない）
                File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, options));

                // 既存設定ファイルが存在する場合はアトミックに置換
                if (File.Exists(_settingsFilePath))
                {
                    // 第3引数に null を渡すことでバックアップを作成しない
                    File.Replace(tempPath, _settingsFilePath, null);
                }
                else
                {
                    // 既存ファイルがなければ単純に移動
                    File.Move(tempPath, _settingsFilePath);
                }
            }
            catch (Exception ex)
            {
                // 保存失敗は致命的ではないためログに記録するのみ
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
            finally
            {
                // 万が一一時ファイルが残っていれば削除を試みる
                try { var tempPath = _settingsFilePath + ".tmp"; if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        /// <summary>
        /// 非同期で設定を保存します。内部で同期的な保存処理をバックグラウンドスレッドにオフロードします。
        /// UI スレッドのブロックを避ける用途で使用してください。
        /// </summary>
        /// <param name="settings">保存対象の設定オブジェクト</param>
        /// <returns>保存処理を表す Task</returns>
        public Task SaveSettingsAsync(AppSettings settings)
        {
            // SaveSettings はファイル I/O を行うため、Task.Run でバックグラウンドにオフロードする
            return Task.Run(() => SaveSettings(settings));
        }
    }
}
