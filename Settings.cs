using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenericDeepL
{
    public class Settings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GenericDeepL");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
        private static readonly string SecureApiKeyFilePath = Path.Combine(SettingsDirectory, "api_key.dat");

        /// <summary>APIキーはsettings.jsonに保存せず、Debug時は.env、Release時はDPAPI暗号化ファイルで保存します。</summary>
        [JsonIgnore]
        public string GoogleApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "";
        public string TranslationStyle { get; set; } = "標準";
        public string Region { get; set; } = "";
        public bool RunAtStartup { get; set; } = true; // スタートアップ実行設定（標準で有効）

        public static Settings Load()
        {
            Settings? loaded = null;
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    loaded = JsonSerializer.Deserialize<Settings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrEmpty(loaded.Region))
                            loaded.Region = TranslationService.GetDefaultRegion();
                        if (string.IsNullOrEmpty(loaded.ModelName))
                            loaded.ModelName = TranslationService.GetDefaultModel();
                    }
                }
            }
            catch (Exception)
            {
                // エラー時はデフォルト設定を返す
            }

            if (loaded == null)
            {
                loaded = new Settings
                {
                    Region = TranslationService.GetDefaultRegion(),
                    ModelName = TranslationService.GetDefaultModel()
                };
            }

            // 常に DPAPI で暗号化した安全な保存場所から読み込む（Debug/Release 共通）
            var keyFromSecure = LoadApiKeyFromSecureStorage();
            if (!string.IsNullOrEmpty(keyFromSecure))
                loaded.GoogleApiKey = keyFromSecure;
#if DEBUG
            // Debug 時は .env も参照（開発用。secure が空なら .env の値を使う）
            if (string.IsNullOrEmpty(loaded.GoogleApiKey))
            {
                var keyFromEnv = LoadApiKeyFromEnv();
                if (!string.IsNullOrEmpty(keyFromEnv))
                    loaded.GoogleApiKey = keyFromEnv;
            }
#endif

            return loaded;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);

                // 常に DPAPI で暗号化して安全な場所に保存（Debug/Release 共通）
                SaveApiKeyToSecureStorage(GoogleApiKey);
#if DEBUG
                // Debug 時は .env にも書き込む（開発用）
                SaveApiKeyToEnv(GoogleApiKey);
#endif
            }
            catch (Exception)
            {
                // エラーハンドリング（必要に応じてログ出力）
            }
        }

        private static string? LoadApiKeyFromEnv()
        {
            var path = Path.Combine(AppContext.BaseDirectory, ".env");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line.Substring(0, i).Trim();
                if (key.Equals("GOOGLE_API_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring(i + 1).Trim().Trim('"').Trim('\'');
                    return value;
                }
            }
            return null;
        }

        private static void SaveApiKeyToEnv(string apiKey)
        {
            var path = Path.Combine(AppContext.BaseDirectory, ".env");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            var found = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("GOOGLE_API_KEY=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "GOOGLE_API_KEY=" + apiKey;
                    found = true;
                    break;
                }
            }
            if (!found) lines.Add("GOOGLE_API_KEY=" + apiKey);
            File.WriteAllLines(path, lines);
        }

        private static string? LoadApiKeyFromSecureStorage()
        {
            try
            {
                if (!File.Exists(SecureApiKeyFilePath)) return null;
                var encrypted = File.ReadAllBytes(SecureApiKeyFilePath);
                if (encrypted.Length == 0) return null;
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SaveApiKeyToSecureStorage(string apiKey)
        {
            try
            {
                if (!Directory.Exists(SettingsDirectory))
                    Directory.CreateDirectory(SettingsDirectory);
                var plain = Encoding.UTF8.GetBytes(apiKey ?? string.Empty);
                var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(SecureApiKeyFilePath, encrypted);
            }
            catch (Exception ex)
            {
                // 保存失敗時はユーザーに通知（APIキーが永続化されないため）
                System.Windows.MessageBox.Show(
                    $"APIキーを安全な場所に保存できませんでした。\n\n{ex.Message}",
                    "設定の保存",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}
