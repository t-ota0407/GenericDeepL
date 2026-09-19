using System;
using System.IO;
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

        /// <summary>確認なしで送信できる文字数の既定値。</summary>
        public const int DefaultConfirmThresholdLength = 20000;
        /// <summary>しきい値として設定可能な下限。</summary>
        public const int MinConfirmThresholdLength = 100;
        /// <summary>しきい値として設定可能な上限。</summary>
        public const int MaxConfirmThresholdLength = 200000;

        /// <summary>
        /// APIキーはsettings.jsonに保存せず、DPAPIで暗号化したファイルに保存します。
        /// Debugビルドでは開発用に .env からの「読み込みのみ」行います（書き込みはしません）。
        /// </summary>
        [JsonIgnore]
        public string GoogleApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "";
        public string TargetLanguage { get; set; } = "自動";
        public bool RunAtStartup { get; set; } = true; // スタートアップ実行設定（標準で有効）

        /// <summary>
        /// この文字数を超えたら送信前に確認ダイアログを出す。
        /// クリップボードの内容は外部（Google）へ送信されるため、巨大なテキストを
        /// 意図せず送ってしまわないよう、しきい値を超えたときだけ確認をはさむ。
        /// </summary>
        public int ConfirmThresholdLength { get; set; } = DefaultConfirmThresholdLength;

        /// <summary>しきい値を設定可能な範囲に収める。</summary>
        public static int ClampConfirmThresholdLength(int value)
        {
            if (value < MinConfirmThresholdLength) return MinConfirmThresholdLength;
            if (value > MaxConfirmThresholdLength) return MaxConfirmThresholdLength;
            return value;
        }

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
                        if (string.IsNullOrEmpty(loaded.ModelName))
                            loaded.ModelName = TranslationService.GetDefaultModel();
                        // 手編集などで範囲外の値が入っていても安全側に倒す
                        loaded.ConfirmThresholdLength = ClampConfirmThresholdLength(loaded.ConfirmThresholdLength);
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

                // APIキーは常に DPAPI で暗号化して保存する（Debug/Release 共通）。
                // Debug でも .env へは書き戻さない（平文がビルド出力に残るのを防ぐため）。
                SaveApiKeyToSecureStorage(GoogleApiKey);
            }
            catch (Exception)
            {
                // エラーハンドリング（必要に応じてログ出力）
            }
        }

        /// <summary>
        /// Debugビルド専用。開発用に .env から APIキーを読み込む（読み取り専用。書き込みは行わない）。
        /// </summary>
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
