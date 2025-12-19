# API リファレンス

## 目次

1. [Core Layer](#core-layer)
   - [Models](#models)
   - [Services](#services)
   - [Repositories](#repositories)
2. [UI Layer](#ui-layer)
   - [ViewModels](#viewmodels)
   - [Services](#ui-services)
3. [Configuration](#configuration)

---

## Core Layer

### Models

#### Photo

写真の完全なメタデータを保持するドメインモデル。

```csharp
namespace PhotoFastRater.Core.Models;

public class Photo
{
    // 基本情報
    public int Id { get; set; }
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public string FolderPath { get; set; }        // フォルダパス
    public string FolderName { get; set; }        // フォルダ名
    public long FileSize { get; set; }
    public DateTime DateTaken { get; set; }
    public DateTime ImportDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // レーティング情報
    public int Rating { get; set; }               // 0-5
    public bool IsFavorite { get; set; }
    public bool IsRejected { get; set; }

    // カメラ・レンズ情報
    public string? CameraModel { get; set; }
    public string? CameraMake { get; set; }
    public string? LensModel { get; set; }

    // 画像情報
    public int Width { get; set; }
    public int Height { get; set; }

    // EXIF撮影設定
    public double? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? ISO { get; set; }
    public double? FocalLength { get; set; }
    public double? ExposureCompensation { get; set; }

    // 位置情報
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }

    // キャッシュ情報
    public string? ThumbnailCachePath { get; set; }
    public DateTime? ThumbnailGeneratedDate { get; set; }
    public string? FileHash { get; set; }
}
```

**使用例**:

```csharp
var photo = new Photo
{
    FilePath = @"C:\Photos\IMG_0001.jpg",
    FileName = "IMG_0001.jpg",
    FolderPath = @"C:\Photos",
    FolderName = "Photos",
    DateTaken = DateTime.Now,
    Rating = 5,
    IsFavorite = true
};
```

---

#### Event

イベント（撮影場所・日時でグループ化）を表すモデル。

```csharp
namespace PhotoFastRater.Core.Models;

public class Event
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public EventType Type { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? CoverPhotoPath { get; set; }
    public int PhotoCount { get; set; }

    // ナビゲーションプロパティ
    public ICollection<PhotoEventMapping> PhotoEventMappings { get; set; }
}

public enum EventType
{
    Auto = 0,      // 自動生成
    Manual = 1,    // 手動作成
    Trip = 2,      // 旅行
    Event = 3      // イベント
}
```

---

#### FolderSession

フォルダモード用のセッション情報。

```csharp
namespace PhotoFastRater.Core.Models;

public class FolderSession
{
    public Guid SessionId { get; set; }
    public string FolderPath { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? LastModifiedDate { get; set; }
    public List<FolderSessionPhoto> Photos { get; set; }

    // 計算プロパティ
    public int TotalPhotos => Photos.Count;
    public int RatedPhotos => Photos.Count(p => p.Rating > 0);
}
```

---

#### FolderSessionPhoto

フォルダモード用の写真情報（DB 非依存）。

```csharp
namespace PhotoFastRater.Core.Models;

public class FolderSessionPhoto
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public DateTime DateTaken { get; set; }
    public int Rating { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsRejected { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }        // 追加 (2025-12-19)
    public double? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? ISO { get; set; }
    public double? FocalLength { get; set; }
    public string? ThumbnailCachePath { get; set; }
    public string? PairedFilePath { get; set; }
    public bool IsRawFile { get; set; }
    public bool HasPair => !string.IsNullOrEmpty(PairedFilePath);
}
```

---

#### ExportTemplate

SNS エクスポート用のテンプレート設定モデル。

```csharp
namespace PhotoFastRater.Core.Models;

public class ExportTemplate
{
    public int Id { get; set; }
    public string Name { get; set; }

    // 出力サイズ
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public bool MaintainAspectRatio { get; set; } = true;

    // 枠設定
    public bool EnableFrame { get; set; }
    public int FrameWidth { get; set; }
    public string FrameColor { get; set; } = "#FFFFFF";

    // EXIF オーバーレイ設定
    public bool EnableExifOverlay { get; set; }
    public ExifOverlayPosition Position { get; set; }
    public int CustomX { get; set; } = 50;  // カスタム位置のX座標（パーセント: 0-100）
    public int CustomY { get; set; } = 50;  // カスタム位置のY座標（パーセント: 0-100）
    public string DisplayFields { get; set; } = string.Empty; // JSON serialized
    public string FontFamily { get; set; } = "Arial";
    public int FontSize { get; set; } = 14;
    public string TextColor { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#000000";
    public int BackgroundOpacity { get; set; } = 70;

    // SNS 設定
    public SocialMediaPlatform TargetPlatform { get; set; }
}

public enum ExifOverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Custom
}

public enum ExifField
{
    CameraModel,
    LensModel,
    FocalLength,
    Aperture,
    ShutterSpeed,
    ISO,
    DateTaken,
    Location
}

public enum SocialMediaPlatform
{
    Instagram,  // 1080 × 1080 px
    Twitter,    // 1200 × 675 px
    Facebook,   // 1200 × 630 px
    Custom
}
```

---

### Services

#### ExifService

EXIF 情報の抽出サービス。

```csharp
namespace PhotoFastRater.Core.Services;

public class ExifService
{
    /// <summary>
    /// 写真ファイルからEXIF情報を抽出してPhotoオブジェクトを生成
    /// </summary>
    /// <param name="filePath">写真ファイルの絶対パス</param>
    /// <returns>EXIF情報が設定されたPhotoオブジェクト</returns>
    public Photo ExtractExifData(string filePath);
}
```

**使用例**:

```csharp
var exifService = new ExifService();
var photo = exifService.ExtractExifData(@"C:\Photos\IMG_0001.jpg");

Console.WriteLine($"Camera: {photo.CameraModel}");
Console.WriteLine($"Date: {photo.DateTaken}");
Console.WriteLine($"ISO: {photo.ISO}");
```

**抽出される情報**:

- カメラメーカー (`CameraMake`)
- カメラモデル (`CameraModel`)
- レンズモデル (`LensModel`)
- 撮影日時 (`DateTaken`)
- ISO 感度 (`ISO`)
- 絞り値 (`Aperture`)
- シャッタースピード (`ShutterSpeed`)
- 焦点距離 (`FocalLength`)
- 露出補正 (`ExposureCompensation`)
- GPS 位置情報 (`Latitude`, `Longitude`)
- 画像サイズ (`Width`, `Height`)
- ファイル情報 (`FileSize`, `FolderPath`, `FolderName`)

**対応フォーマット**:

- JPEG, PNG, TIFF
- RAW: CR2, CR3, NEF, ARW, DNG, ORF, RAF, RW2

---

#### ImportService

写真のインポート処理を管理するサービス。

```csharp
namespace PhotoFastRater.Core.Services;

public class ImportService
{
    /// <summary>
    /// フォルダから写真をインポート
    /// </summary>
    /// <param name="folderPath">インポート元フォルダパス</param>
    /// <param name="includeSubfolders">サブフォルダを含めるか</param>
    /// <param name="exclusionPatterns">除外パターンリスト</param>
    /// <param name="progress">進捗通知</param>
    /// <returns>インポートされた写真のリスト</returns>
    public Task<List<Photo>> ImportFromFolderAsync(
        string folderPath,
        bool includeSubfolders = true,
        List<FolderExclusionPattern>? exclusionPatterns = null,
        IProgress<ImportProgress>? progress = null);

    /// <summary>
    /// 単一ファイルをインポート
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>インポートされた写真、または既に存在する場合null</returns>
    public Task<Photo?> ImportSingleFileAsync(string filePath);
}

public class ImportProgress
{
    public string CurrentFile { get; set; }
    public int ProcessedCount { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; }
}
```

**使用例**:

```csharp
var importService = new ImportService(photoRepository, exifService);

var progress = new Progress<ImportProgress>(p =>
{
    Console.WriteLine($"{p.ProcessedCount}/{p.TotalCount}: {p.Status}");
});

var photos = await importService.ImportFromFolderAsync(
    @"C:\MyPhotos",
    includeSubfolders: true,
    exclusionPatterns: null,
    progress: progress
);

Console.WriteLine($"Imported {photos.Count} photos");
```

---

#### DataMigrationService

データベーススキーマ変更時のデータ移行サービス。

```csharp
namespace PhotoFastRater.Core.Services;

public class DataMigrationService
{
    /// <summary>
    /// FolderPathとFolderNameを既存の写真に設定
    /// </summary>
    /// <param name="progress">進捗通知</param>
    public Task MigrateFolderPathsAsync(IProgress<MigrationProgress>? progress = null);

    /// <summary>
    /// すべての保留中のマイグレーションを実行
    /// </summary>
    /// <param name="progress">進捗通知</param>
    public Task RunAllMigrationsAsync(IProgress<MigrationProgress>? progress = null);
}

public class MigrationProgress
{
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; }
}
```

**使用例**:

```csharp
var migrationService = new DataMigrationService(dbContext);

var progress = new Progress<MigrationProgress>(p =>
{
    Console.WriteLine($"[{p.CurrentIndex}/{p.TotalCount}] {p.Status}");
});

await migrationService.RunAllMigrationsAsync(progress);
```

---

#### FolderSessionService

フォルダモードのセッション管理サービス。

```csharp
namespace PhotoFastRater.Core.Services;

public class FolderSessionService
{
    /// <summary>
    /// フォルダのセッションを作成または読み込み
    /// </summary>
    /// <param name="folderPath">フォルダパス</param>
    /// <returns>セッション情報</returns>
    public Task<FolderSession> CreateSessionAsync(string folderPath);

    /// <summary>
    /// フォルダから写真を読み込み
    /// </summary>
    /// <param name="folderPath">フォルダパス</param>
    /// <param name="progress">進捗通知</param>
    /// <returns>写真リスト</returns>
    public Task<List<FolderSessionPhoto>> LoadPhotosAsync(
        string folderPath,
        IProgress<int>? progress = null);

    /// <summary>
    /// セッションを保存
    /// </summary>
    /// <param name="session">セッション情報</param>
    public Task SaveSessionAsync(FolderSession session);
}
```

---

#### SocialMediaExporter

SNS 向けに最適化された画像エクスポートサービス。

```csharp
namespace PhotoFastRater.Core.Export;

public class SocialMediaExporter : IImageExporter
{
    /// <summary>
    /// 写真をエクスポート（リサイズ、枠追加、EXIF オーバーレイ）
    /// </summary>
    /// <param name="photo">エクスポートする写真</param>
    /// <param name="template">エクスポート設定テンプレート</param>
    /// <param name="outputPath">出力先パス</param>
    /// <returns>出力先パス</returns>
    public Task<string> ExportAsync(Photo photo, ExportTemplate template, string outputPath);
}
```

**使用例**:

```csharp
var exporter = new SocialMediaExporter();

var template = new ExportTemplate
{
    Name = "Instagram Export",
    TargetPlatform = SocialMediaPlatform.Instagram,
    EnableFrame = true,
    FrameWidth = 30,
    EnableExifOverlay = true,
    Position = ExifOverlayPosition.BottomLeft
};

await exporter.ExportAsync(photo, template, @"C:\Exports\output.jpg");
```

**機能**:

- プラットフォーム別の最適サイズへのリサイズ
- 枠（フレーム）の追加
- EXIF 情報のオーバーレイ表示
- 元画像のメタデータ保持（EXIF、IPTC、XMP、ICC プロファイル）
- 高品質 JPEG 出力（Quality: 95）

---

#### ExifOverlayRenderer

画像に EXIF 情報をオーバーレイ表示するレンダラー。

```csharp
namespace PhotoFastRater.Core.Export;

public class ExifOverlayRenderer
{
    /// <summary>
    /// 画像に EXIF オーバーレイを描画
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="photo">写真情報</param>
    /// <param name="template">エクスポート設定</param>
    public void RenderExifOverlay(Image<Rgba32> image, Photo photo, ExportTemplate template);
}
```

**表示内容**:

- カメラモデル
- レンズモデル
- 焦点距離（mm）
- 絞り値（f/）
- シャッタースピード（秒）
- ISO 感度
- 撮影日時
- 位置情報

**カスタマイズ可能な項目**:

- 表示位置（左上、右上、左下、右下、カスタム）
- フォントファミリー
- フォントサイズ
- テキストカラー
- 背景カラー
- 背景の透明度

---

### Repositories

#### PhotoRepository

写真エンティティのデータアクセスリポジトリ。

```csharp
namespace PhotoFastRater.Core.Database.Repositories;

public class PhotoRepository
{
    /// <summary>
    /// すべての写真を取得
    /// </summary>
    /// <returns>すべての写真のリスト</returns>
    public Task<List<Photo>> GetAllAsync();

    /// <summary>
    /// IDで写真を取得
    /// </summary>
    /// <param name="id">写真ID</param>
    /// <returns>写真オブジェクト、見つからない場合null</returns>
    public Task<Photo?> GetByIdAsync(int id);

    /// <summary>
    /// ファイルパスで写真を取得
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>写真オブジェクト、見つからない場合null</returns>
    public Task<Photo?> GetByFilePathAsync(string filePath);

    /// <summary>
    /// 写真を追加
    /// </summary>
    /// <param name="photo">写真オブジェクト</param>
    /// <returns>追加された写真（IDが設定される）</returns>
    public Task<Photo> AddAsync(Photo photo);

    /// <summary>
    /// 写真を更新
    /// </summary>
    /// <param name="photo">写真オブジェクト</param>
    public Task UpdateAsync(Photo photo);

    /// <summary>
    /// 写真を削除
    /// </summary>
    /// <param name="id">写真ID</param>
    public Task DeleteAsync(int id);

    /// <summary>
    /// カメラ別に写真を取得
    /// </summary>
    /// <param name="make">カメラメーカー</param>
    /// <param name="model">カメラモデル</param>
    /// <returns>該当する写真のリスト</returns>
    public Task<List<Photo>> GetByCameraAsync(string make, string model);

    /// <summary>
    /// レーティング別に写真を取得
    /// </summary>
    /// <param name="rating">レーティング（0-5）</param>
    /// <returns>該当する写真のリスト</returns>
    public Task<List<Photo>> GetByRatingAsync(int rating);

    /// <summary>
    /// 日付範囲で写真を取得
    /// </summary>
    /// <param name="startDate">開始日</param>
    /// <param name="endDate">終了日</param>
    /// <returns>該当する写真のリスト</returns>
    public Task<List<Photo>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// フォルダパスで写真を取得
    /// </summary>
    /// <param name="folderPath">フォルダパス</param>
    /// <returns>該当する写真のリスト</returns>
    public Task<List<Photo>> GetByFolderPathAsync(string folderPath);

    /// <summary>
    /// ファイルパスが既に存在するかチェック
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>存在する場合true</returns>
    public Task<bool> ExistsAsync(string filePath);
}
```

**使用例**:

```csharp
var photoRepository = new PhotoRepository(dbContext);

// すべての写真を取得
var allPhotos = await photoRepository.GetAllAsync();

// レーティング5の写真を取得
var topPhotos = await photoRepository.GetByRatingAsync(5);

// カメラ別に取得
var canonPhotos = await photoRepository.GetByCameraAsync("Canon", "EOS R5");

// 日付範囲で取得
var recentPhotos = await photoRepository.GetByDateRangeAsync(
    DateTime.Now.AddMonths(-1),
    DateTime.Now
);

// フォルダ別に取得
var folderPhotos = await photoRepository.GetByFolderPathAsync(@"C:\Photos\2025");
```

---

#### EventRepository

イベントエンティティのデータアクセスリポジトリ。

```csharp
namespace PhotoFastRater.Core.Database.Repositories;

public class EventRepository
{
    public Task<List<Event>> GetAllAsync();
    public Task<Event?> GetByIdAsync(int id);
    public Task<Event> AddAsync(Event @event);
    public Task UpdateAsync(Event @event);
    public Task DeleteAsync(int id);
    public Task<List<Event>> GetByDateRangeAsync(DateTime start, DateTime end);
    public Task AddPhotoToEventAsync(int eventId, int photoId);
    public Task RemovePhotoFromEventAsync(int eventId, int photoId);
    public Task<List<Photo>> GetEventPhotosAsync(int eventId);
}
```

---

## UI Layer

### ViewModels

#### PhotoGridViewModel

写真一覧画面の ViewModel。

```csharp
namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoGridViewModel : ViewModelBase
{
    // プロパティ
    [ObservableProperty]
    private ObservableCollection<PhotoViewModel> _photos;

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _photoTree;

    [ObservableProperty]
    private PhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private bool _isTreeViewMode;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText;

    // コマンド
    [RelayCommand]
    private async Task LoadPhotosAsync();

    [RelayCommand]
    private void SelectPhoto(PhotoViewModel photo);

    [RelayCommand]
    private void OpenPhoto(PhotoViewModel photo);

    [RelayCommand]
    private void NavigateUp();

    [RelayCommand]
    private void NavigateDown();

    [RelayCommand]
    private void NavigateLeft();

    [RelayCommand]
    private void NavigateRight();

    [RelayCommand]
    private async Task SetRatingAsync(int rating);

    [RelayCommand]
    private async Task ToggleFavoriteAsync();

    [RelayCommand]
    private async Task ImportPhotosAsync();

    // メソッド
    public void BuildPhotoTree();
    public void ApplyFilters();
    public void ApplySort();
}
```

**使用例**:

```csharp
// XAML でのバインディング
<ItemsControl ItemsSource="{Binding Photos}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Image Source="{Binding Thumbnail}"/>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>

<Button Content="Load Photos" Command="{Binding LoadPhotosCommand}"/>
```

---

#### PhotoViewModel

単一の写真を表す ViewModel。

```csharp
namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoViewModel : ViewModelBase
{
    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isRejected;

    [ObservableProperty]
    private bool _isSelected;

    // 読み取り専用プロパティ
    public int Id { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public DateTime DateTaken { get; }
    public string? CameraModel { get; }
    public int? ISO { get; }

    public Photo GetModel();
}
```

---

#### PhotoTreeNode

ツリービュー階層のノードを表す ViewModel。

```csharp
namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoTreeNode : ViewModelBase
{
    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _children;

    [ObservableProperty]
    private ObservableCollection<PhotoViewModel> _photos;

    public TreeNodeType NodeType { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
    public int? Day { get; set; }
    public string? FolderPath { get; set; }

    public int PhotoCount => Photos.Count + Children.Sum(c => c.PhotoCount);
    public string DisplayNameWithCount => $"{DisplayName} ({PhotoCount}枚)";
}

public enum TreeNodeType
{
    Year,
    Month,
    Day,
    Folder
}
```

**使用例**:

```csharp
// ツリー構造の構築
var yearNode = new PhotoTreeNode
{
    NodeType = TreeNodeType.Year,
    Year = 2025,
    DisplayName = "2025年"
};

var monthNode = new PhotoTreeNode
{
    NodeType = TreeNodeType.Month,
    Month = 12,
    DisplayName = "12月"
};

yearNode.Children.Add(monthNode);
```

---

#### FolderModeViewModel

フォルダモード画面の ViewModel。

```csharp
namespace PhotoFastRater.UI.ViewModels;

public partial class FolderModeViewModel : ViewModelBase
{
    [ObservableProperty]
    private FolderSession? _currentSession;

    [ObservableProperty]
    private ObservableCollection<FolderSessionPhotoViewModel> _photos;

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _photoTree;

    [ObservableProperty]
    private FolderSessionPhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private bool _isTreeViewMode;

    [ObservableProperty]
    private string _folderPath;

    [ObservableProperty]
    private int _totalPhotos;

    [ObservableProperty]
    private int _ratedPhotos;

    // コマンド
    [RelayCommand]
    private async Task OpenFolderAsync();

    [RelayCommand]
    private void SelectPhoto(FolderSessionPhotoViewModel photo);

    [RelayCommand]
    private async Task SetRatingAsync(int rating);

    [RelayCommand]
    private async Task SaveSessionAsync();

    [RelayCommand]
    private async Task ExportToDbAsync();

    [RelayCommand]
    private void NavigateUp();

    [RelayCommand]
    private void NavigateDown();

    [RelayCommand]
    private void NavigateLeft();

    [RelayCommand]
    private void NavigateRight();

    public Task LoadFolderAsync(string folderPath);
    public void BuildPhotoTree();
}
```

---

### UI Services

#### ImageLoader

画像の非同期読み込みサービス。

```csharp
namespace PhotoFastRater.UI.Services;

public class ImageLoader
{
    /// <summary>
    /// 画像を非同期で読み込み
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <returns>BitmapImageオブジェクト</returns>
    public Task<BitmapImage> LoadAsync(string filePath);

    /// <summary>
    /// サムネイルを生成してキャッシュ
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <param name="size">サムネイルサイズ</param>
    /// <returns>サムネイルのBitmapImage</returns>
    public Task<BitmapImage> LoadThumbnailAsync(string filePath, int size = 256);

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    public void ClearCache();
}
```

**使用例**:

```csharp
var imageLoader = new ImageLoader(cacheConfiguration);

// サムネイル読み込み
var thumbnail = await imageLoader.LoadThumbnailAsync(@"C:\Photos\IMG_0001.jpg", 256);

// フルサイズ読み込み
var fullImage = await imageLoader.LoadAsync(@"C:\Photos\IMG_0001.jpg");
```

---

## Configuration

### UIConfiguration

UI 関連の設定クラス。

```csharp
namespace PhotoFastRater.Core.UI;

public class UIConfiguration
{
    /// <summary>
    /// グリッドサムネイルサイズ (デフォルト: 256)
    /// </summary>
    public int GridThumbnailSize { get; set; } = 256;

    /// <summary>
    /// GPU高速化の有効化 (デフォルト: true)
    /// </summary>
    public bool EnableGPUAcceleration { get; set; } = true;

    /// <summary>
    /// 矢印キーナビゲーションモード
    /// "GridFocus" または "SelectionOnly"
    /// </summary>
    public string ArrowKeyNavigationMode { get; set; } = "GridFocus";
}
```

**appsettings.json**:

```json
{
  "UI": {
    "GridThumbnailSize": 256,
    "EnableGPUAcceleration": true,
    "ArrowKeyNavigationMode": "GridFocus"
  }
}
```

---

### CacheConfiguration

キャッシュ関連の設定クラス。

```csharp
namespace PhotoFastRater.Core.UI;

public class CacheConfiguration
{
    /// <summary>
    /// キャッシュディレクトリパス
    /// </summary>
    public string CacheDirectory { get; set; }

    /// <summary>
    /// 最大キャッシュサイズ (MB)
    /// </summary>
    public int MaxCacheSizeMB { get; set; } = 1024;

    /// <summary>
    /// サムネイル品質 (1-100)
    /// </summary>
    public int ThumbnailQuality { get; set; } = 85;
}
```

**appsettings.json**:

```json
{
  "Cache": {
    "CacheDirectory": "%LOCALAPPDATA%\\PhotoFastRater\\thumbnails",
    "MaxCacheSizeMB": 1024,
    "ThumbnailQuality": 85
  }
}
```

---

## 依存性注入 (DI) 設定

### サービス登録

```csharp
// App.xaml.cs
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Configuration
        var (cacheConfig, uiConfig) = LoadConfiguration();
        services.AddSingleton(cacheConfig);
        services.AddSingleton(uiConfig);

        // Database
        services.AddDbContext<PhotoDbContext>(options =>
            options.UseSqlite("Data Source=photos.db"));

        // Repositories
        services.AddScoped<PhotoRepository>();
        services.AddScoped<EventRepository>();
        services.AddScoped<CameraRepository>();

        // Services
        services.AddScoped<ExifService>();
        services.AddScoped<ImportService>();
        services.AddScoped<FolderSessionService>();
        services.AddScoped<DataMigrationService>();
        services.AddSingleton<ImageLoader>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<PhotoGridViewModel>();
        services.AddTransient<FolderModeViewModel>();
        services.AddTransient<SettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // データベースマイグレーション実行
        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
            context.Database.Migrate();
        }

        base.OnStartup(e);
    }
}
```

---

## エラーハンドリング

### 標準的なエラーハンドリングパターン

```csharp
public async Task<List<Photo>> ImportFromFolderAsync(string folderPath)
{
    try
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {folderPath}");
        }

        var photos = new List<Photo>();
        // インポート処理...

        return photos;
    }
    catch (DirectoryNotFoundException ex)
    {
        // ユーザーにエラーメッセージ表示
        MessageBox.Show(ex.Message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        return new List<Photo>();
    }
    catch (Exception ex)
    {
        // 予期しないエラーのログ記録
        Debug.WriteLine($"エラー: {ex}");
        throw;
    }
}
```

---

## パフォーマンスのベストプラクティス

### 1. 非同期処理の活用

```csharp
// ✅ 良い例: 非同期で実行
public async Task LoadPhotosAsync()
{
    IsLoading = true;
    try
    {
        var photos = await _photoRepository.GetAllAsync();
        Photos = new ObservableCollection<PhotoViewModel>(
            photos.Select(p => new PhotoViewModel(p))
        );
    }
    finally
    {
        IsLoading = false;
    }
}

// ❌ 悪い例: UIスレッドをブロック
public void LoadPhotos()
{
    var photos = _photoRepository.GetAllAsync().Result; // ブロッキング!
}
```

### 2. バッチ処理

```csharp
// ✅ 良い例: バッチで一括更新
public async Task UpdateRatingsAsync(List<Photo> photos)
{
    foreach (var photo in photos)
    {
        _context.Photos.Update(photo);
    }
    await _context.SaveChangesAsync(); // 一度だけ保存
}

// ❌ 悪い例: 個別に保存
public async Task UpdateRatingsAsync(List<Photo> photos)
{
    foreach (var photo in photos)
    {
        _context.Photos.Update(photo);
        await _context.SaveChangesAsync(); // 毎回保存!
    }
}
```

### 3. メモリ管理

```csharp
// ✅ 良い例: Disposeパターン
public class ImageLoader : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(10);

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

// 使用側
using var imageLoader = new ImageLoader();
await imageLoader.LoadAsync(filePath);
```

---

## テスト例

### ユニットテスト

```csharp
[Fact]
public async Task ImportFromFolderAsync_ShouldImportAllPhotos()
{
    // Arrange
    var mockRepo = new Mock<PhotoRepository>();
    var mockExif = new Mock<ExifService>();
    var importService = new ImportService(mockRepo.Object, mockExif.Object);

    // Act
    var photos = await importService.ImportFromFolderAsync(@"C:\TestPhotos");

    // Assert
    photos.Should().NotBeEmpty();
    photos.All(p => p.FilePath.StartsWith(@"C:\TestPhotos")).Should().BeTrue();
}
```

---

## まとめ

この API リファレンスは、Photo Fast Rater の主要なクラスとメソッドを網羅しています。
実際の実装では、各メソッドの詳細な動作を確認し、適切なエラーハンドリングと非同期処理を行ってください。

さらに詳しい情報は、[ARCHITECTURE.md](./ARCHITECTURE.md)を参照してください。
