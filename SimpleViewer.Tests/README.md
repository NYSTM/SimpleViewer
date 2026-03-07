# SimpleViewer.Tests

SimpleViewer アプリケーションのユニットテストプロジェクトです。

## 概要

このプロジェクトは、SimpleViewer の主要コンポーネントの動作を検証するユニットテストを提供します。
xUnit フレームワークを使用し、MVP パターンに基づいた実装の品質と信頼性を保証します。

## テスト対象コンポーネント

### 1. ナビゲーション (`Presenters/Navigation`)

**PageNavigationManagerTests** (38 テスト)
- ページ遷移ロジックの検証
- 単一表示・見開き表示（左綴じ/右綴じ）のモード切り替え
- ページインデックスの計算と境界値チェック
- プリフェッチ対象ページの決定ロジック
- ナビゲーションのキャンセル処理

### 2. 画像ソース管理 (`Models/ImageSources`)

**ImageSourceFactoryTests** (10 テスト)
- ファイル形式に応じた適切な ImageSource 実装の生成
- フォルダ、ZIP/CBZ アーカイブ、静的画像ファイルの振り分け
- 未対応フォーマットのエラーハンドリング
- 引数バリデーション

**ImageSourceBaseTests** (9 テスト)
- サポート対象画像拡張子の判定
- FrozenSet を使用した高速な拡張子検索の検証
- null/空白パスのエッジケース処理

### 3. 設定管理 (`Utils/Configuration`)

**SettingsManagerTests** (11 テスト)
- JSON 設定ファイルの読み込み・保存
- デフォルト設定のフォールバック
- アトミックなファイル置換による安全な保存
- シリアライゼーションの往復テスト（全プロパティの整合性確認）
- 非同期保存処理

### 4. キャッシュ管理 (`Services`)

**DiskCacheManagerTests** (13 テスト)
- ソースごとのサブフォルダ管理
- キャッシュファイルの作成・削除
- ソース切り替え時の自動クリーンアップ
- セキュア削除オプション

**CacheKeyGeneratorTests** (5 テスト)
- SHA256 ハッシュを使用した一意なキャッシュキー生成
- ソース識別子とインデックスからの決定的なキー生成
- 衝突回避の検証

**BitmapFileHandlerTests** (9 テスト)
- ビットマップのファイル入出力
- デコーダの抽象化サポート
- アトミックなファイル保存（一時ファイル経由）
- エラーハンドリング

### 5. ユーティリティ (`Utils`)

**NaturalStringComparerTests** (7 テスト)
- Windows API (`StrCmpLogicalW`) を使用した自然順ソート
- 数値を含むファイル名の正しい順序付け（例: image1.jpg → image2.jpg → image10.jpg）
- null 値の安全な処理

## 技術スタック

- **.NET 8** (WPF アプリケーション)
- **xUnit 2.9.3** - テストフレームワーク
- **Moq 4.20.72** - モックライブラリ
- **Microsoft.NET.Test.Sdk 18.0.1** - テストランナー

## テスト実行

### すべてのテストを実行
```powershell
dotnet test SimpleViewer.Tests\SimpleViewer.Tests.csproj
```

### 詳細な出力で実行
```powershell
dotnet test SimpleViewer.Tests\SimpleViewer.Tests.csproj --verbosity detailed
```

### 特定のテストクラスのみ実行
```powershell
dotnet test --filter "FullyQualifiedName~PageNavigationManagerTests"
```

## テスト結果

? **全 102 件のテストが成功**

| カテゴリ | テスト数 |
|----------|----------|
| ナビゲーション | 38 |
| 画像ソース管理 | 19 |
| 設定管理 | 11 |
| キャッシュ管理 | 27 |
| ユーティリティ | 7 |

## コーディング規約

- **テストメソッド名**: 英語を使用し、動作と期待結果を明示
  - 形式: `MethodName_Condition_ExpectedResult()`
  - 例: `SetTotalPageCount_ClampsNegativeValueToZero()`
  - テスト内容の詳細は、メソッド冒頭の日本語コメントで説明

- **AAA パターン**: Arrange（準備）、Act（実行）、Assert（検証）の構造

- **クリーンアップ**: `IDisposable` または `IAsyncDisposable` を実装し、テスト後のリソース解放を保証

- **エッジケース**: null、空文字列、境界値を必ずテスト

## 依存関係

このテストプロジェクトは以下に依存します：

```xml
<ProjectReference Include="..\SimpleViewer\SimpleViewer.csproj" />
```

## 貢献

新しい機能を追加する際は、対応するユニットテストも作成してください。
テストカバレッジを維持し、リグレッションを防ぐことが重要です。

## ライセンス

SimpleViewer プロジェクトと同じライセンスが適用されます。
