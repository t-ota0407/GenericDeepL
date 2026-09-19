# GenericDeepL - Claude Code 開発ガイド

## プロジェクト概要

Windows のシステムトレイに常駐する翻訳ツール。`Ctrl+C` を素早く2回押すとクリップボードの内容を自動翻訳して表示する。Google Vertex AI (Gemini モデル) を使って日英/英日を自動判定して翻訳する。

- **プラットフォーム**: Windows のみ（WPF / .NET 6.0）
- **翻訳エンジン**: Google Vertex AI (Gemini) — グローバルエンドポイント固定
- **APIキー保存**: Windows DPAPI で暗号化（Debug/Release 共通）。Debug のみ `.env` からの読み込みにも対応（書き込みはしない）
- **インストーラー**: WiX Toolset 6 で MSI を生成

## ファイル構成

| ファイル | 役割 |
|---|---|
| `MainWindow.xaml/.cs` | メインウィンドウ、システムトレイアイコン、翻訳フロー制御 |
| `SettingsWindow.xaml/.cs` | 設定画面 UI |
| `TranslationService.cs` | Google Vertex AI API 呼び出し、レスポンス処理 |
| `GlobalHotKeyHook.cs` | Win32 低レベルキーボードフック（Ctrl+C x2 検出） |
| `Settings.cs` | 設定の読み書き、DPAPI によるAPIキー暗号化 |
| `StartupManager.cs` | Windows スタートアップ（レジストリ）登録・解除 |
| `models.json` | 選択可能な Gemini モデルの一覧と既定モデル |
| `build-msi.ps1` | MSI インストーラービルドスクリプト |
| `.env.example` | 開発用 `.env` のテンプレート |

## ビルド・実行方法

ビルド手順・前提条件・実行方法・APIキーのセットアップ・MSI の配布は
**[INSTALL.md](INSTALL.md) を唯一の正とする。ここには複製しないこと**（二重管理でズレるため）。

ビルドや実行が必要になったら INSTALL.md を読むこと。
ビルド手順を変更した場合も INSTALL.md だけを更新する。

## コーディング規約

- **言語**: コメント・変数名・エラーメッセージはすべて日本語を優先する
- **スタイル**: Microsoft 標準の C# コーディング規約（[公式ガイドライン](https://learn.microsoft.com/ja-jp/dotnet/csharp/fundamentals/coding-style/coding-conventions)）に従う
  - クラス・メソッド・プロパティ: PascalCase
  - ローカル変数・引数: camelCase
  - プライベートフィールド: `_camelCase`（アンダースコアプレフィックス）
- **非同期処理**: UI スレッドへのアクセスは必ず `Dispatcher.Invoke` を使う
- **例外処理**: ユーザーに見せる必要のないエラーは握りつぶさず、ログまたはメッセージで通知する

## 重要な制約・注意事項

- `.env` ファイルは `.gitignore` で除外済み。**絶対にコミットしないこと**
- アプリから `.env` へ書き込まないこと（平文のAPIキーがビルド出力に残るため）。
  APIキーの保存先は DPAPI 暗号化ファイルのみで、`.env` は Debug 時の読み込み専用
- `Settings.GoogleApiKey` は `[JsonIgnore]` のため `settings.json` には保存されない。APIキーは DPAPI 経由でのみ永続化される
- APIキーは URL のクエリ文字列ではなく `x-goog-api-key` ヘッダーで送る。URL はログや例外メッセージに残りやすいため
- 翻訳の指示は必ず `systemInstruction` に置き、`contents` のユーザーターンには原文だけを入れる。
  指示と原文を同じテキストに連結すると、クリップボード由来の文が指示として解釈される（プロンプトインジェクション）
- クリップボードの内容は外部に送信される。`Settings.ConfirmThresholdLength` を超えたら送信前に確認する。
  トレイの「クリップボード監視を一時停止」でも止められる
- 翻訳は `MainWindow.RunTranslationAsync` に一本化されている。新しい要求が来たら古い要求を
  `CancellationTokenSource` でキャンセルし、古い結果が新しい結果を上書きしないようにしている。
  `TranslateAsync` はキャンセル時に `OperationCanceledException` を投げ、タイムアウト時は
  エラー文字列を返す（両者を `cancellationToken.IsCancellationRequested` で区別している）
- 多重起動すると低レベルキーボードフックが二重に刺さる。`App` の名前付き Mutex で抑止している
- `GlobalHotKeyHook` は Win32 低レベルフックを使用するため、フックコールバック内では重い処理をしないこと
- WPF は Windows 専用。クロスプラットフォーム対応は想定していない

## テスト

現時点ではテストプロジェクトなし。将来的に追加する場合は xUnit を推奨。

## よくある作業パターン

### 翻訳ロジックの変更
`TranslationService.cs` の `TranslateAsync` メソッドを修正する。翻訳の指示は `BuildSystemInstruction` が組み立てて
`systemInstruction` に入れ、`contents` のユーザーターンには原文だけを渡す（この分離を崩さないこと）。

### 新しいモデルの追加
`models.json` の `models` 配列に追加する。`defaultModel` キーで既定値を指定できる。

**リージョンは `global` 固定**（設定項目にしていない）。Gemini 3 系の flash-lite は
グローバルエンドポイントでしか提供されておらず、地域リージョンを選べるようにすると
存在しないモデルを指して 404 になる組み合わせが生まれるため。エンドポイントは
`TranslationService.ApiEndpointBase` に定数として置いている。

設定に保存されたモデル名が `models.json` の一覧に無い場合は、`ResolveModelName` が
既定モデルにフォールバックする（古い設定を引き継いだときに壊れないようにするため）。

### 設定項目の追加
1. `Settings.cs` にプロパティを追加
2. `SettingsWindow.xaml/.cs` に UI を追加
3. `TranslationService.cs` で参照する
