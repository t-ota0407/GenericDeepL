# GenericDeepL ビルド・実行・配布

このファイルがビルド手順の唯一の正です。ビルドに関する記述はここに集約し、他のファイルには複製しないでください。

---

## 前提条件

### ビルドする PC

- Windows 10 以降
- .NET 6.0 SDK

### 実行する PC

- Windows 10 以降
- **.NET Desktop Runtime (x64)** — [ダウンロード](https://dotnet.microsoft.com/download/dotnet/6.0)

> **MSI に .NET ランタイムは同梱されていません。**
> `build-msi.ps1` はフレームワーク依存構成（`dotnet publish` に `--self-contained` を付けない）で
> ビルドしているため、実行する PC 側に .NET Desktop Runtime が必要です。
> インストーラーはランタイムの有無を確認しません。未導入の PC では
> インストール自体は成功しますが、起動時に .NET のインストールを促すダイアログが出ます。
>
> なお `RollForward=Major` を指定しているため、.NET 6.0 が無くても 8.0 以降の
> .NET Desktop Runtime が入っていれば起動します（6.0 があれば 6.0 が優先されます）。

---

## ビルド

| 目的 | コマンド |
|---|---|
| Debug ビルド | `dotnet build GenericDeepL.csproj -c Debug` |
| Release ビルド | `dotnet build GenericDeepL.csproj -c Release` |
| MSI インストーラー | `.\build-msi.ps1` |
| MSI（クリーンビルド） | `.\build-msi.ps1 -Clean` |

`build-msi.ps1` は既定で Release を対象にします。Debug の MSI が必要な場合は
`-Configuration Debug` を付けてください。

**ソースを変更したあとは `-Clean` を付けたビルドを推奨します。**
`-Clean` なしの場合、出力フォルダを削除せず `dotnet publish` で上書きするため、
削除したファイルが出力に残ることがあります。

MSI のビルドには WiX Toolset 6 が必要ですが、NuGet 経由で自動的に復元されるため
個別のインストールは不要です。

### 出力先

| 成果物 | パス |
|---|---|
| 実行ファイル（Debug） | `bin\Debug\net6.0-windows\GenericDeepL.exe` |
| 実行ファイル（Release） | `bin\Release\net6.0-windows\GenericDeepL.exe` |
| MSI インストーラー | `bin\Release\GenericDeepL.msi` |

### ビルド前の注意

GenericDeepL が起動中だと、実行ファイルを上書きできずビルドに失敗することがあります。
先にトレイアイコンを右クリックして「終了」してください。

---

## 実行

MSI でインストールして起動（推奨）:

1. 起動中の GenericDeepL を終了する
2. `bin\Release\GenericDeepL.msi` をダブルクリックしてインストール
3. スタートメニューまたはインストール先の `GenericDeepL.exe` から起動

exe を直接実行:

```powershell
.\bin\Release\net6.0-windows\GenericDeepL.exe
```

Debug ビルドを実行:

```powershell
dotnet run --project GenericDeepL.csproj -c Debug
```

---

## API キー（Google Vertex AI）

API キーは `settings.json` には保存されません。

| 用途 | 場所 |
|---|---|
| 保存先（Debug/Release 共通） | `%AppData%\GenericDeepL\api_key.dat`（Windows DPAPI 暗号化） |
| 読み込みのみ（Debug のみ） | プロジェクト直下の `.env`（`GOOGLE_API_KEY=...`） |

アプリから `.env` へ書き込むことはありません。設定画面で保存したキーは DPAPI 暗号化ファイルにのみ
保存されます（`.env` は DPAPI 側が空のときのフォールバックとして読まれるだけです）。

Debug の初回セットアップ:

```powershell
cp .env.example .env
# .env を編集して GOOGLE_API_KEY を設定
```

`.env` は `.gitignore` で除外済みです。**絶対にコミットしないでください。**

Release では、設定画面で API キーを入力して保存すると DPAPI で暗号化保存され、以降は自動で読み込まれます。

---

## MSI の配布

配布するファイルは `bin\Release\GenericDeepL.msi` のみです。受け取った側は MSI をダブルクリックして
インストール後、設定画面で各自の API キーを入力してください。

受け取る側の PC に **.NET Desktop Runtime (x64)** が必要です（MSI には同梱されていません）。
インストーラーは有無を確認しないため、先にランタイムを入れてもらうよう案内してください。

### バージョンを上げるとき

`GenericDeepL.wxs` の `Version` と `ProductCode` を両方変更してください。`Version` が同じままだと
`MajorUpgrade` が働かず、旧版が置き換わりません。同一バージョンで入れ替える場合は、
先にコントロールパネルからアンインストールしてください。
