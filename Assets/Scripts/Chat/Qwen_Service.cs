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
    /// <summary>
    /// Qwen 对话服务 - 兼容模式 API（支持流式与非流式）
    /// </summary>
    public static class Qwen_Service
    {
        private const string ApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

        private static string GetAPIKey()
        {
            var key = ChatConfig.GetApiKey("Qwen");
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[Qwen] 无法读取 API Key，请在 Resources/APISettings 配置。");
            }
            return key;
        }

        /// <summary>
        /// 同步接口：根据 StreamingEnabled 自动选择非流式或流式（内部累积后返回最终串）
        /// </summary>
        public static async Task<string> SendMessage(string apiKey, List<ChatMessage> chatHistory)
        {
            if (string.IsNullOrEmpty(apiKey)) apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("[Qwen] 缺少 API Key");
                return "{\"dialogue\":\"Qwen API Key 未配置\", \"emotion\":\"Sad\", \"action\":\"\"}";
            }

            var streaming = ChatConfig.GetStreamingEnabled("Qwen", false);
            if (!streaming)
            {
                return await SendMessageOnce(apiKey, chatHistory);
            }

            var sb = new StringBuilder();
            await SendMessageStream(apiKey, chatHistory,
                onDelta: delta => { if (!string.IsNullOrEmpty(delta)) sb.Append(delta); },
                onCompleted: _ => { },
                onError: _ => { },
                ct: CancellationToken.None);
            return sb.ToString();
        }

        /// <summary>
        /// 非流式一次性请求
        /// </summary>
        private static async Task<string> SendMessageOnce(string apiKey, List<ChatMessage> chatHistory)
        {
            // 转换为接口格式
            var messages = BuildMessages(chatHistory);

            var model = ChatConfig.GetModel("Qwen", "qwen-plus");
            var temperature = ChatConfig.GetTemperature("Qwen", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("Qwen", 2048);

            var reqBody = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            Debug.Log($"[Qwen] 发送请求(非流式) - 模型: {model}, 温度: {temperature}, MaxTokens: {maxTokens}");

            var http = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            http.Headers.Add("Authorization", $"Bearer {apiKey}");
            http.Content = new StringContent(JsonConvert.SerializeObject(reqBody), Encoding.UTF8, "application/json");

            try
            {
                using (var resp = await client.SendAsync(http).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.LogError($"[Qwen] API错误: {(int)resp.StatusCode} {body}");
                        return $"{{\"dialogue\":\"Qwen API错误: {(int)resp.StatusCode}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
                    }

                    return TryExtractOnce(body);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qwen] 异常: {ex.Message}");
                return $"{{\"dialogue\":\"请求异常: {EscapeForJson(ex.Message)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
            }
        }

        /// <summary>
        /// 流式请求：通过回调分发增量与完成。若错误则调用 onError。
        /// </summary>
        public static async Task SendMessageStream(
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
                onError?.Invoke("Qwen API Key 未配置");
                return;
            }

            var messages = BuildMessages(chatHistory);
            var model = ChatConfig.GetModel("Qwen", "qwen-plus");
            var temperature = ChatConfig.GetTemperature("Qwen", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("Qwen", 2048);

            var reqBody = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = true
            };

            Debug.Log($"[Qwen] 发送请求(流式) - 模型: {model}, 温度: {temperature}, MaxTokens: {maxTokens}");

            try
            {
                using (var http = new HttpRequestMessage(HttpMethod.Post, ApiUrl))
                {
                    http.Headers.Add("Authorization", $"Bearer {apiKey}");
                    http.Headers.Add("X-DashScope-SSE", "enable");
                    http.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                    http.Content = new StringContent(JsonConvert.SerializeObject(reqBody), Encoding.UTF8, "application/json");

                    using (var resp = await client.SendAsync(http, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            onError?.Invoke($"HTTP {(int)resp.StatusCode}: {err}");
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
                                if (line.StartsWith(":")) continue; // 注释
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
                                    Debug.LogWarning($"[Qwen] SSE 解析失败: {ex.Message} | 原始: {payload}");
                                }
                            }
                        }

                        onCompleted?.Invoke(sb.ToString());
                    }
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

        private static List<QwenMessage> BuildMessages(List<ChatMessage> chatHistory)
        {
            var messages = new List<QwenMessage>();
            if (chatHistory != null)
            {
                foreach (var c in chatHistory)
                {
                    if (c == null) continue;
                    var role = NormalizeRole(c.role);
                    var content = c.parts != null && c.parts.Count > 0
                        ? string.Join("\n", c.parts.Where(p => p != null).Select(p => p.text ?? string.Empty))
                        : string.Empty;
                    messages.Add(new QwenMessage { role = role, content = content });
                }
            }
            return messages;
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return "user";
            role = role.ToLowerInvariant();
            if (role == "model") return "assistant";
            if (role == "assistant") return "assistant";
            if (role == "system") return "system";
            return "user";
        }

        private static string TryExtractOnce(string body)
        {
            try
            {
                var jo = JObject.Parse(body);
                var choices = jo["choices"] as JArray;
                if (choices != null && choices.Count > 0)
                {
                    var message = choices[0]["message"];
                    var content = message?["content"]?.ToString();
                    if (!string.IsNullOrEmpty(content))
                    {
                        return content;
                    }
                }

                var error = jo["error"];
                if (error != null)
                {
                    var errorMsg = error["message"]?.ToString() ?? error.ToString();
                    Debug.LogError($"[Qwen] API 返回错误: {errorMsg}");
                    return $"{{\"dialogue\":\"API错误: {EscapeForJson(errorMsg)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
                }

                Debug.LogWarning($"[Qwen] 未能解析响应: {body}");
                return "{\"dialogue\":\"未能解析AI响应\", \"emotion\":\"Thinking\", \"action\":\"\"}";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qwen] 解析响应失败: {ex.Message}");
                return "{\"dialogue\":\"解析响应失败\", \"emotion\":\"Sad\", \"action\":\"\"}";
            }
        }

        private static string EscapeForJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private class QwenMessage
        {
            public string role;
            public string content;
        }
    }
}
