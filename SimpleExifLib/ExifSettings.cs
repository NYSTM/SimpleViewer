using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleExifLib
{
    /// <summary>
    /// EXIF 読み取り設定を表すクラス。
    /// 設定ファイルで各種フィールドの読み取り可否を制御し、追加で抽出するタグIDのリストを保持する。
    /// 設定ファイル名の既定値は "exiftags.json"（JSON形式のみサポート）。
    /// </summary>
    public class ExifSettings
    {
        /// <summary>カメラメーカーを読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("CameraMake")]
        public bool ReadCameraMake { get; set; } = true;

        /// <summary>カメラモデルを読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("CameraModel")]
        public bool ReadCameraModel { get; set; } = true;

        /// <summary>撮影日時を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("DateTimeOriginal")]
        public bool ReadDateTimeOriginal { get; set; } = true;

        /// <summary>Orientation を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("Orientation")]
        public bool ReadOrientation { get; set; } = true;

        /// <summary>Width を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("Width")]
        public bool ReadWidth { get; set; } = true;

        /// <summary>Height を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("Height")]
        public bool ReadHeight { get; set; } = true;

        /// <summary>FNumber を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("FNumber")]
        public bool ReadFNumber { get; set; } = true;

        /// <summary>ExposureTime を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("ExposureTime")]
        public bool ReadExposureTime { get; set; } = true;

        /// <summary>ISOSpeed を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("IsoSpeed")]
        public bool ReadISOSpeed { get; set; } = true;

        /// <summary>FocalLength を読み取るかどうか。既定値 true。</summary>
        [JsonPropertyName("FocalLength")]
        public bool ReadFocalLength { get; set; } = true;

        /// <summary>追加で抽出するタグの ID リスト（10 進数または16進数文字列）。</summary>
        [JsonPropertyName("AdditionalTags")]
        public IList<string> AdditionalTagStrings { get; set; } = new List<string>();

        /// <summary>追加で抽出するタグ ID リスト（10 進）。内部処理用。</summary>
        [JsonIgnore]
        public IList<int> AdditionalTagIds { get; } = new List<int>();

        /// <summary>
        /// 設定ファイルから読み込み、ExifSettings を返す。
        /// ファイルが存在しない場合は既定値の設定を返す。
        /// JSON形式のみサポート。
        /// </summary>
        public static ExifSettings Load(string? fileName = null)
        {
            var settings = new ExifSettings();
            try
            {
                var exeDir = AppContext.BaseDirectory ?? string.Empty;
                var name = string.IsNullOrEmpty(fileName) ? "exiftags.json" : fileName;
                var path = Path.Combine(exeDir, name);
                
                if (!File.Exists(path)) return settings;

                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<ExifSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (loaded != null)
                {
                    settings = loaded;
                    ParseAdditionalTags(settings);
                }
            }
            catch
            {
                // 設定読み込み失敗時は既定値を使用
            }

            return settings;
        }

        /// <summary>
        /// AdditionalTagStrings を解析して AdditionalTagIds に変換する。
        /// </summary>
        private static void ParseAdditionalTags(ExifSettings settings)
        {
            settings.AdditionalTagIds.Clear();
            foreach (var tagStr in settings.AdditionalTagStrings)
            {
                if (string.IsNullOrWhiteSpace(tagStr)) continue;

                var trimmed = tagStr.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, 
                        System.Globalization.CultureInfo.InvariantCulture, out var hexId))
                    {
                        settings.AdditionalTagIds.Add(hexId);
                    }
                }
                else
                {
                    if (int.TryParse(trimmed, System.Globalization.NumberStyles.Integer, 
                        System.Globalization.CultureInfo.InvariantCulture, out var decId))
                    {
                        settings.AdditionalTagIds.Add(decId);
                    }
                }
            }
        }
    }
}
