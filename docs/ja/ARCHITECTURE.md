# アーキテクチャ

更新日: 2026-09-05

## 目的

PhotoFastRaterは、単一フォルダおよびライブラリ各10万枚でも、写真数に比例してUIオブジェクトや画像を保持しない構造を目指します。評価を失わないこと、元画像を暗黙に変更しないこと、I/Oを伴わない単体テストを可能にすることを境界設計の優先事項とします。

## 変更前と変更理由

変更前はCoreアセンブリにEF Core、SQLite、EXIF、画像デコード、キャッシュ、書き出しが同居し、ViewModelが`IServiceProvider`、`MessageBox`、`Process`、`Clipboard`へ直接アクセスしていました。また、1つのscoped `PhotoDbContext`を画面の存続期間中保持するため、追跡エンティティが増え続け、別ウィンドウとの更新競合も起きやすい構造でした。

そこで、ドメイン規則とユースケース契約をCoreへ、外部I/Oの実装をInfrastructureへ、表示とWindows固有の対話をUIへ分離しました。CoreはEF Core、ImageSharp、MetadataExtractorを参照しません。この不変条件は`CoreBoundaryTests`で検証します。

## 変更後の構造

```text
PhotoFastRater.UI
  ├─ Views / ViewModels
  ├─ Windows固有の対話・ごみ箱・Shell
  └─ ウィンドウ単位のDIスコープ
            │
            ├──────────────┐
            ▼              ▼
PhotoFastRater.Core   PhotoFastRater.Infrastructure
  ├─ Domain             ├─ Database / Repositories / Migrations
  ├─ Models             ├─ EXIF / Scan / Session
  └─ Abstractions       ├─ Cache / ImageProcessing
                       └─ Export
```

依存方向はUI→Core、UI→Infrastructure、Infrastructure→Coreです。CoreからInfrastructureまたはUIへの参照は禁止します。

## ライフタイム

- `WindowManager`がMainWindowとFolderModeWindowごとにDIスコープを作り、閉じた時点で破棄します。
- Repositoryは`IDbContextFactory<PhotoDbContext>`から操作単位の短命Contextを作ります。
- 読み取りは原則`AsNoTracking`とし、ChangeTrackerのメモリを写真件数に比例させません。
- `IPhotoChangeNotifier`はDB確定後の変更を独立ウィンドウへ通知する共有点です。
- DB migrationは最終ServiceProvider構築後に一度だけ実行し、構成途中の一時ServiceProviderは作りません。

## Coreの主要契約

- `IFolderScanner`: 逐次列挙、キャンセル、個別エラー、O(n)・有界メモリ
- `IPhotoCatalog`: 一括upsertとカーソルページング検索
- `IRatingCoordinator`: DB先行確定とXMP同期予約
- `IXmpSidecarStore`: 未知XMLを保持する原子的sidecar更新
- `IEmbeddedMetadataWriter`: 再圧縮しない明示的な埋込更新
- `IThumbnailService` / `IImageDecodeService`: 優先度、重複統合、キャンセル
- `IExportService`: 元画像を変更しない部分成功型バッチ出力
- `IUserInteractionService` / `IPlatformShell`: ViewModelからWPFとOS操作を隔離

## 次の移行

現行Repository APIは互換性のため具象型を残しています。次段階で`IPhotoCatalog`へ集約し、WAL、正規化パスupsert、FTS5、カーソルページングを実装します。旧FolderSession JSONはDB移行完了まで読み取り互換を維持し、自動削除しません。
