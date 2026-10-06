# Photo Fast Rater - ビルド・動作検証ノート

最終検証: 2026-10-06（Windows 11 x64 / .NET SDK 10.0.401）

## 再現可能な検証手順

```powershell
dotnet restore PhotoFastRater.sln --locked-mode
dotnet format whitespace PhotoFastRater.sln --verify-no-changes --no-restore
dotnet format style PhotoFastRater.sln --verify-no-changes --no-restore
dotnet build PhotoFastRater.sln -c Release --no-restore
dotnet test PhotoFastRater.sln -c Release --no-build
dotnet list PhotoFastRater.sln package --vulnerable --include-transitive
dotnet publish src/PhotoFastRater.UI/PhotoFastRater.UI.csproj -p:PublishProfile=win-x64 -p:RestoreLockedMode=true
```

`--locked-mode`は`dotnet restore`のオプションです。publishでは
`-p:RestoreLockedMode=true`を指定します。配布プロファイルにはReadyToRunが
含まれるため、通常のsolution restoreだけでは必要なランタイム・コンパイラパックが
揃いません。publish時の復元を省略しないことで、lock fileを維持して必要なパックを取得します。

## 実施した動作確認

- Releaseテスト47件が成功（DB移行、評価保存、検索、タグ、コレクション、キャッシュ、画像デコード、XMP、出力など）。
- 通常モードの実行ファイルで起動、ウィンドウ応答、閉じた後の終了コード0を確認。
- 一時フォルダのJPEGとPNG各1枚を使い、フォルダモードで起動と正常終了を確認。
- 自己完結win-x64配布物をリポジトリ外の作業ディレクトリから起動し、
  Automation経由で両画像のファイル名と「2枚の写真を読み込みました」を確認。正常終了も確認。
- 書式・コードスタイルの検証と依存監査を実施。依存監査で既知の脆弱性の報告なし。

実画像10万枚の総合受入試験、すべてのUI操作、未対応RAWの現像は、この確認の対象外です。
既存の静的解析警告は残っています。機能の実装範囲と制約は
[実装状況](docs/ja/IMPLEMENTATION_STATUS.md)を参照してください。

## 今回修正した問題と理由

### ウィンドウを閉じても終了しない

XMP同期ワーカーをUIスレッドで直接開始していたため、非同期の継続処理が
UIの同期コンテキストに戻ろうとしていました。終了処理がそのワーカーを同期的に待つと、
UI側とワーカー側が互いに待ち続けます。

ワーカーを`Task.Run`で開始し、継続処理をUIの寿命から分離しました。
単一ワーカー、容量512件のキュー、DBに残る未同期状態という既存の設計は維持します。
キャンセル時には処理中のI/Oも停止を待つため、そのI/Oがキャンセルを尊重する必要があります。

回帰テストはSTAスレッドに継続処理を実行しない同期コンテキストを設定し、
空のキューをDisposeしてもUI側の継続処理を要求せず完了することを検証します。
修正前は5秒でタイムアウトし、修正後は成功しました。
初期化で追加する処理はO(1)であり、写真全件をコピーする処理は追加していません。

### 配布用publishが失敗する

Windows CIの`--no-restore`によるReadyToRunパック不足と、READMEのpublishに対する
不正な`--locked-mode`指定を修正しました。CIとREADMEを、配布プロファイルを指定して
固定復元を含める同じコマンドに揃えています。

## 起動・ログ

```powershell
dotnet run --project src/PhotoFastRater.UI
dotnet run --project src/PhotoFastRater.UI -- --folder "C:\path\to\photos"
```

カタログDBは`%LOCALAPPDATA%\PhotoFastRater\photos.db`、
日次ログは`%LOCALAPPDATA%\PhotoFastRater\Logs`に保存されます。
既存DBの移行前にはSQLiteバックアップを作成します。
元画像とユーザーデータの扱いは[データ保護](docs/ja/DATA_SAFETY.md)を参照してください。

アーキテクチャと主要依存関係は[README](README.md)、
中央管理されたパッケージバージョンは`Directory.Packages.props`を参照してください。

## UI検証ツール

```powershell
dotnet test PhotoFastRater.sln -c Release
dotnet run --project tools/PhotoFastRater.UIValidation -c Release -- C:\Programming\PhotoFastRater\artifacts\ui-validation 0 software
dotnet run --project tools/PhotoFastRater.UIValidation -c Release -- C:\Programming\PhotoFastRater\artifacts\ui-validation 0
dotnet run --project tools/PhotoFastRater.UIValidation -c Release -- C:\Programming\PhotoFastRater\artifacts\ui-validation 1800 measure
pwsh -NoProfile -File tools/validation/Get-UiInventory.ps1
```

UIValidationは専用プロファイルを作り本番DIとWPF画面を使います。確認・ファイル選択・外部シェルの一部を代替するため、実OS操作や物理入力の完全なE2Eとは区別します。測定中は同じ結果ファイルを書き換える別の測定を起動しないでください。RAW素材は取得スクリプトで取得しSHA-256を検証します。UIの実機条件と受入手順はUI_TEST_PLAN／UI_TEST_REPORTを参照してください。
