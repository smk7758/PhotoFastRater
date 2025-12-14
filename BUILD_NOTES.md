# Photo Fast Rater - ビルドノート

## ✅ 完成した機能

### コア機能 (PhotoFastRater.Core)

- ✅ **データモデル**: Photo, Event, ExportTemplate, Camera, Lens
- ✅ **キャッシュシステム**: LRU キャッシュ、3 段階キャッシュ戦略
- ✅ **データベース**: SQLite + EF Core、自動マイグレーション
- ✅ **画像処理**: JPEG サムネイル生成
- ✅ **EXIF 読み取り**: MetadataExtractor による完全対応
- ✅ **エクスポート機能**: 枠追加、EXIF オーバーレイ、SNS 用プリセット

### UI 機能 (PhotoFastRater.UI)

- ✅ **ViewModels**: MVVM パターン実装
- ✅ **MainWindow**: タブベース UI
- ✅ **写真グリッド**: 仮想化リスト対応
- ✅ **イベント管理**: 自動グルーピング、手動作成
- ✅ **設定画面**: キャッシュ設定、SSD パス指定

## ビルド方法

```bash
cd c:\Programming\photo-fast-rater
dotnet build
```

## 実行方法

```bash
dotnet run --project src/PhotoFastRater.UI
```

## 今後の実装予定

### 優先度: 高

- [ ] RAW 対応 (LibRaw 統合)
- [ ] レーティング機能の完全実装
- [ ] フォルダインポート UI の完成
- [ ] 画像プレビュー機能

### 優先度: 中

- [ ] 検索機能
- [ ] フィルタリング UI
- [ ] キーボードショートカット
- [ ] バッチエクスポート

### 優先度: 低

- [ ] テーマ切り替え
- [ ] 多言語対応
- [ ] プラグインシステム

## 既知の問題

1. **ImageSharp 脆弱性警告**: バージョン 3.1.6 に既知の脆弱性がありますが、開発段階では問題ありません。本番リリース時には最新版に更新してください。

2. **FolderBrowserDialog**: Windows Forms を使用しています。将来的には WPF ネイティブのフォルダ選択ダイアログに置き換えることを推奨します。

3. **RAW 対応**: 現在は JPEG/PNG のみ対応。RAW ファイルサポートは今後実装予定です。

## アーキテクチャ概要

```
┌─────────────────────────────────────────┐
│         PhotoFastRater.UI (WPF)         │
│  ┌──────────┐  ┌───────────────────┐   │
│  │ViewModels│─▶│Views (XAML)       │   │
│  └────┬─────┘  └───────────────────┘   │
└───────┼─────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────┐
│        PhotoFastRater.Core              │
│  ┌──────────┐  ┌────────────────────┐  │
│  │ Services │  │ Cache (LRU)        │  │
│  ├──────────┤  ├────────────────────┤  │
│  │ Database │  │ ImageProcessing    │  │
│  ├──────────┤  ├────────────────────┤  │
│  │ Export   │  │ Models             │  │
│  └──────────┘  └────────────────────┘  │
└─────────────────────────────────────────┘
        │
        ▼
┌─────────────────────────────────────────┐
│   データ層                              │
│  SQLite Database    +    SSD Cache      │
└─────────────────────────────────────────┘
```

## パフォーマンス最適化メモ

### 実装済み

- ✅ LRU メモリキャッシュ (500MB)
- ✅ SSD ディスクキャッシュ
- ✅ 非同期画像読み込み
- ✅ 並列サムネイル生成 (最大 4 並列)
- ✅ ファイル変更検出 (ハッシュ比較)

### 今後の最適化案

- [ ] プリフェッチ機能の完全実装
- [ ] UI 仮想化の改善
- [ ] GPU アクセラレーション
- [ ] マルチスレッドインデックス作成

## 依存関係

### 主要ライブラリ

- .NET 10.0
- WPF (Windows Presentation Foundation)
- Entity Framework Core 8.0
- SQLite
- SixLabors.ImageSharp 3.1.6
- MetadataExtractor 2.8.1
- MaterialDesignThemes 5.0.0
- CommunityToolkit.Mvvm 8.2.2

## データベーススキーマ

```sql
Photos
  - Id (PK)
  - FilePath, FileName, FileSize
  - DateTaken, ImportDate
  - Rating, IsFavorite
  - CameraModel, LensModel
  - EXIF情報 (Aperture, ISO, ShutterSpeed等)
  - GPS情報 (Latitude, Longitude)
  - キャッシュ情報

Events
  - Id (PK)
  - Name, Description, Type
  - StartDate, EndDate
  - Location, GPS
  - PhotoCount

PhotoEventMappings
  - PhotoId (FK)
  - EventId (FK)
  - AddedDate

ExportTemplates
  - Id (PK)
  - Name
  - 出力設定 (Width, Height)
  - 枠設定
  - EXIFオーバーレイ設定
```

## 開発者向け TIPS

1. **デバッグ**: Visual Studio 2022 または Visual Studio Code を推奨
2. **データベース確認**: DB Browser for SQLite で`photos.db`を開く
3. **キャッシュクリア**: 設定画面からまたは手動で`{LocalAppData}\PhotoFastRater\Cache`を削除
4. **ログ**: 現在はコンソール出力のみ、今後ファイルロギング実装予定

## ビルド成功を確認

```
✅ PhotoFastRater.Core.dll
✅ PhotoFastRater.UI.exe
✅ PhotoFastRater.Tests.dll
```

すべてのプロジェクトがエラーなしでビルドされました！
