# GenericDeepL ビルド・実行

## 前提条件

- Windows 10 以降
- .NET SDK（ビルド時のみ。MSI 配布版には .NET ランタイムが同梱）

---

## Release ビルド

クリーンビルド（ソース変更後は必ずこちら）:

```powershell
dotnet restore GenericDeepL.csproj
dotnet clean GenericDeepL.csproj -c Release
.\build-msi.ps1 -Configuration Release
```

通常ビルド:

```powershell
.\build-msi.ps1 -Configuration Release
```

出力: `bin\Release\GenericDeepL.msi`

### 実行

MSI でインストールして起動（推奨）:
1. 起動中の GenericDeepL を終了する
2. `bin\Release\GenericDeepL.msi` をダブルクリックしてインストール
3. スタートメニューまたはインストール先の `GenericDeepL.exe` から起動

exe を直接実行:

```powershell
.\bin\Release\net6.0-windows\GenericDeepL.exe
```

---

## Debug ビルド

```powershell
dotnet build GenericDeepL.csproj -c Debug
dotnet run --project GenericDeepL.csproj -c Debug
```

---

## API キー（Google Vertex AI）

API キーは `settings.json` には保存されません。

| ビルド | 保存先 |
|---|---|
| Debug | プロジェクト直下の `.env`（`GOOGLE_API_KEY=...`） |
| Release | `%AppData%\GenericDeepL\api_key.dat`（Windows DPAPI 暗号化） |

Debug の初回セットアップ:

```powershell
cp .env.example .env
# .env を編集して GOOGLE_API_KEY を設定
```

Release では、設定画面で API キーを入力して保存すると DPAPI で暗号化保存され、以降は自動で読み込まれます。

---

## MSI の配布

配布するファイルは `bin\Release\GenericDeepL.msi` のみです。受け取った側は MSI をダブルクリックしてインストール後、設定画面で各自の API キーを入力してください。
