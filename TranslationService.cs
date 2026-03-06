using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GenericDeepL
{
    public class TranslationService
    {
        private readonly Settings _settings;
        private readonly HttpClient _httpClient;

        public TranslationService(Settings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();
            // タイムアウトを30秒に設定（デフォルトは100秒）
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private static Dictionary<string, string[]> _regionModelsCache;
        private static string _defaultRegion = "us-central1";
        private static string _defaultModel = "gemini-2.5-flash-lite";
        private static readonly object _regionModelsLock = new object();

        /// <summary>
        /// region-models.json の defaultRegion を返します。
        /// </summary>
        public static string GetDefaultRegion()
        {
            EnsureRegionModelsLoaded();
            return _defaultRegion;
        }

        /// <summary>
        /// region-models.json の defaultModel を返します。
        /// </summary>
        public static string GetDefaultModel()
        {
            EnsureRegionModelsLoaded();
            return _defaultModel;
        }

        /// <summary>
        /// region-models.json に定義されているリージョンIDの一覧を返します。
        /// </summary>
        public static string[] GetRegionIds()
        {
            EnsureRegionModelsLoaded();
            return _regionModelsCache.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// region-models.json を読み込み、指定リージョンで利用可能なモデル名の一覧を返します。
        /// </summary>
        public static string[] GetModelsForRegion(string region)
        {
            EnsureRegionModelsLoaded();
            if (string.IsNullOrWhiteSpace(region))
                region = _defaultRegion;
            if (_regionModelsCache.TryGetValue(region, out var models))
                return models;
            return Array.Empty<string>();
        }

        private static void EnsureRegionModelsLoaded()
        {
            lock (_regionModelsLock)
            {
                if (_regionModelsCache == null)
                    LoadRegionModelsJson();
            }
        }

        private static void LoadRegionModelsJson()
        {
            _regionModelsCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.Combine(baseDir, "region-models.json");
                if (!File.Exists(path))
                    return;

                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    if (prop.Name.Equals("defaultRegion", StringComparison.OrdinalIgnoreCase))
                    {
                        _defaultRegion = prop.Value?.ToString() ?? _defaultRegion;
                        continue;
                    }
                    if (prop.Name.Equals("defaultModel", StringComparison.OrdinalIgnoreCase))
                    {
                        _defaultModel = prop.Value?.ToString() ?? _defaultModel;
                        continue;
                    }
                    var arr = prop.Value as JArray;
                    if (arr == null) continue;
                    var list = new List<string>();
                    foreach (var t in arr)
                    {
                        var s = t?.ToString();
                        if (!string.IsNullOrWhiteSpace(s))
                            list.Add(s);
                    }
                    _regionModelsCache[prop.Name] = list.ToArray();
                }
            }
            catch (Exception)
            {
                // 読み込み失敗時はフォールバック値のまま
            }
        }

        public async Task<string> GetAvailableModelsAsync()
        {
            // Vertex AIではモデルリスト取得APIが異なるため、
            // 一般的に利用可能なモデルリストを返す
            return "Vertex AIで利用可能なモデル:\n" +
                   "- gemini-2.5-flash-lite\n" +
                   "- gemini-2.5-flash\n" +
                   "- gemini-2.5-pro\n" +
                   "- gemini-2.0-flash\n" +
                   "- gemini-2.0-flash-lite\n" +
                   "- gemini-1.5-flash\n" +
                   "- gemini-1.5-pro\n" +
                   "- gemini-pro";
        }

        public async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(_settings.GoogleApiKey))
            {
                throw new InvalidOperationException("Google Vertex AI API Key is not set. Please configure it in Settings.");
            }

            try
            {
                // 言語を検出
                bool isJapanese = IsJapanese(text);
                
                // 翻訳スタイルに応じたプロンプトを生成
                string styleInstruction = GetTranslationStyleInstruction(_settings.TranslationStyle);
                
                // プロンプト（systemInstructionで翻訳結果のみを返すように指示しているため、ここではシンプルに）
                string prompt = isJapanese
                    ? $"{styleInstruction}\n\n以下の文章を英語に翻訳してください：\n\n{text}"
                    : $"{styleInstruction}\n\n以下の文章を日本語に翻訳してください：\n\n{text}";

                // モデル名を取得（デフォルトはgemini-2.5-flash-lite - 最も高速）
                string modelName = string.IsNullOrWhiteSpace(_settings.ModelName) 
                    ? "gemini-2.5-flash-lite" 
                    : _settings.ModelName;
                
                // 古いモデル名を新しいモデル名にマッピング
                string actualModelName = MapLegacyModelName(modelName);
                
                // モデル名が空の場合はデフォルトを使用
                if (string.IsNullOrEmpty(actualModelName))
                {
                    actualModelName = "gemini-2.5-flash-lite"; // Vertex AIのデフォルト（最も高速）
                }
                
                // Vertex AI APIのエンドポイント（設定されたリージョンを使用）
                // ストリーミングAPIは実装が複雑なため、まず通常版で動作確認
                string region = string.IsNullOrWhiteSpace(_settings.Region) ? "asia-northeast1" : _settings.Region;
                string apiUrl = $"https://{region}-aiplatform.googleapis.com/v1/publishers/google/models/{actualModelName}:generateContent?key={_settings.GoogleApiKey}";

                // リクエストボディを作成（Vertex AI APIの形式に合わせる）
                // systemInstructionを使用して、翻訳結果のみを返すように指示
                string systemInstructionText = isJapanese
                    ? "あなたは翻訳専門のAIです。翻訳結果のみを出力してください。説明、補足、コメント、プレフィックスは一切不要です。翻訳された文章だけをそのまま出力してください。"
                    : "You are a translation AI. Output only the translation result. No explanations, notes, comments, or prefixes. Output only the translated text.";
                
                var requestBody = new
                {
                    systemInstruction = new
                    {
                        parts = new[]
                        {
                            new { text = systemInstructionText }
                        }
                    },
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // APIを呼び出し
                var response = await _httpClient.PostAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // APIキーをエラーメッセージから除外
                    string safeUrl = apiUrl.Substring(0, apiUrl.IndexOf("?key=") + 6) + "***";
                    return $"APIエラー: {response.StatusCode}\n\n使用したモデル: {actualModelName}\n使用したエンドポイント: {safeUrl}\n\nエラー詳細: {responseContent}";
                }

                // レスポンスを処理
                return ProcessNormalResponse(responseContent);
            }
            catch (Exception ex)
            {
                return $"エラー: {ex.Message}";
            }
        }


        private string MapLegacyModelName(string modelName)
        {
            // 古いモデル名を新しいモデル名にマッピング
            switch (modelName)
            {
                case "gemini-pro":
                case "gemini-1.5-pro":
                case "gemini-1.5-pro-latest":
                    return "gemini-2.5-pro";
                
                case "gemini-1.5-flash":
                case "gemini-1.5-flash-latest":
                    return "gemini-2.5-flash";
                
                default:
                    return modelName; // そのまま返す
            }
        }

        private string GetTranslationStyleInstruction(string style)
        {
            switch (style)
            {
                case "カジュアル":
                    return "カジュアルで親しみやすい文体で翻訳してください。";
                case "フォーマル":
                    return "フォーマルで丁寧な文体で翻訳してください。";
                case "厳密な直訳":
                    return "原文の構造や語順を可能な限り保持し、厳密に直訳してください。";
                case "自然さを優先した翻訳":
                    return "自然で流暢な表現を優先し、読み手にとって最も自然な翻訳にしてください。";
                case "標準":
                default:
                    return "自然で読みやすい標準的な文体で翻訳してください。";
            }
        }

        private string ProcessNormalResponse(string responseContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(responseContent))
                {
                    return "エラー: レスポンスが空です。";
                }

                var jsonResponse = JObject.Parse(responseContent);
                var candidates = jsonResponse["candidates"] as JArray;

                if (candidates == null || candidates.Count == 0)
                {
                    // エラー情報を確認
                    var error = jsonResponse["error"];
                    if (error != null)
                    {
                        var errorMessage = error["message"]?.ToString() ?? "不明なエラー";
                        return $"APIエラー: {errorMessage}";
                    }
                    
                    return $"翻訳に失敗しました。レスポンス: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                }

                var contentParts = candidates[0]?["content"]?["parts"] as JArray;
                if (contentParts == null || contentParts.Count == 0)
                {
                    return $"翻訳に失敗しました。candidatesは存在しますが、content/partsが見つかりません。レスポンス: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                }

                var translatedText = contentParts[0]?["text"]?.ToString();
                if (string.IsNullOrEmpty(translatedText))
                {
                    return $"翻訳に失敗しました。textフィールドが空です。レスポンス: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}";
                }

                return translatedText;
            }
            catch (JsonException ex)
            {
                return $"JSONパースエラー: {ex.Message}\n\nレスポンス: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}";
            }
            catch (Exception ex)
            {
                return $"レスポンス処理エラー: {ex.Message}\n\nレスポンス: {responseContent.Substring(0, Math.Min(500, responseContent.Length))}";
            }
        }

        private bool IsJapanese(string text)
        {
            // 日本語文字（ひらがな、カタカナ、漢字）が含まれているかチェック
            var japanesePattern = new Regex(@"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF]");
            return japanesePattern.IsMatch(text);
        }
    }
}
