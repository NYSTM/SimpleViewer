# SimpleViewer

.NET 8 / WPF ベースの画像ビューアソリューションです。  
ビューア本体の `SimpleViewer` と、Exif 関連処理を担う `SimpleExifLib` を中心に構成されています。

## ソリューション構成

- `SimpleViewer`
  - WPF ベースの画像ビューア本体
- `SimpleExifLib`
  - Exif 情報の読み取りや画像向き補正に関するライブラリ
- `SimpleViewer.Tests`
  - `SimpleViewer` 向けテスト
- `SimpleExifLib.Tests`
  - `SimpleExifLib` 向けテスト
- `SimpleExifLib.Benchmarks`
  - Exif 処理や関連処理のベンチマーク

## 主な機能

### `SimpleViewer`

- 画像ファイルの表示
- Exif Orientation に基づく画像の向き補正
- ズーム操作
  - ズームイン
  - ズームアウト
  - 実寸表示
  - ウィンドウ幅に合わせる
  - ウィンドウ全体に合わせる
- サイドバー表示
  - サムネイル一覧
  - ツリー表示
- カタログ一覧表示
- ドラッグ＆ドロップによるファイル読み込み

### `SimpleExifLib`

- Exif 情報の読み取り基盤
- 画像回転・向き補正に必要な情報の取得

## 使用技術

- .NET 8
- WPF (Windows Presentation Foundation)
- xUnit
- Moq
- BenchmarkDotNet
- SkiaSharp

## 開発環境

- .NET 8 SDK
- Visual Studio 2022 以降
- Windows 10 / 11

## ビルド

```bash
dotnet build
```

## テスト実行

```bash
dotnet test
```

## ベンチマーク実行

```bash
dotnet run -c Release --project SimpleExifLib.Benchmarks
```

## 免責事項

本ソフトウェアを使用した結果生じたいかなる損害についても、作者は責任を負いません。使用は自己責任で行ってください。

## ライセンス

このソリューション全体はルートの `LICENSE.txt` に記載された MIT License の下で提供されます。

各プロジェクトには、配布物やパッケージ同梱用として個別の `LICENSE.txt` を配置しています。

## 第三者ライセンス

第三者ライブラリの案内はルートの `THIRD_PARTY_LICENSES.md` を参照してください。