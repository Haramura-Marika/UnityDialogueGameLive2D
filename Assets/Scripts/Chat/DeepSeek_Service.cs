using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Providers
{
    public static class DeepSeek_Service
    {
        private static readonly string apiUrl = "https://api.deepseek.com/chat/completions";
        private static readonly HttpClient client = new HttpClient();

        private static string GetAPIKey()
        {
            var key = ChatConfig.GetApiKey("DeepSeek");
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[DeepSeek] 无法读取 API Key，请在 Resources/APISettings 配置。");
            }
            return key;
        }

        // 统一：使用 ChatMessage 列表
        public static async Task<string> GetAIResponse(List<ChatMessage> chatHistory)
        {
            string apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return "{\"dialogue\":\"请在 Resources/APISettings 配置 DeepSeek API 密钥\", \"emotion\":\"Sad\", \"action\":\"\"}";
            }

            var streaming = ChatConfig.GetStreamingEnabled("DeepSeek", false);
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

        private static async Task<string> SendOnce(string apiKey, List<ChatMessage> chatHistory)
        {
            var messages = new List<DeepSeekMessage>();
            if (chatHistory != null)
            {
                foreach (var c in chatHistory)
                {
                    if (c == null) continue;
                    var role = NormalizeRole(c.role);
                    var content = c.parts != null && c.parts.Count > 0
                        ? string.Join("\n", c.parts.Where(p => p != null).Select(p => p.text ?? string.Empty))
                        : string.Empty;
                    messages.Add(new DeepSeekMessage { role = role, content = content });
                }
            }

            var model = ChatConfig.GetModel("DeepSeek", "deepseek-chat");
            var temperature = ChatConfig.GetTemperature("DeepSeek", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("DeepSeek", 2048);

            var req = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            Debug.Log($"[DeepSeek] 发送请求(非流式) - 模型: {req.model}, 温度: {temperature}, MaxTokens: {maxTokens}, 消息数: {messages.Count}");

            var http = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            http.Headers.Add("Authorization", $"Bearer {apiKey}");
            http.Content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");

            try
            {
                using (var resp = await client.SendAsync(http).ConfigureAwait(false))
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.LogError($"[DeepSeek] API错误: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
                        return $"{{\"dialogue\":\"DeepSeek API错误: {(int)resp.StatusCode} {EscapeForJson(resp.ReasonPhrase)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
                    }

                    return TryExtractOnce(body);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DeepSeek] 请求异常: {ex.Message}");
                return $"{{\"dialogue\":\"请求异常: {EscapeForJson(ex.Message)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
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
                onError?.Invoke("DeepSeek API Key 未配置");
                return;
            }

            var messages = new List<DeepSeekMessage>();
            if (chatHistory != null)
            {
                foreach (var c in chatHistory)
                {
                    if (c == null) continue;
                    var role = NormalizeRole(c.role);
                    var content = c.parts != null && c.parts.Count > 0
                        ? string.Join("\n", c.parts.Where(p => p != null).Select(p => p.text ?? string.Empty))
                        : string.Empty;
                    messages.Add(new DeepSeekMessage { role = role, content = content });
                }
            }

            var model = ChatConfig.GetModel("DeepSeek", "deepseek-chat");
            var temperature = ChatConfig.GetTemperature("DeepSeek", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("DeepSeek", 2048);

            var req = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = true
            };

            Debug.Log($"[DeepSeek] 发送请求(流式) - 模型: {req.model}, 温度: {temperature}, MaxTokens: {maxTokens}, 消息数: {messages.Count}");

            try
            {
                var http = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                http.Headers.Add("Authorization", $"Bearer {apiKey}");
                http.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                http.Content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");

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
                                var delta = jo["choices"]?[0]?["delta"]?["content"]?.ToString();
                                if (string.IsNullOrEmpty(delta))
                                {
                                    delta = jo["choices"]?[0]?["message"]?["content"]?.ToString();
                                }
                                if (!string.IsNullOrEmpty(delta))
                                {
                                    sb.Append(delta);
                                    onDelta?.Invoke(delta);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[DeepSeek] SSE 解析失败: {ex.Message} | 原始: {payload}");
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

        private static string TryExtractOnce(string body)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<DeepSeekChatResponse>(body);
                var content = data?.Choices != null && data.Choices.Count > 0 ? data.Choices[0].Message?.Content : null;
                if (!string.IsNullOrEmpty(content)) return content;
            }
            catch { }
            return body;
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return "user";
            var r = role.Trim().ToLowerInvariant();
            if (r == "model") return "assistant";
            if (r == "assistant" || r == "user" || r == "system") return r;
            return "user";
        }

        private static string EscapeForJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    public class DeepSeekChatResponse
    {
        [JsonProperty("choices")] public List<DeepSeekChoice> Choices { get; set; }
    }

    public class DeepSeekChoice
    {
        [JsonProperty("message")] public DeepSeekMessage Message { get; set; }
    }

    public class DeepSeekMessage
    {
        [JsonProperty("role")] public string Role { get; set; }
        [JsonProperty("content")] public string Content { get; set; }
        public string role { get => Role; set => Role = value; }
        public string content { get => Content; set => Content = value; }
    }
}
