using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Providers
{
    public static class Gemini_Service
    {
        private static readonly string apiUrlBase = "https://generativelanguage.googleapis.com/v1beta/models/";
        private static readonly HttpClient client = new HttpClient();

        private static string GetAPIKey()
        {
            var key = ChatConfig.GetApiKey("Gemini");
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[Gemini_Service] 无法读取 Gemini API Key，请在 Resources/APISettings.asset 配置。");
            }
            return key;
        }

        // 统一接口：使用 StreamingEnabled 自动选择流式或非流式
        public static async Task<string> GetAIResponse(List<ChatMessage> chatHistory)
        {
            string apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return "{\"dialogue\":\"请在 APISettings 配置 Gemini API 密钥\", \"emotion\":\"Sad\", \"action\":\"\"}";
            }

            var streaming = ChatConfig.GetStreamingEnabled("Gemini", false);
            if (!streaming)
            {
                return await SendOnce(apiKey, chatHistory);
            }

            var sb = new StringBuilder();
            await SendStream(apiKey, chatHistory,
                onDelta: d => { if (!string.IsNullOrEmpty(d)) sb.Append(d); },
                onCompleted: _ => { },
                onError: _ => { },
                ct: CancellationToken.None);
            return sb.ToString();
        }

        // 非流式：一次性返回
        private static async Task<string> SendOnce(string apiKey, List<ChatMessage> chatHistory)
        {
            // ? 从配置读取模型名称
            string model = ChatConfig.GetModel("Gemini", "gemini-2.0-flash-exp");
            float temperature = ChatConfig.GetTemperature("Gemini", 1.0f);
            int maxOutputTokens = ChatConfig.GetMaxTokens("Gemini", 8192);

            // ? 动态构建 API URL
            string apiUrl = $"{apiUrlBase}{model}:generateContent";
            
            Debug.Log($"[Gemini] 发送请求(非流式) - 模型: {model}, 温度: {temperature}, MaxTokens: {maxOutputTokens}");

            client.DefaultRequestHeaders.Remove("x-goog-api-key");
            client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

            // 将通用结构转换为 Gemini 的 contents/parts
            var geminiContents = new List<GeminiContent>();
            if (chatHistory != null)
            {
                foreach (var m in chatHistory)
                {
                    if (m == null) continue;
                    var parts = new List<GeminiPart>();
                    if (m.parts != null)
                    {
                        foreach (var p in m.parts)
                        {
                            if (p == null) continue;
                            parts.Add(new GeminiPart { text = p.text });
                        }
                    }
                    geminiContents.Add(new GeminiContent { role = m.role, parts = parts });
                }
            }

            var requestData = new GeminiRequest
            {
                contents = geminiContents,
                generationConfig = new GeminiGenerationConfig { temperature = temperature, maxOutputTokens = maxOutputTokens }
            };

            string jsonBody = JsonUtility.ToJson(requestData);

            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var geminiResponse = JsonUtility.FromJson<GeminiResponse>(responseBody);

                    if (geminiResponse != null && geminiResponse.candidates != null && geminiResponse.candidates.Count > 0)
                    {
                        string aiContent = geminiResponse.candidates[0].content.parts[0].text;
                        Debug.Log($"[Gemini_Service] AI 响应: {aiContent}");
                        return aiContent;
                    }
                    return "{\"dialogue\":\"AI返回了空内容，可能因安全或其它原因\", \"emotion\":\"Thinking\", \"action\":\"\"}";
                }
                else
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"[Gemini_Service] API 错误 {response.StatusCode}: {errorBody}");
                    return $"{{\"dialogue\":\"Gemini API错误: {response.StatusCode}. {errorBody}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gemini_Service] 异常: {e.Message}");
                return $"{{\"dialogue\":\"请求异常: {e.Message}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
            }
        }

        // 流式：SSE 增量读取
        public static async Task SendStream(
            string apiKey,
            List<ChatMessage> chatHistory,
            Action<string> onDelta,
            Action<string> onCompleted,
            Action<string> onError,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(apiKey)) apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                onError?.Invoke("Gemini API Key 未配置");
                return;
            }

            // 从配置读取模型名称
            string model = ChatConfig.GetModel("Gemini", "gemini-2.0-flash-exp");
            float temperature = ChatConfig.GetTemperature("Gemini", 1.0f);
            int maxOutputTokens = ChatConfig.GetMaxTokens("Gemini", 8192);

            // ? 流式 API URL（使用 streamGenerateContent 并添加 alt=sse）
            string apiUrl = $"{apiUrlBase}{model}:streamGenerateContent?alt=sse";
            
            Debug.Log($"[Gemini] 发送请求(流式) - 模型: {model}, 温度: {temperature}, MaxTokens: {maxOutputTokens}");

            // 将通用结构转换为 Gemini 的 contents/parts
            var geminiContents = new List<GeminiContent>();
            if (chatHistory != null)
            {
                foreach (var m in chatHistory)
                {
                    if (m == null) continue;
                    var parts = new List<GeminiPart>();
                    if (m.parts != null)
                    {
                        foreach (var p in m.parts)
                        {
                            if (p == null) continue;
                            parts.Add(new GeminiPart { text = p.text });
                        }
                    }
                    geminiContents.Add(new GeminiContent { role = m.role, parts = parts });
                }
            }

            var requestData = new GeminiRequest
            {
                contents = geminiContents,
                generationConfig = new GeminiGenerationConfig { temperature = temperature, maxOutputTokens = maxOutputTokens }
            };

            string jsonBody = Newtonsoft.Json.JsonConvert.SerializeObject(requestData);

            try
            {
                var http = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                http.Headers.Add("x-goog-api-key", apiKey);
                http.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                http.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using (var resp = await client.SendAsync(http, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        onError?.Invoke($"HTTP {(int)resp.StatusCode}: {errBody}");
                        return;
                    }

                    var sb = new StringBuilder();
                    using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while (!reader.EndOfStream && !ct.IsCancellationRequested && (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (line.StartsWith(":")) continue; // 注释/心跳
                            if (!line.StartsWith("data:")) continue;

                            var payload = line.Substring("data:".Length).Trim();
                            if (payload == "[DONE]") break;

                            try
                            {
                                var jo = JObject.Parse(payload);
                                // Gemini SSE 格式: candidates[0].content.parts[0].text
                                var delta = jo["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                                if (!string.IsNullOrEmpty(delta))
                                {
                                    sb.Append(delta);
                                    onDelta?.Invoke(delta);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Gemini] SSE 解析失败: {ex.Message} | 原始: {payload}");
                            }
                        }
                    }

                    onCompleted?.Invoke(sb.ToString());
                }
            }
            catch (TaskCanceledException)
            {
                onError?.Invoke("请求已取消");
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex.Message);
            }
        }
    }

    // --- Gemini 数据结构 ---
    [System.Serializable]
    public class GeminiRequest { public List<GeminiContent> contents; public GeminiGenerationConfig generationConfig; }
    [System.Serializable]
    public class GeminiGenerationConfig { public float temperature; public int maxOutputTokens; }
    [System.Serializable]
    public class GeminiContent { public string role; public List<GeminiPart> parts; }
    [System.Serializable]
    public class GeminiPart { public string text; }
    [System.Serializable]
    public class GeminiResponse { public List<GeminiCandidate> candidates; }
    [System.Serializable]
    public class GeminiCandidate { public GeminiContent content; }
}
