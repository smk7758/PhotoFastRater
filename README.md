# Photo Fast Rater

高速表示を最重要とした写真レーティング・管理ソフトウェア

> [!IMPORTANT]
> 現在は常用ベータに向けた段階的な再構築中です。実装済み機能と制約は
> [実装状況](docs/ja/IMPLEMENTATION_STATUS.md)を参照してください。
> カタログ層は合成10万件で測定済みですが、フォルダモードと実画像10万枚の総合受入は未完了です。

## 主な機能

### 📸 高速表示

- **3 段階キャッシュシステム**: メモリ → SSD → 元画像
- **LRU キャッシュ管理**: 最近使用した画像を優先的にキャッシュ
- **非同期・並列読み込み**: UI をブロックせずスムーズに表示
- **プリフェッチ機能**: 次に表示する画像を先読み

### 🗂️ 柔軟なグルーピング

- **フォルダ横断管理**: 物理的なフォルダ構造に縛られない
- **イベント/場所別グルーピング**: 日付や GPS 情報で自動グルーピング
- **カスタムイベント**: 手動で写真をイベントにまとめる
- **カメラ・レンズ別表示**: 機材ごとに写真を整理
- **横断検索**: ファイル名・パス・タグをFTS5索引から検索
- **タグと階層コレクション**: 最大1,280件の選択へ一括付与・追加
- **欠損管理**: 元ファイルが見つからないDB記録だけを抽出

### ⭐ レーティング機能

- 0-5 段階の星評価
- お気に入りマーク
- リジェクト機能
- レーティングでのフィルタリング

### 🔍 2～4枚比較

- 選択写真と隣接写真を最大4ペインで比較
- 同期ズーム・パンをON/OFF可能
- ペイン固定、入替、任意の評価後自動送り

### 🎨 非破壊エクスポート

- **枠の追加**: カスタマイズ可能な枠を画像に追加
- **EXIF 情報オーバーレイ**: カメラ、レンズ、撮影設定を画像に表示
- **SNS プリセット**: Instagram、Twitter、Facebook 用の最適サイズ
- **安全な単体出力**: 選択写真を別名で出力し、同名ファイルを上書きしない
- **バッチ出力**: チェックした最大1,280枚をJPEG／PNG／TIFFへ書き出し
- **非破壊クロップ**: 正規化座標と縦横比プリセット、回転、最大寸法を指定
- **部分成功**: 欠損・破損・未対応RAWを個別表示し、失敗項目だけ再試行

> RAWファイルは埋め込みJPEGの表示のみです。RAWフル現像を伴う書き出しは未対応です。

### 📷 RAW 対応

- RAW ファイルの即座にサムネイル化して JPEG 保存
- 主要な RAW 形式をサポート（CR2, CR3, NEF, ARW, DNG 等）

## システム要件

- Windows 11
- .NET 10.0 Runtime
- SSD 推奨（キャッシュ用）

## インストール

現時点では署名済みインストーラーを提供していません。開発用配布物は次で作成できます。

```powershell
dotnet publish src/PhotoFastRater.UI -p:PublishProfile=win-x64 -p:RestoreLockedMode=true
```

生成されたpublishディレクトリをWindows 11 x64環境へコピーして起動します。公開配布前にはコード署名と更新・ロールバック検証が必要です。

## 使い方

### 初回セットアップ

1. **設定タブ**を開く
2. **キャッシュフォルダ**に SSD のパスを指定（推奨）
3. **設定を保存**をクリック

### 写真のインポート

1. **インポート**ボタンをクリック
2. 写真が保存されているフォルダを選択
3. サブフォルダも含めてインポートされます

### レーティング

1. 写真を選択
2. 0-5 の数字キーで評価
3. F キーでお気に入り登録

### イベント作成

#### 手動作成

1. **イベント**タブを開く
2. イベント名を入力
3. 写真を選択してイベントに追加

#### 自動グルーピング

1. **イベント**タブで**自動候補を確認**をクリック
2. 日付と GPS 情報で近い候補の名前と件数を確認
3. **候補を確定**をクリック（同じ候補を再確定しても重複しません）

### SNS 用エクスポート

1. **エクスポート**タブを開く
2. 枠や EXIF 表示の設定を選択
3. SNS プラットフォームを選択
4. **エクスポート**をクリック

