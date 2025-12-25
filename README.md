# VideoSplitter

Windows 向けの WPF アプリケーションです。大容量 MP4 を「指定した最大サイズ (GB)」および「最大再生時間 (分)」を満たすように自動で分割します。既定値は 1.5 GB / 50 分です。

## 主な機能

- FFmpeg/ffprobe を同梱フォルダーから自動検出し、存在しない場合は環境 PATH から検索
- ffprobe による入力ファイル解析と SplitPlan に基づく自動分割
- `-c copy` による高速セグメント分割、しきい値超過したパートのみ H.264/AAC で再エンコード
- 動画から音声 (MP3) だけを抽出するボタンを GUI から実行可能
- 出力ファイル名は元のファイル名を含む連番 (`<元名>_part_01.mp4` など)
- 非同期処理で UI スレッドをブロックしない進捗表示とキャンセル
- ログは UI と `%LOCALAPPDATA%\VideoSplitter\logs\app.log` へ出力 (1MB ローテーション)
- ドラッグ＆ドロップ、フォルダーを開くボタン、しきい値スピンボックス

## 使い方

1. `third_party/ffmpeg/win-x64/` に FFmpeg の Windows x64 バイナリ (`ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt`) を配置します。
2. `dotnet restore && dotnet build` を実行し、WPF アプリをビルドします。
3. アプリを起動し、対象の MP4 をドラッグ＆ドロップまたは [参照] から選択します。
4. 出力フォルダー、最大サイズ (GB)、最大時間 (分) を必要に応じて変更します。
5. [実行] ボタンを押すと分割処理が始まります。処理中は進捗バーとログで状況を確認できます。
6. 処理完了後はログに生成ファイル名が表示されます。[フォルダーを開く] ボタンで出力フォルダーを開けます。
7. Gemini などクラウドサービスへアップロードする場合は、`part_01.mp4` から順番にアップロードすると視聴順序を保てます。

## 既知の制限

- 入力が極端な可変ビットレートの場合、`-c copy` 分割後にサイズが上限をわずかに超えることがあります。その場合は自動で再エンコードします。
- `-c copy` 分割はキーフレーム境界に依存するため、稀にチャプターまたはフレーム境界がずれます。音ズレが気になる場合は設定値を調整するか、再エンコード結果を利用してください。
- 入力ファイルが DRM 保護または破損している場合は処理に失敗します。

## FFmpeg のバージョンとライセンス

- 本プロジェクトは FFmpeg の公式ビルドを同梱することを前提としています。
- 配布時は `third_party/ffmpeg/win-x64/` に配置したバージョンと出典 URL を README や配布物に明記してください。
- FFmpeg は LGPL/GPL ライセンスに従います。ライセンス文書 (`LICENSE.txt`) の同梱と、ソース取得方法の案内が必要です。商用利用時は法務部門に確認してください。

## ビルド手順

```powershell
# 依存バイナリ配置
# third_party/ffmpeg/win-x64/ffmpeg.exe などを配置

# ビルド
cd src/VideoSplitter.App
 dotnet restore
 dotnet build
```

## 発行 (Publish)

```powershell
# ルートで実行
pwsh .\build\publish.ps1 -Configuration Release -Runtime win-x64
```

- 発行後、`src/VideoSplitter.App/bin/Release/net8.0-windows/win-x64/publish/` に自己完結に近い配布物が作成されます。FFmpeg バイナリも同フォルダーへコピーしてください。
- `dotnet publish` には `PublishSingleFile=true` と `PublishReadyToRun=true` を指定しています。必要に応じて `--self-contained true` を追加してください。

## 配布とライセンス注意

- 配布物にはアプリ本体、`ffmpeg.exe`, `ffprobe.exe`, `LICENSE.txt` を含めてください。
- FFmpeg のライセンスに従い、対応するソース入手方法を案内してください。
- 可能であればコード署名 (`signtool`) を実施して信頼性を高めてください。

## トラブルシューティング

- **FFmpeg が見つからない**: `third_party/ffmpeg/win-x64/` にバイナリを配置し直すか、PATH を確認してください。
- **ファイル書き込みエラー**: 出力フォルダーの書き込み権限や空き容量を確認してください。
- **長いパスで失敗する**: Windows のパス長制限が原因の場合があります。短いパスに移動して実行してください。
- **キャンセルが効かない**: 数秒遅れて停止します。停止しない場合はアプリを終了してください。

## Python / Tkinter 版 (Windows / macOS)

WPF 版とは別に、`VideoSplitter/` ディレクトリ以下に Python / Tkinter 版の GUI があります。こちらは PyInstaller でスタンドアロン バイナリを作成でき、Windows と macOS の両方に対応しています。詳細は `VideoSplitter/README.md` を参照してください。ここでは macOS 向けビルド手順のみ抜粋します。

### macOS ワンファイルビルド

1. macOS 用の FFmpeg バイナリ (`ffmpeg`, `ffprobe`, `LICENSE.txt`) を入手し、`VideoSplitter/third_party/ffmpeg/mac-universal/` に配置します。Homebrew (`brew install ffmpeg`) や [https://evermeet.cx/ffmpeg/](https://evermeet.cx/ffmpeg/) などの配布サイトを利用できます。
2. PyInstaller をインストールします。

   ```bash
   cd VideoSplitter
   python3 -m pip install --upgrade pyinstaller
   ```

3. ワンファイル パッケージを生成します。

   ```bash
   cd VideoSplitter
   bash build/build_macos.sh
   ```

`dist/VideoSplitter-macos.zip` が作成され、中に macOS 実行ファイル (`VideoSplitter`)、README、FFmpeg ライセンスが含まれます。配布時は notarize / Gatekeeper 対応のために署名・公証を行うことを推奨します。
