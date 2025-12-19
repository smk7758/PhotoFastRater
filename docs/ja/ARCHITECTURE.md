# アーキテクチャドキュメント

## 目次

1. [システム概要](#システム概要)
2. [アーキテクチャパターン](#アーキテクチャパターン)
3. [プロジェクト構成](#プロジェクト構成)
4. [レイヤー構成](#レイヤー構成)
5. [データフロー](#データフロー)
6. [主要コンポーネント](#主要コンポーネント)
7. [データベース設計](#データベース設計)
8. [UI/UX 設計](#uiux-設計)

---

## システム概要

Photo Fast Rater は、写真家やフォトグラファー向けの高速写真レーティングアプリケーションです。
大量の写真を効率的に評価・分類し、ベストショットを素早く選別することを目的としています。

### 主要機能

- **写真一覧表示**: グリッド表示とツリー表示（年 → 月 → 日 → フォルダ階層）の切り替え
- **高速レーティング**: キーボードショートカット（1-5 キー）による瞬時の評価
- **写真ナビゲーション**: 矢印キーによる素早い写真間移動
- **フォルダモード**: DB 登録前の一時的なフォルダ単位でのレーティングセッション
- **EXIF 情報抽出**: カメラモデル、レンズモデル、撮影日時、撮影設定などのメタデータ自動抽出
- **RAW+JPEG 対応**: RAW ファイルと JPEG ファイルのペアリング機能
- **サムネイルキャッシュ**: 高速表示のためのサムネイル自動生成・キャッシュ
- **SNS エクスポート**: Instagram、Twitter、Facebook 向けの最適化エクスポート機能
  - プラットフォーム別の最適サイズ調整
  - 枠（フレーム）の追加とカスタマイズ
  - EXIF 情報のオーバーレイ表示（位置・内容のカスタマイズ可能）
  - リアルタイムプレビュー
  - 元画像のメタデータ保持

---

## アーキテクチャパターン

### MVVM (Model-View-ViewModel)

Photo Fast Rater は WPF の標準パターンである MVVM アーキテクチャを採用しています。

```text
┌─────────────────────────────────────────────────────────┐
│                        View (XAML)                       │
│  - MainWindow.xaml                                      │
│  - FolderModeWindow.xaml                                │
│  - PhotoViewerWindow.xaml                               │
└────────────────┬────────────────────────────────────────┘
                 │ DataBinding
                 │ Commands
                 ▼
┌─────────────────────────────────────────────────────────┐
│                  ViewModel (C#)                          │
│  - PhotoGridViewModel                                    │
│  - FolderModeViewModel                                   │
│  - PhotoViewerViewModel                                  │
└────────────────┬────────────────────────────────────────┘
                 │ Business Logic
                 │ Data Access
                 ▼
┌─────────────────────────────────────────────────────────┐
│                    Model & Services                      │
│  - Photo, Event, Camera (Models)                        │
│  - PhotoRepository, EventRepository (Data Access)        │
│  - ExifService, ImportService (Business Logic)           │
└─────────────────────────────────────────────────────────┘
```

### 主要ライブラリ

- **CommunityToolkit.Mvvm**: `[ObservableProperty]`、`[RelayCommand]`などのソースジェネレーター
- **Entity Framework Core**: SQLite データベースへの ORM アクセス
- **MetadataExtractor**: EXIF 情報読み取り
- **ImageSharp**: 画像処理・サムネイル生成
- **MaterialDesignThemes**: マテリアルデザイン UI

---

## プロジェクト構成

```text
photo-fast-rater/
├── src/
│   ├── PhotoFastRater.Core/         # コアビジネスロジック
│   │   ├── Database/                # データベース関連
│   │   │   ├── PhotoDbContext.cs    # EF Core コンテキスト
│   │   │   └── Repositories/        # リポジトリパターン実装
│   │   ├── Models/                  # ドメインモデル
│   │   │   ├── Photo.cs
│   │   │   ├── Event.cs
│   │   │   ├── FolderSession.cs
│   │   │   └── ExportTemplate.cs
│   │   ├── Services/                # ビジネスロジック
│   │   │   ├── ExifService.cs
│   │   │   ├── ImportService.cs
│   │   │   ├── FolderSessionService.cs
│   │   │   └── DataMigrationService.cs
│   │   ├── Export/                  # エクスポート機能
│   │   │   ├── SocialMediaExporter.cs
│   │   │   ├── ExifOverlayRenderer.cs
│   │   │   └── FrameRenderer.cs
│   │   └── UI/                      # UI設定
│   │       ├── UIConfiguration.cs
│   │       └── CacheConfiguration.cs
│   │
│   ├── PhotoFastRater.UI/           # ユーザーインターフェース
│   │   ├── Views/                   # XAML Views
│   │   │   ├── MainWindow.xaml
│   │   │   ├── FolderModeWindow.xaml
│   │   │   └── PhotoViewerWindow.xaml
│   │   ├── ViewModels/              # ViewModels
│   │   │   ├── PhotoGridViewModel.cs
│   │   │   ├── FolderModeViewModel.cs
│   │   │   └── PhotoTreeNode.cs
│   │   ├── Services/                # UIサービス
│   │   │   └── ImageLoader.cs
│   │   └── App.xaml.cs              # アプリケーション起動
│   │
│   └── PhotoFastRater.Tests/        # ユニットテスト
│
├── docs/                            # ドキュメント
│   └── ja/                          # 日本語ドキュメント
│
└── appsettings.json                 # アプリケーション設定
```

---

## レイヤー構成

### 1. **Presentation Layer (UI 層)**

- **責務**: ユーザーインターフェースの表示とユーザー入力の処理
- **コンポーネント**: Views (XAML), ViewModels
- **技術**: WPF, XAML, Data Binding

### 2. **Business Logic Layer (ビジネスロジック層)**

- **責務**: アプリケーションのコアロジック、データ変換、検証
- **コンポーネント**: Services (ExifService, ImportService, etc.)
- **技術**: C# classes

### 3. **Data Access Layer (データアクセス層)**

- **責務**: データベースアクセス、CRUD 操作
- **コンポーネント**: Repositories, DbContext
- **技術**: Entity Framework Core

### 4. **Domain Layer (ドメイン層)**

- **責務**: ドメインモデル、ビジネスエンティティ
- **コンポーネント**: Models (Photo, Event, Camera, etc.)
- **技術**: Plain C# classes (POCOs)

---

## データフロー

### 写真インポートフロー

```text
ユーザー操作
    │
    ▼
[MainWindow] または [FolderModeWindow]
    │
    ▼
[ImportService.ImportFromFolderAsync()]
    │
    ├─► [ExifService.ExtractExifData()]  ← EXIF情報抽出
    │       │
    │       └─► MetadataExtractor ライブラリ
    │
    ├─► [PhotoRepository.AddAsync()]     ← データベース保存
    │       │
    │       └─► Entity Framework Core → SQLite
    │
    └─► [ImageLoader.LoadAsync()]        ← サムネイル生成
            │
            └─► ImageSharp → キャッシュ保存
```

### 写真表示フロー

```text
[PhotoGridViewModel.LoadPhotosAsync()]
    │
    ▼
[PhotoRepository.GetAllAsync()]
    │
    ▼
Entity Framework Core (クエリ実行)
    │
    ▼
SQLite Database (photos.db)
    │
    ▼
List<Photo> → ObservableCollection<PhotoViewModel>
    │
    ▼
[PhotoGridViewModel.BuildPhotoTree()] (ツリーモード時)
    │
    ▼
階層構造構築: Year → Month → Day → Folder
    │
    ▼
[MainWindow.xaml] DataBinding
    │
    ▼
画面表示 (グリッド or ツリー)
```

---

## 主要コンポーネント

### 1. **PhotoDbContext**

EF Core の DbContext クラス。データベーススキーマの定義とエンティティ設定を管理。

**主要責務**:

- テーブル定義 (Photos, Events, Cameras, etc.)
- インデックス設定 (DateTaken, Rating, CameraModel, FolderPath)
- リレーションシップ定義 (PhotoEventMappings)

### 2. **PhotoRepository**

写真エンティティのデータアクセスを抽象化するリポジトリパターン実装。

**主要メソッド**:

- `GetAllAsync()`: 全写真取得
- `GetByIdAsync(int id)`: ID 指定取得
- `AddAsync(Photo photo)`: 新規追加
- `UpdateAsync(Photo photo)`: 更新
- `DeleteAsync(int id)`: 削除
- `GetByFilePathAsync(string path)`: ファイルパス検索
- `GetByCameraAsync(string make, string model)`: カメラ別取得
- `GetByRatingAsync(int rating)`: レーティング別取得

### 3. **ExifService**

写真ファイルから EXIF 情報を抽出するサービス。

**主要メソッド**:

- `ExtractExifData(string filePath)`: EXIF 情報抽出
  - カメラメーカー/モデル
  - 撮影日時
  - 撮影設定 (ISO, 絞り, シャッタースピード, 焦点距離)
  - GPS 位置情報
  - 画像サイズ

### 4. **ImportService**

フォルダから写真をインポートするサービス。

**主要メソッド**:

- `ImportFromFolderAsync()`: フォルダ一括インポート
  - サブフォルダ対応
  - 除外パターン適用
  - 重複チェック
  - 進捗通知

### 5. **DataMigrationService**

データベーススキーマ変更時の既存データ移行サービス。

**主要メソッド**:

- `MigrateFolderPathsAsync()`: FolderPath/FolderName 移行
- `RunAllMigrationsAsync()`: 全マイグレーション実行

### 6. **PhotoGridViewModel**

写真一覧画面の ViewModel。

**主要機能**:

- 写真一覧の表示管理 (`ObservableCollection<PhotoViewModel>`)
- ツリービューの構築 (`BuildPhotoTree()`)
- フィルタリング (カメラ、レーティング、日付)
- ソート機能
- 矢印キーナビゲーション
- レーティング変更

### 7. **FolderModeViewModel**

フォルダモード画面の ViewModel。

**主要機能**:

- フォルダセッション管理
- 一時的なレーティング
- DB 未登録写真の処理
- セッション保存/読み込み
- DB へのエクスポート

### 8. **ImageLoader**

サムネイル画像の非同期読み込みサービス。

**主要機能**:

- 高速サムネイル生成
- キャッシュ管理
- バックグラウンド読み込み
- メモリ最適化

---

## データベース設計

### テーブル構成

#### **Photos** (写真テーブル)

```sql
CREATE TABLE Photos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FilePath TEXT NOT NULL,
    FileName TEXT NOT NULL,
    FolderPath TEXT NOT NULL,           -- 追加 (2025-12-16)
    FolderName TEXT NOT NULL,           -- 追加 (2025-12-16)
    FileSize INTEGER NOT NULL,
    DateTaken DATETIME NOT NULL,
    ImportDate DATETIME NOT NULL,
    ModifiedDate DATETIME,
    Rating INTEGER NOT NULL,
    IsFavorite BOOLEAN NOT NULL,
    IsRejected BOOLEAN NOT NULL,
    CameraModel TEXT,
    CameraMake TEXT,
    LensModel TEXT,
    Width INTEGER NOT NULL,
    Height INTEGER NOT NULL,
    Aperture REAL,
    ShutterSpeed TEXT,
    ISO INTEGER,
    FocalLength REAL,
    ExposureCompensation REAL,
    Latitude REAL,
    Longitude REAL,
    LocationName TEXT,
    ThumbnailCachePath TEXT,
    ThumbnailGeneratedDate DATETIME,
    FileHash TEXT
);

-- インデックス
CREATE INDEX IX_Photos_DateTaken ON Photos(DateTaken);
CREATE INDEX IX_Photos_Rating ON Photos(Rating);
CREATE INDEX IX_Photos_CameraModel ON Photos(CameraModel);
CREATE INDEX IX_Photos_FileHash ON Photos(FileHash);
CREATE INDEX IX_Photos_FolderPath ON Photos(FolderPath);  -- 追加
```

#### **Events** (イベントテーブル)

```sql
CREATE TABLE Events (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT,
    Type INTEGER NOT NULL,
    StartDate DATETIME,
    EndDate DATETIME,
    Location TEXT,
    Latitude REAL,
    Longitude REAL,
    CoverPhotoPath TEXT,
    PhotoCount INTEGER NOT NULL
);

CREATE INDEX IX_Events_StartDate ON Events(StartDate);
CREATE INDEX IX_Events_EndDate ON Events(EndDate);
```

#### **Cameras** (カメラテーブル)

```sql
CREATE TABLE Cameras (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Make TEXT NOT NULL,
    Model TEXT NOT NULL,
    PhotoCount INTEGER NOT NULL,
    UNIQUE(Make, Model)
);
```

#### **PhotoEventMappings** (写真-イベント関連テーブル)

```sql
CREATE TABLE PhotoEventMappings (
    PhotoId INTEGER NOT NULL,
    EventId INTEGER NOT NULL,
    AddedDate DATETIME NOT NULL,
    PRIMARY KEY (PhotoId, EventId),
    FOREIGN KEY (PhotoId) REFERENCES Photos(Id) ON DELETE CASCADE,
    FOREIGN KEY (EventId) REFERENCES Events(Id) ON DELETE CASCADE
);
```

### マイグレーション履歴

- **20251216163207_AddFolderPathAndName**: FolderPath/FolderName カラム追加
- **20251219133912_AddCustomPositionToExportTemplate**: ExportTemplates テーブルに CustomX/CustomY カラム追加（EXIF オーバーレイのカスタム位置用）

---

## UI/UX 設計

### ナビゲーションモード

ユーザーは 2 つの矢印キーナビゲーションモードから選択可能:

1. **GridFocus モード** (デフォルト)

   - グリッドにフォーカスがあれば常に矢印キーで移動可能
   - 写真が選択されていない場合は自動的に最初の写真を選択

2. **SelectionOnly モード**
   - 写真が明示的に選択されている場合のみ矢印キーで移動可能

設定場所: `appsettings.json` → `UI.ArrowKeyNavigationMode`

### 表示モード

#### グリッド表示

- WrapPanel によるグリッドレイアウト
- サムネイルサイズ: 256x256 (設定可能)
- 選択時の視覚的フィードバック: 青枠 + 水色背景

#### ツリー表示

- 階層構造: **年 → 月 → 日 → フォルダ → 写真**
- 各ノードに写真枚数を表示
- 展開/折りたたみ可能

### キーボードショートカット

- **矢印キー**: 写真間ナビゲーション
- **1-5 キー**: レーティング設定
- **0 キー**: レーティングクリア
- **Enter キー**: 写真ビューアーを開く
- **Escape/Q キー**: ウィンドウを閉じる

### ウィンドウ構成

#### MainWindow (メインウィンドウ)

- 写真一覧表示 (グリッド/ツリー切り替え)
- サイドパネル: フィルター、ソート、統計
- ツールバー: インポート、エクスポート、設定

#### FolderModeWindow (フォルダモード)

- フォルダ単位の一時セッション
- サイドパネル: 選択中の写真詳細
- セッション保存/DB エクスポート

#### PhotoViewerWindow (写真ビューアー)

- 大画面表示
- EXIF 情報パネル（カメラ、レンズ、撮影設定）
- 前後の写真へのナビゲーション
- エクスポート設定パネル
  - プラットフォーム選択（Instagram、Twitter、Facebook）
  - 枠の設定（表示/非表示、幅の調整）
  - EXIF オーバーレイ設定
    - 表示/非表示
    - 位置選択（左上、右上、左下、右下、カスタム）
    - カスタム位置の調整（スライダーまたはマウスドラッグ）
  - リアルタイムプレビュー
  - エクスポートボタン

---

## 設計原則

### 1. **関心の分離 (Separation of Concerns)**

各レイヤーが明確な責務を持ち、他レイヤーへの依存を最小化。

### 2. **依存性注入 (Dependency Injection)**

`Microsoft.Extensions.DependencyInjection`によるサービス管理。

### 3. **リポジトリパターン**

データアクセスロジックの抽象化により、テスタビリティと保守性を向上。

### 4. **非同期処理**

UI スレッドのブロックを避けるため、すべての I/O 操作は`async/await`で実装。

### 5. **SOLID 原則**

- Single Responsibility: 各クラスは単一の責務
- Open/Closed: 拡張に開いており、修正に閉じている
- Liskov Substitution: 基底クラスと派生クラスの置換可能性
- Interface Segregation: 必要最小限のインターフェース
- Dependency Inversion: 抽象への依存

---

## パフォーマンス最適化

### 1. **サムネイルキャッシュ**

- 初回生成後はキャッシュから読み込み
- ディスクキャッシュ: `%LOCALAPPDATA%/PhotoFastRater/thumbnails`

### 2. **バックグラウンド読み込み**

- サムネイルは`Task`による非同期読み込み
- UI スレッドをブロックしない

### 3. **データベースインデックス**

- 頻繁にクエリされるカラムにインデックス設定
- DateTaken, Rating, CameraModel, FolderPath

### 4. **遅延読み込み (Lazy Loading)**

- 写真一覧の初回表示時は基本情報のみ
- 詳細情報は必要時に読み込み

---

## セキュリティ考慮事項

### 1. **ファイルパスの検証**

- パストラバーサル攻撃の防止
- 不正なファイルパスの拒否

### 2. **SQL インジェクション対策**

- EF Core によるパラメータ化クエリ
- 直接 SQL 実行は最小限に

### 3. **データベースバックアップ**

- 定期的な自動バックアップ推奨
- `photos.db`ファイルの安全な保管

---

## 今後の拡張性

### 計画中の機能

- クラウド同期 (OneDrive, Google Drive)
- AI による自動タグ付け
- 顔認識機能
- RAW ファイルの直接プレビュー
- プラグインシステム

---

## 参考資料

- [EF Core Documentation](https://docs.microsoft.com/ef/core/)
- [WPF MVVM Pattern](https://docs.microsoft.com/dotnet/desktop/wpf/data/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet)
