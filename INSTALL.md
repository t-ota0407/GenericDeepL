# GenericDeepL ビルド・実行

## Release ビルドと実行

### ビルド

**修正を反映した新しいビルドを作る場合**（前回ビルドからソースを変更したとき）は、先にクリーンしてからビルドしてください。

```powershell
dotnet restore GenericDeepL.csproj
dotnet clean GenericDeepL.csproj -c Release
.\build-msi.ps1 -Configuration Release
```

※ `dotnet clean` で「資産ファイルに 'net6.0-windows' のターゲットがありません」(NETSDK1005) が出る場合は、先に `dotnet restore GenericDeepL.csproj` を実行してから再度 clean してください。（同一フォルダに WiX 用 wixproj があるため、`obj\project.assets.json` が上書きされると発生します。）

※ **Debug と Release で表示が違う場合**: 設定画面の並び（Region → Model など）は同じソースなので、両方で同じになります。Release で古い表示のままなら、**Release をクリーンビルド**したうえで、`bin\Release\net6.0-windows\GenericDeepL.exe` を**直接実行**して確認してください。MSI でインストールした exe を使っている場合は、新しい MSI で**再インストール**すると最新の UI になります。

通常のビルドのみ:

```powershell
.\build-msi.ps1 -Configuration Release
```

- アプリケーションを **dotnet publish** でデプロイ用にビルドし、続けて MSI インストーラーをビルドします。
- 出力: `bin\Release\GenericDeepL.msi`

### 実行

**MSI でインストールして実行する場合**

1. **GenericDeepL が起動中なら終了しておく**（新しい exe を上書きするため）
2. `bin\Release\GenericDeepL.msi` を実行してインストール
3. スタートメニューまたはインストール先の `GenericDeepL.exe` から起動

**ビルド出力をそのまま実行する場合**

```powershell
.\bin\Release\net6.0-windows\GenericDeepL.exe
```

---

## Debug ビルドと実行

### ビルド

```powershell
dotnet build GenericDeepL.csproj -c Debug
```

### 実行

```powershell
dotnet run --project GenericDeepL.csproj -c Debug
```

または、ビルド後の exe を直接実行:

```powershell
.\bin\Debug\net6.0-windows\GenericDeepL.exe
```

---

## API キー（Google Vertex AI）

- **API キーはプログラム内（settings.json）には保存されません。**

### Debug ビルド

- 起動時に **`.env`** から `GOOGLE_API_KEY` を読み込みます。
- 設定画面で API キーを保存すると、**`.env`** に書き込まれます。
- 初回はプロジェクト直下に `.env.example` をコピーして `.env` を作成し、`GOOGLE_API_KEY=あなたのキー` を設定してください。Debug ビルド時は `.env` が出力フォルダにコピーされます（存在する場合）。

### Release ビルド

- `.env` は読み書きしません。
- 設定で保存した API キーは **Windows DPAPI** で暗号化され、`%AppData%\GenericDeepL\api_key.dat` に保存されます。同じ Windows ユーザーでログインしている場合のみ復号できるため、安全に保持されます。一度保存すれば次回起動時も自動で読み込まれます。

---

## 他人に共有する場合（Release MSI の配布）

1. **MSI をビルドする**
   ```powershell
   dotnet clean GenericDeepL.csproj -c Release
   .\build-msi.ps1 -Configuration Release
   ```
2. **共有するファイル**
   - 共有するのは **`bin\Release\GenericDeepL.msi`** のみで十分です。
3. **渡し方**
   - USB、OneDrive / Google Drive / Dropbox などのクラウド、メール添付など、任意の方法で `GenericDeepL.msi` を渡してください。
4. **受け取った人側**
   - Windows 10 以降の PC で `GenericDeepL.msi` をダブルクリックしてインストール。
   - .NET の別途インストールは不要（ランタイムは MSI に含まれています）。
   - 初回起動後、設定画面で **各自の Google Vertex AI 用 API キー** を入力して保存してください。一度保存すると DPAPI で暗号化されて安全に保存され、次回以降は自動で読み込まれます。

---

## 前提条件

- Windows 10 以降
- .NET SDK（ビルド時）。MSI インストール後は .NET ランタイムはアプリに同梱されるため不要。
