# Changelog

このプロジェクトの利用者に影響する変更を記録します。

## Unreleased - 2026-09-05

### Added

- 10万件向け有界走査、SQLite WAL、FTS5、カーソルページング、容量制御サムネイルキャッシュ
- DB先行の評価保存、XMP sidecar同期状態、RAW+JPEG連動
- 2～4枚比較、タグ、階層コレクション、欠損抽出、イベント候補確認
- 非破壊クロップとJPEG／PNG／TIFFバッチ書き出し
- DB移行前バックアップ、日次ローカルログ、Windows CI、win-x64配布プロファイル

### Changed

- CoreからDB、EXIF、キャッシュ、画像処理をInfrastructureへ分離
- 写真ファイル削除を完全削除からWindowsごみ箱へ変更
- JPEG評価埋込を再圧縮フォールバックなしの明示操作へ変更

### Known limitations

- フォルダモードの10万枚データ仮想化と、実画像10万枚の総合受入試験は未完了
- RAWフル現像、XMP競合解決画面、署名済みインストーラーは未実装
