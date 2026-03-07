# SimpleExifLib

軽量・高速なEXIF抽出ライブラリ（.NET 8）

## 使い方

実行時に `exiftags.json` を実行フォルダに配置することで、抽出するフィールドと追加タグを制御できます。

### 設定ファイル形式

JSON形式の設定ファイルをサポートします。

- 各フィールドの読み取り可否を `true` / `false` で指定します
- サポートされるフィールド: `CameraMake`, `CameraModel`, `DateTimeOriginal`, `Orientation`, `Width`, `Height`, `FNumber`, `ExposureTime`, `IsoSpeed`, `FocalLength`
- 追加タグは `AdditionalTags` 配列に指定します（10 進数または `0x` で始まる 16 進数）
- JSON のコメントは `//` でサポートされます

### 設定ファイル例: `exiftags.json`

```json
{
  // EXIF読み取り設定ファイル
  "CameraMake": true,
  "CameraModel": true,
  "DateTimeOriginal": true,
  "Orientation": true,
  "Width": false,
  "Height": false,
  "FNumber": false,
  "ExposureTime": true,
  "IsoSpeed": true,
  "FocalLength": true,
  
  // 追加で抽出したいEXIFタグ
  "AdditionalTags": [
    "0x9286",  // UserComment
    "0xA434"   // LensModel
  ]
}
```

## API

- `IExifReader.ReadAsync(Stream)` を使用してストリームから EXIF を取得します
- `ExifReaderFactory.ReadAsync(Stream)` を使うと適切なリーダーが選択されます（現状は内部実装で JPEG を処理します）。外部からはファクトリ経由で利用することを推奨します。
- ファイルパスを直接渡すオーバーロードも用意しています（非同期/同期両方）:
  - `ExifReaderFactory.ReadAsync(string)` / `ExifReaderFactory.Read(string)`

## どのメソッドを呼べばよいか（用途別ガイド）

以下は典型的な用途ごとに、呼び出すべきメソッドと最短のコード例を示したガイドです。

- 画像ストリームから可能な限り多くのEXIF情報を取得したい（汎用）

  - 呼ぶメソッド: `ExifReaderFactory.ReadAsync(Stream)` またはファイルパス版 `ExifReaderFactory.ReadAsync(string)`
  - 説明: 画像フォーマットに適したリーダーを内部で選択してEXIFを抽出します。
  - 例:
    `var exif = await ExifReaderFactory.ReadAsync(stream);`
    `var exif = await ExifReaderFactory.ReadAsync("path/to/image.jpg");`

- 単に画像の向き（Orientation）だけを高速に確認したい（サムネイル・表示回転用）

  - 呼ぶメソッド: `ExifReaderFactory.ReadOrientation(Stream)`（同期） または `ExifReaderFactory.ReadOrientationAsync(Stream)`（非同期）。ファイルパス版オーバーロードも利用可能です。
  - 説明: ストリームの先頭を読み取り、Orientation値(1-8)を返します。Factory経由での取得を推奨します。
  - 例:
    `int orientation = ExifReaderFactory.ReadOrientation(stream);`
    `int orientation = await ExifReaderFactory.ReadOrientationAsync("path/to/image.jpg");`

- exiftags.json（抽出するフィールドと追加タグ）をプログラムで読み取りたい

  - 呼ぶメソッド: `ExifSettings.Load(string? fileName = null)`
  - 説明: 実行フォルダの `exiftags.json` を読み込み、設定と追加タグIDリストを返します。
  - 例:
    `var cfg = ExifSettings.Load();`
    `// cfg.ReadOrientation などで個別フィールドの有無を確認`

- 追加タグ（AdditionalTags）を解析した結果（10進IDリスト）が欲しい

  - 呼ぶメソッド: 上記 `ExifSettings.Load()` を呼ぶと `AdditionalTagIds` に変換済みで入ります。
  - 注意: `AdditionalTagIds` は設定で指定した追加タグの「IDリスト」です。タグの「値」は EXIF を実際にパースした結果（`ExifData`）の `AdditionalTags` 辞書に格納されます。
  - 使用例:
    ```csharp
    var cfg = ExifSettings.Load();
    var ids = cfg.AdditionalTagIds; // 例: [37510, 42004]

    var exif = await ExifReaderFactory.ReadAsync(stream);
    if (exif != null)
    {
        foreach (var id in ids)
        {
            if (exif.AdditionalTags.TryGetValue(id, out var value))
            {
                // value は文字列表現（存在しない場合は null）
            }
        }
    }
    ```

## 文字列デコードとフォールバック

- ライブラリはタグ値のデコードで可能な限り UTF-8 をサポートします。
  - TIFF/IFD のタグ型 `ASCII` を読む際、バイト列に ASCII 範囲外のバイトが含まれる場合は UTF-8 としてデコードします。
  - TIFF パースに失敗した場合のフォールバック文字列検索でも UTF-8 デコードを使用します。
- タグ型が `BYTE` / `UNDEFINED` の場合はバイナリを 16 進表記で返します。

## フォーマット対応について

- 現状このライブラリは主に JPEG の EXIF を取り扱う実装です。
- 将来的に JPEG 以外（TIFF、RAW、HEIF など）への対応を行うかは未定です。これらのフォーマットを確実に扱う必要がある場合は、`MetadataExtractor` 等の成熟した外部ライブラリの利用を検討してください。

## 制約

- このライブラリは限定的な TIFF/IFD パーサーを実装しています。すべての EXIF バリエーションに対応するわけではありません
- 本格的な解析が必要な場合は `MetadataExtractor` 等の専門ライブラリを使用してください

## 免責事項

本ソフトウェアを使用した結果生じたいかなる損害についても、著者は責任を負いません。使用は自己責任で行ってください。

## ライセンス

SimpleExifLibのライセンスはプロジェクト内の `LICENSE` ファイルを参照してください。
