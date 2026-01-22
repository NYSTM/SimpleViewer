namespace SimpleExifLib
{
    /// <summary>
    /// EXIFデータのシンプルな格納クラス。
    /// アプリケーションで必要になる主要なEXIFフィールドを最小限提供する。
    /// 追加で設定ファイルで指定されたタグ値を保持する辞書を持つ。
    /// </summary>
    public class ExifData
    {
        /// <summary>
        /// カメラメーカー（例: "Canon", "Sony"）。存在しない場合は null。
        /// </summary>
        public string? CameraMake { get; set; }

        /// <summary>
        /// カメラモデル（例: "EOS 5D Mark IV"）。存在しない場合は null。
        /// </summary>
        public string? CameraModel { get; set; }

        /// <summary>
        /// 撮影日時（可能なら DateTime として解析）。存在しない場合は null。
        /// </summary>
        public DateTime? DateTimeOriginal { get; set; }

        /// <summary>
        /// 画像の向き（EXIF Orientation）。1 = 正位置など。存在しない場合は null。
        /// </summary>
        public int? Orientation { get; set; }

        /// <summary>
        /// 画像幅（ピクセル）。TIFF/IFD から取得できる場合に設定される。
        /// </summary>
        public int? Width { get; set; }

        /// <summary>
        /// 画像高さ（ピクセル）。TIFF/IFD から取得できる場合に設定される。
        /// </summary>
        public int? Height { get; set; }

        /// <summary>
        /// 絞り値（F値）。存在しない場合は null。
        /// </summary>
        public double? FNumber { get; set; }

        /// <summary>
        /// シャッタースピード（秒）。存在しない場合は null。
        /// </summary>
        public double? ExposureTime { get; set; }

        /// <summary>
        /// ISO 感度。存在しない場合は null。
        /// </summary>
        public int? ISOSpeed { get; set; }

        /// <summary>
        /// 焦点距離（ミリメートル）。存在しない場合は null。
        /// </summary>
        public double? FocalLength { get; set; }

        /// <summary>
        /// 設定ファイルで指定された追加のタグを格納する辞書。
        /// キーは EXIF タグ ID（10 進）、値は文字列表現。
        /// </summary>
        public IDictionary<int, string?> AdditionalTags { get; } = new Dictionary<int, string?>();
    }
}
