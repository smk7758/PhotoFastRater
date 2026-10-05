# APIリファレンス

更新日: 2026-09-05

この文書は外部I/Oから独立したCore契約と、その主要実装を示します。具象型の全メンバーを転記せず、コンパイラが検証するXML documentationを詳細の正本とします。

## 依存方向

```text
PhotoFastRater.UI ──→ PhotoFastRater.Core
        │
        └──────────→ PhotoFastRater.Infrastructure ──→ PhotoFastRater.Core
```

CoreはEF Core、SQLite、WPF、ImageSharpを参照しません。UIはウィンドウ単位のDI scopeを持ち、Repositoryは`IDbContextFactory<PhotoDbContext>`で操作単位のContextを生成します。

## Core契約

### `IFolderScanner`

```csharp
IAsyncEnumerable<FolderScanItem> ScanAsync(
    string rootPath,
    ScanOptions options,
    CancellationToken cancellationToken = default);
```

逐次列挙と有界Channelを使用します。アクセス拒否や破損画像は`FolderScanItem.Error`として個別に返し、走査全体を終了しません。時間計算量はO(n)、作業メモリはChannel容量に制限されます。

### `IPhotoCatalog`

```csharp
Task<PagedResult<PhotoSummary>> SearchAsync(
    PhotoSearchQuery query,
    PageCursor? after,
    int pageSize,
    CancellationToken cancellationToken = default);

Task UpsertBatchAsync(
    IReadOnlyCollection<Photo> photos,
    CancellationToken cancellationToken = default);
```

ページサイズは1～256件、upsertは最大500件です。検索はFTS5と通常索引、ページ移動は`DateTaken + Id`カーソルを使い、深い`OFFSET`を使いません。

### `IRatingCoordinator`

```csharp
Task SetRatingAsync(
    int photoId,
    RatingState state,
    CancellationToken cancellationToken = default);
```

DBトランザクションを先に確定し、その後XMP同期を予約します。`RatingState.Stars`はXMP標準の-1または0～5です。

### `IXmpSidecarStore` / `IEmbeddedMetadataWriter`

```csharp
Task<XmpRatingDocument?> ReadAsync(string photoPath, CancellationToken cancellationToken = default);
Task WriteAsync(string photoPath, XmpRatingDocument document, CancellationToken cancellationToken = default);
Task<MetadataWriteResult> WriteRatingAsync(string filePath, RatingState state, CancellationToken cancellationToken = default);
```

sidecarは未知XMLを保持し、`元ファイル名＋拡張子.xmp`へ原子的に保存します。埋込書込はJPEGを再圧縮せず、インプレース更新できない場合は失敗を返します。

### `IThumbnailService` / `IImageDecodeService`

```csharp
Task<ReadOnlyMemory<byte>> GetAsync(ThumbnailRequest request, CancellationToken cancellationToken = default);
Task<ReadOnlyMemory<byte>> DecodeAsync(string filePath, int maximumDimension, CancellationToken cancellationToken = default);
```

サムネイル要求は`Visible`、`Prefetch`、`Idle`の優先度を持ち、同じキーの並行要求を統合します。比較デコードは要求寸法に縮小し、呼出側はキャンセルできます。

### `IExportService`

```csharp
Task<IReadOnlyList<ExportResult>> ExportAsync(
    IReadOnlyCollection<int> photoIds,
    ExportRecipe recipe,
    IProgress<ExportProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

1～1,280件を逐次処理し、写真単位の成功・失敗を返します。`ExportRecipe`は正規化`CropRect`、回転、最大寸法、形式、品質、枠、EXIF表示、メタデータ保持を表します。元画像は変更しません。

### UI/OS抽象

- `IUserInteractionService`: 通知、確認、文字入力、フォルダ選択
- `IPlatformShell`: ごみ箱、Explorer、ClipboardなどのWindows操作
- `IPhotoChangeNotifier`: 独立したWindow scope間でDB確定済み変更を通知

## 主要Infrastructure実装

- `PhotoRepository`: FTS5検索、カーソルページング、500件upsert、評価トランザクション
- `FolderScanner` / `ImportService`: 有界走査と部分エラー継続
- `XmpSidecarStore` / `XmpSyncQueue`: 原子的sidecarと永続的な同期待ち状態
- `ThumbnailCacheManager`: 優先度キュー、重複統合、メモリ／ディスクLRU
- `ImageDecodeService`: JPEG等とRAW埋め込みJPEGの表示用デコード
- `BatchExportService`: JPEG／PNG／TIFFの非破壊・部分成功型書き出し
- `LibraryOrganizationRepository`: タグと階層コレクションの冪等な一括関連付け
- `EventRepository`: 決定的候補キーによるイベントの冪等確定

## 例外、キャンセル、上限

- 無効な引数と上限超過は処理開始前に`ArgumentException`系で拒否します。
- I/O処理は`CancellationToken`を受け取り、キャンセルを通常の失敗結果へ偽装しません。
- XMP失敗は評価DBを巻き戻さず、写真単位の同期状態として残します。
- バッチ出力の欠損・破損・権限不足は兄弟の成功を破棄せず`ExportResult.Error`へ記録します。
- 10万件すべての`PhotoViewModel`生成は禁止し、ライブラリUIは最大5ページ（1,280件）だけを保持します。

構造の理由と詳細は[アーキテクチャ](ARCHITECTURE.md)、データ保護規則は[データ保護](DATA_SAFETY.md)を参照してください。
