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

現行Repository APIは互換性のため具象型を残していますが、一覧と検索は`IPhotoCatalog`のFTS5・カーソルページングへ移行済みです。旧FolderSession JSONはDB移行完了まで読み取り互換を維持し、自動削除しません。

## 仮想化とサムネイル

ライブラリ一覧は`IAsyncVirtualizingCollection<T>`を通じて256件ずつ読み、最大5ページ（1,280件）だけをUIに保持します。深い位置でも`OFFSET`は使わず、`DateTaken + Id`の安定カーソルで次ページを取得します。選択の識別にはDBのPhoto IDを用います。

サムネイル要求は可視、通常、先読みの3本の有界キューへ入り、6ワーカーが可視要求から処理します。同一キーの同時要求は1タスクへ統合します。キーは正規化パス、サイズ、更新UTC、寸法、JPEG品質、生成器バージョンのSHA-256です。ディスクはハッシュ先頭2文字ずつの2階層に分散し、SQLiteの最終アクセス時刻で既定10GB（設定1～100GB）のLRU削除を行います。

メモリLRUは圧縮JPEGサイズだけでなく、サムネイルをRGBA展開した概算サイズも予算へ含めます。全件先読みは禁止し、可視領域と直後の範囲だけを要求します。

## UIと設定

MainWindowの検索は入力から200ms後に`PhotoSearchQuery`へ変換し、新しい入力が来た場合は旧DBページ取得と可視サムネイル要求をキャンセルします。インポートは総数を事前列挙せず、確認済み件数、個別エラー件数、キャンセル結果をステータスラインへ表示します。これは10万件の事前カウントによる二重走査を避けるためです。

配布物の`appsettings.json`は既定値としてのみ読み、変更可能な設定は`%LOCALAPPDATA%\PhotoFastRater\user-settings.json`へ一時ファイル経由で原子的に保存します。キャッシュ構成はキャッシュサービス生成時に固定されるため、保存後の変更は次回起動時に反映します。キャッシュクリアは元画像や任意フォルダのJPEGを削除せず、キャッシュ管理下の生成済みサムネイルだけを隣接する`_GARBAGE`へ移動します。

画面テーマは「暗室の選別机」を基準に、写真面を広く、操作面を細く、処理状態を常時確認できる構造とします。評価Amber、リジェクトRedは意味を持つ箇所だけに使い、文言・枠も併用して色覚だけに依存しません。

## 比較ワークスペース

比較は2～4ペインに制限し、各画像を最大2,048pxへ段階デコードします。したがって画像メモリはライブラリ件数ではなく最大4枚の表示用画素に比例します。RAWは埋め込みJPEGを利用し、フル現像は行いません。ズームとパンは既定で同期し、OFF時は操作対象ペインだけを変更します。固定ペインは「次の組」および評価後の自動送りから除外し、自動送りは誤操作を避けるため既定OFFです。

## タグ、コレクション、イベント

タグは表示名とは別に大文字化した正規化名を保持し、一意制約で大小文字違いの重複を防ぎます。写真との関係は中間テーブルで管理し、同じタグを再度付けても結果が変わらない冪等操作です。タグ名は`TagSearch` FTS5表とトリガーで同期し、写真のファイル名・パス索引との和集合をパラメーター化SQLで検索します。

コレクションは自己参照の`ParentId`で階層化し、写真は複数コレクションへ所属できます。親を削除した際の意図しない連鎖削除を避けるため、親子関係は`Restrict`です。一括タグ付けと一括追加はUIが保持できる上限と同じ1,280件までに制限し、巨大なSQLパラメーター列や誤操作を防ぎます。

イベント自動整理は候補作成とDB更新を分離します。候補キーは所属写真IDから決定的に生成し、確定時の一意制約とトランザクションにより再実行してもイベントや対応関係を重複させません。候補確認ではDBを変更しないため、ユーザーは件数と名前を確認してから一括確定できます。

## 非破壊バッチ書き出し

`ExportRecipe`は出力先、命名規則、形式、品質、正規化クロップ、回転、最大寸法、枠、EXIF表示、メタデータ保持を1つの反復可能な値として表します。処理順は向き補正→回転→クロップ→リサイズ→枠→EXIF表示→メタデータ復元→保存です。向き補正後は元のOrientationタグだけを除き、他アプリで二重回転されることを防ぎます。

DBからの写真取得は最大1,280件を1クエリにまとめ、画像は逐次処理してフル画像メモリを1枚分に抑えます。各画像は出力先と同じディレクトリの一時ファイルへ書き、完成後に上書きなしの移動で確定します。同名競合時は連番を採用します。欠損、破損、権限不足、未対応RAWは他の成功を破棄せず写真単位の結果とし、UIは失敗IDだけを保持して明示的に再試行できます。キャンセル時も確定済み出力は保持し、処理中の一時ファイルだけを除去します。
