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
    public class MinimaxMessage
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    public static class Minimax_Service
    {
        // 更新 API URL 适配 OpenAI 兼容格式
        private static readonly string apiUrl = "https://api.minimaxi.com/v1/chat/completions";
        private static readonly HttpClient client = new HttpClient();

        private static string StripReasoningContent(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var cleaned = text;

            // 移除已闭合的思考标签
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "<think>[\\s\\S]*?</think>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "<thinking>[\\s\\S]*?</thinking>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 处理流式中尚未闭合的思考标签（不展示未闭合部分）
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "<think>[\\s\\S]*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "<thinking>[\\s\\S]*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 如果已经出现 JSON 正文，则裁掉前置分析文本，只保留 JSON 起始内容
            // 仅当前缀为非空白文本且 JSON 包含 "dialogue" 字段键时才裁剪
            int jsonStart = cleaned.IndexOf('{');
            if (jsonStart > 0)
            {
                string prefix = cleaned.Substring(0, jsonStart).Trim();
                if (prefix.Length > 0)
                {
                    var jsonCandidate = cleaned.Substring(jsonStart);
                    if (jsonCandidate.Contains("\"dialogue\""))
                    {
                        cleaned = jsonCandidate;
                    }
                }
            }

            return cleaned.TrimStart();
        }

        private static string GetAPIKey()
        {
            var key = ChatConfig.GetApiKey("Minimax");
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogError("[Minimax] 无法读取 API Key，请在 Resources/APISettings 配置。");
            }
            return key;
        }

        private static string GetGroupId()
        {
            var groupId = ChatConfig.GetGroupId("Minimax");
            if (string.IsNullOrEmpty(groupId))
            {
                Debug.LogError("[Minimax] 无法读取 GroupId，请在 Resources/APISettings 配置。");
            }
            return groupId;
        }

        private static string NormalizeRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return "user";
            role = role.ToLower();
            if (role == "system" || role == "developer") return "system";
            if (role == "assistant" || role == "model") return "assistant";
            return "user";
        }

        private static string EscapeForJson(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string TryExtractOnce(string json)
        {
            try
            {
                var jo = JObject.Parse(json);
                var content = jo["choices"]?[0]?["message"]?["content"]?.ToString();
                if (!string.IsNullOrEmpty(content)) return content;
            }
            catch { }
            return json; // 穿透，交给业务层强解
        }

        public static async Task<string> GetAIResponse(List<ChatMessage> chatHistory)
        {
            string apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return "{\"dialogue\":\"请在 Resources/APISettings 配置 Minimax API 密钥\", \"emotion\":\"Sad\", \"action\":\"\"}";
            }

            var streaming = ChatConfig.GetStreamingEnabled("Minimax", false);
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
            var messages = new List<MinimaxMessage>();
            if (chatHistory != null)
            {
                foreach (var c in chatHistory)
                {
                    if (c == null) continue;
                    var role = NormalizeRole(c.role);
                    var content = c.parts != null && c.parts.Count > 0
                        ? string.Join("\n", c.parts.Where(p => p != null).Select(p => p.text ?? string.Empty))
                        : string.Empty;
                    messages.Add(new MinimaxMessage { role = role, content = content });
                }
            }

            var model = ChatConfig.GetModel("Minimax", "MiniMax-M2.5-highspeed");
            var temperature = ChatConfig.GetTemperature("Minimax", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("Minimax", 8192);

            var req = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            Debug.Log($"[Minimax] 发送请求(非流式) - 模型: {req.model}, 温度: {temperature}, MaxTokens: {maxTokens}, 消息数: {messages.Count}");

            var http = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            http.Headers.Add("Authorization", $"Bearer {apiKey}");
            var groupId = GetGroupId();
            if (!string.IsNullOrEmpty(groupId))
            {
                http.Headers.Add("GroupId", groupId);
            }
            http.Content = new StringContent(JsonConvert.SerializeObject(req), Encoding.UTF8, "application/json");

            try
            {
                using (var resp = await client.SendAsync(http).ConfigureAwait(false))
                {
                    var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Debug.LogError($"[Minimax] API错误: {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}");
                        return $"{{\"dialogue\":\"Minimax API错误: {(int)resp.StatusCode} {EscapeForJson(resp.ReasonPhrase)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
                    }

                    return StripReasoningContent(TryExtractOnce(body));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minimax] 请求异常: {ex.Message}");
                return $"{{\"dialogue\":\"请求异常: {EscapeForJson(ex.Message)}\", \"emotion\":\"Sad\", \"action\":\"\"}}";
            }
        }

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
                onError?.Invoke("Minimax API Key 未配置");
                return;
            }

            var messages = new List<MinimaxMessage>();
            if (chatHistory != null)
            {
                foreach (var c in chatHistory)
                {
                    if (c == null) continue;
                    var role = NormalizeRole(c.role);
                    var content = c.parts != null && c.parts.Count > 0
                        ? string.Join("\n", c.parts.Where(p => p != null).Select(p => p.text ?? string.Empty))
                        : string.Empty;
                    messages.Add(new MinimaxMessage { role = role, content = content });
                }
            }

            var model = ChatConfig.GetModel("Minimax", "MiniMax-M2.5-highspeed");
            var temperature = ChatConfig.GetTemperature("Minimax", 1.0f);
            var maxTokens = ChatConfig.GetMaxTokens("Minimax", 8192);

            var req = new
            {
                model = model,
                messages = messages.Select(m => new { role = m.role, content = m.content }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                stream = true
            };

            Debug.Log($"[Minimax] 发送请求(流式) - 模型: {req.model}, 温度: {temperature}, MaxTokens: {maxTokens}, 消息数: {messages.Count}");

            try
            {
                var http = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                http.Headers.Add("Authorization", $"Bearer {apiKey}");
                var groupId = GetGroupId();
                if (!string.IsNullOrEmpty(groupId))
                {
                    http.Headers.Add("GroupId", groupId);
                }
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
                    var rawSb = new StringBuilder();
                    string lastClean = string.Empty;
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
                                    rawSb.Append(delta);
                                    var cleanAll = StripReasoningContent(rawSb.ToString());
                                    if (cleanAll.Length > lastClean.Length)
                                    {
                                        var incremental = cleanAll.Substring(lastClean.Length);
                                        sb.Append(incremental);
                                        onDelta?.Invoke(incremental);
                                        lastClean = cleanAll;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Minimax] SSE 解析失败: {ex.Message} | 原始: {payload}");
                            }
                        }
                    }

                    if (ct.IsCancellationRequested)
                    {
                        Debug.Log("[Minimax] 流式请求被取消");
                    }
                    else
                    {
                        onCompleted?.Invoke(lastClean);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[Minimax] 请求已被用户取消");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minimax] 流式请求异常: {ex.Message}");
                onError?.Invoke(ex.Message);
            }
        }
    }
}