### バッチエクスポート

1. 写真カード左上のチェックで対象を選択
2. **エクスポート**タブで形式、命名規則、クロップ、回転、最大寸法を設定
3. **チェック写真を書き出す**をクリックして出力先を選択
4. 個別の結果を確認し、必要なら**失敗だけ再試行**をクリック

## アーキテクチャ

### プロジェクト構成

```text
PhotoFastRater/
├── src/PhotoFastRater.Core/  # I/O非依存のドメイン型・契約
│   ├── Models/               # データモデル
├── src/PhotoFastRater.Infrastructure/ # DB、走査、XMP、キャッシュ、出力
├── src/PhotoFastRater.UI/    # WPF UI
│   ├── ViewModels/           # MVVM ViewModels
│   └── Views/                # XAML Views
└── src/PhotoFastRater.Tests/ # テスト
```

### 使用技術

- **C# / .NET 10.0**: メイン言語・フレームワーク
- **WPF**: UI フレームワーク
- **Entity Framework Core**: ORM
- **SQLite**: データベース
- **ImageSharp**: 画像処理
- **MetadataExtractor**: EXIF 読み取り
- **Material Design**: UI デザイン

### キャッシュ戦略

```text
L1: メモリキャッシュ (LRU, 500MB)
    ↓ キャッシュミス
L2: SSDディスクキャッシュ (JPEG)
    ↓ キャッシュミス
L3: 元画像から生成
```

## 開発

### ビルド方法

```bash
# リポジトリをクローン
git clone https://github.com/yourusername/photo-fast-rater.git
cd photo-fast-rater

# ビルド
dotnet build
dotnet build src/PhotoFastRater.UI

# 実行
dotnet run --project src/PhotoFastRater.UI

# フォルダモードで起動
dotnet run --project src/PhotoFastRater.UI --folder
dotnet run --project src/PhotoFastRater.UI --folder "C:\path\to\photos"

# ユーザーデータに触れない検証用プロファイル（絶対パス）
dotnet run --project src/PhotoFastRater.UI -- --data-dir "C:\PFR-Test\Profile" --folder "C:\PFR-Test\Photos"

# リリースビルド
dotnet publish src/PhotoFastRater.UI -c Release -r win-x64 --self-contained
```

### データベースマイグレーション

```bash
dotnet ef migrations add MigrationName --project src/PhotoFastRater.Infrastructure --startup-project src/PhotoFastRater.UI
dotnet ef database update --project src/PhotoFastRater.Infrastructure --startup-project src/PhotoFastRater.UI
```

## パフォーマンス最適化

### 高速化のポイント

1. **サムネイルの事前生成**: 初回のみ生成、以降はキャッシュから読み込み
2. **非同期処理**: すべての I/O 処理を非同期化
3. **並列処理**: 複数の画像を並列で読み込み
4. **仮想化**: 表示領域の画像のみレンダリング
5. **プリフェッチ**: 次の画面分を先読み
6. **LRU キャッシュ**: 最近使用した画像を優先的にメモリ保持

## ライセンス

MIT License

## 貢献

プルリクエストを歓迎します！

1. このリポジトリをフォーク
2. フィーチャーブランチを作成 (`git checkout -b feature/AmazingFeature`)
3. 変更をコミット (`git commit -m 'Add some AmazingFeature'`)
4. ブランチにプッシュ (`git push origin feature/AmazingFeature`)
5. プルリクエストを作成

## 既知の問題

- [ ] RAWは埋め込みJPEG表示のみで、フル現像と色管理は未実装
- [ ] フォルダモードの実画像10万枚データ仮想化は未完了
- [ ] GPS情報からの地名取得機能は未実装

## ロードマップ

- [ ] RAW 完全対応（LibRaw 統合）
- [ ] 顔認識機能
- [ ] AI 自動タグ付け
- [ ] クラウド同期
- [ ] モバイルアプリ連携

詳細は[ユーザーガイド](docs/ja/USER_GUIDE.md)、[データ保護](docs/ja/DATA_SAFETY.md)、[性能計測](docs/ja/PERFORMANCE.md)、[ロードマップ](docs/ja/ROADMAP.md)、[変更履歴](CHANGELOG.md)を参照してください。
