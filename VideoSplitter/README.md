# VideoSplitter (Python / Tkinter)

Windows / macOS 向けの MP4 自動分割ツールです。FFmpeg を同梱し、PyInstaller で単一バイナリとして配布できるように設計しています。

## 主な機能

- MP4 を「最大サイズ (GB)」と「最大時間 (分)」の両制約を同時に満たすよう自動分割
- コピー分割を優先し、閾値を超えるパートのみ H.264/AAC で再エンコード
- 処理状況・ログの GUI 表示、キャンセル対応
- 入力動画から音声 (MP3) のみを抽出する簡易変換
- 音声ファイル (MP3/WAV/FLAC など) の分割
- 設定 (しきい値・入出力パス) を `settings.json` に保存・復元
- ログを `logs/app.log` (1MB x 3 ローテーション) に出力
- PyInstaller onefile ビルドに対応 (`build/build.ps1`, `build/video_splitter.spec`)

## 前提条件

- Python 3.11 (開発時)
- Windows 10 / 11 または macOS 13+ での実行を想定
- FFmpeg 公式配布バイナリを以下へ配置  
  - Windows: `third_party/ffmpeg/win-x64/{ffmpeg.exe, ffprobe.exe, LICENSE.txt}`  
  - macOS: `third_party/ffmpeg/mac-universal/{ffmpeg, ffprobe, LICENSE.txt}`  
  （GitHub Actions ビルドでは Windows 版を自動ダウンロードします）

## セットアップ

1. `third_party/ffmpeg/<platform>/` に FFmpeg バイナリ一式を配置します。  
   (例: Windows は https://github.com/BtbN/FFmpeg-Builds, macOS は Homebrew や https://evermeet.cx/ffmpeg/ など)
2. `VideoSplitter/src` を PYTHONPATH に追加するか、プロジェクトルートで `python -m src.app` を起動できます。

```powershell
cd VideoSplitter
python -m src.app
```

## 使い方

1. GUI から入力 MP4、出力フォルダを選択。
2. 最大サイズ (GB) と最大時間 (分) を設定 (初期値 1.5GB / 50分)。
3. 「開始」を押すと別スレッドで処理が走り、進捗とログが表示されます。
4. パート生成後は完了ダイアログからフォルダを開けます。出力ファイル名は `元ファイル名_part_01.mp4` のように元ファイル名を含みます。

### 音声のみを抽出したい場合

1. 入力動画と出力フォルダを指定。
2. 「音声へ変換」を押すと、動画と同名の MP3 を出力フォルダへ生成します（既存ファイルがある場合は上書き確認あり）。

### 音声ファイルを分割したい場合

1. 分割したい音声ファイル (MP3/WAV/FLAC/AAC/OGG/WMA など) を入力に選択。
2. 最大サイズ (GB) / 最大時間 (分) を指定。
3. 「音声分割」を押すと `元ファイル名_part_01.xxx` のように連番で出力フォルダへ保存します（同名ファイルがある場合は上書き確認あり）。

## ログと設定ファイル

- `logs/app.log` … 1MB で 3 世代ローテーション
- `settings.json` … GUI で変更した値を保存。存在しない場合は自動生成

## テスト用ダミー動画生成

`tests/generate_samples.ps1` で 10分・30分・120分のサンプル MP4 を生成します。

```powershell
cd VideoSplitter/tests
.\generate_samples.ps1 -FfmpegPath ..\third_party\ffmpeg\win-x64\ffmpeg.exe
```

## ビルド

### GitHub Actions (Windows)

ローカルに PowerShell / PyInstaller 環境が無い場合は GitHub Actions のワークフローで Windows 向けビルドを行えます。

1. リポジトリを GitHub 上に配置し、`main` ブランチへプッシュします。  
   （手動で実行する場合は Actions タブから `build` ワークフローを `Run workflow` できます）
2. ワークフローは Windows ランナー上で PyInstaller を実行し、最新の FFmpeg を自動ダウンロードして同梱します。
3. 成功後、Actions → 該当ジョブ → `VideoSplitter-win64` アーティファクトから `VideoSplitter-win64.zip` をダウンロードします。
4. ZIP 内には `VideoSplitter.exe`, `FFmpeg_LICENSE.txt`, `README.txt` が含まれます（展開して実行可能）。

> 備考: `build/build.ps1` や `build/video_splitter.spec` はローカルで PowerShell が利用できる環境向けに残していますが、GitHub Actions の利用が前提です。

### ローカルビルド (macOS)

1. `python3 -m pip install --upgrade pyinstaller` で PyInstaller をインストールします。
2. `third_party/ffmpeg/mac-universal/` に macOS 版の `ffmpeg`, `ffprobe`, `LICENSE.txt` を配置します。
3. `VideoSplitter` ディレクトリで `bash build/build_macos.sh` を実行します。

`dist/VideoSplitter-macos.zip` が生成され、macOS 用のバイナリと FFmpeg LICENSE、README がまとめて出力されます。

## 配布・ライセンス

- 配布物と同じフォルダに `ffmpeg` / `ffprobe` / `LICENSE.txt` (プラットフォームに応じた拡張子付き) が含まれるようにしてください。PyInstaller onefile 実行時は自動で一時展開されます。
- FFmpeg は LGPL/GPL ライセンスです。配布物には必ず使用したビルドの `LICENSE.txt` を同梱し、出典・バージョンを記載してください。
- 商用配布時は法務確認を実施し、SmartScreen 対策としてコード署名を推奨します。
