# GenericDeepL - Claude Code 開発ガイド

## プロジェクト概要

Windows のシステムトレイに常駐する翻訳ツール。`Ctrl+C` を素早く3回押すとクリップボードの内容を自動翻訳して表示する。Google Vertex AI (Gemini モデル) を使って日英/英日を自動判定して翻訳する。

- **プラットフォーム**: Windows のみ（WPF / .NET 6.0）
- **翻訳エンジン**: Google Vertex AI (Gemini)
- **APIキー保存**: Windows DPAPI で暗号化（Release）、`.env` ファイル併用（Debug）
- **インストーラー**: WiX Toolset 6 で MSI を生成

## ファイル構成

| ファイル | 役割 |
|---|---|
| `MainWindow.xaml/.cs` | メインウィンドウ、システムトレイアイコン、翻訳フロー制御 |
| `SettingsWindow.xaml/.cs` | 設定画面 UI |
| `TranslationService.cs` | Google Vertex AI API 呼び出し、レスポンス処理 |
| `GlobalHotKeyHook.cs` | Win32 低レベルキーボードフック（Ctrl+C x3 検出） |
| `Settings.cs` | 設定の読み書き、DPAPI によるAPIキー暗号化 |
| `StartupManager.cs` | Windows スタートアップ（レジストリ）登録・解除 |
| `region-models.json` | 利用可能なリージョンとGeminiモデルの一覧 |
| `build-msi.ps1` | MSI インストーラービルドスクリプト |
| `.env.example` | 開発用 `.env` のテンプレート |

## ビルド・実行方法

### 前提条件
- .NET 6.0 SDK がインストールされていること
- Windows 環境であること（WPF のため）

### Debug ビルド（開発時）

```bash
dotnet build GenericDeepL.csproj -c Debug
```

実行ファイルは `bin/Debug/net6.0-windows/GenericDeepL.exe` に出力される。

### Release ビルド

```bash
dotnet build GenericDeepL.csproj -c Release
```

### MSI インストーラーのビルド

```powershell
.\build-msi.ps1
```

MSI は `bin/Release/GenericDeepL.msi` に出力される。

## 開発環境のセットアップ

APIキーをデバッグ時に使うには `.env` ファイルを作成する：

```bash
cp .env.example .env
# .env を編集して GOOGLE_API_KEY を設定
```

Debug ビルドでは `.env` がビルド出力ディレクトリにコピーされ、アプリ起動時に読み込まれる。Release ビルドでは `.env` は使用されない（DPAPI のみ）。

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
- `Settings.GoogleApiKey` は `[JsonIgnore]` のため `settings.json` には保存されない。APIキーは DPAPI 経由でのみ永続化される
- `GlobalHotKeyHook` は Win32 低レベルフックを使用するため、フックコールバック内では重い処理をしないこと
- WPF は Windows 専用。クロスプラットフォーム対応は想定していない

## テスト

現時点ではテストプロジェクトなし。将来的に追加する場合は xUnit を推奨。

## よくある作業パターン

### 翻訳ロジックの変更
`TranslationService.cs` の `TranslateAsync` メソッドを修正する。プロンプトは `systemInstruction` と `contents` の両方で構成されており、翻訳スタイルは `GetTranslationStyleInstruction` で管理している。

### 新しいモデル・リージョンの追加
`region-models.json` を編集する。`defaultRegion` と `defaultModel` キーでデフォルト値を設定できる。古いモデル名のマッピングは `TranslationService.MapLegacyModelName` で管理している。

### 設定項目の追加
1. `Settings.cs` にプロパティを追加
2. `SettingsWindow.xaml/.cs` に UI を追加
3. `TranslationService.cs` で参照する
