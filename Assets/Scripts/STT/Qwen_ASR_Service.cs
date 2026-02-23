using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using AI.Config;

namespace AI.STT
{
    /// <summary>
    /// Qwen Paraformer 实时语音识别（WebSocket 版）遵循官方协议
    /// 文档: https://help.aliyun.com/zh/model-studio/websocket-for-paraformer-real-time-service
    /// </summary>
    public static class Qwen_ASR_Service
    {
        private const string WebSocketUrl = "wss://dashscope.aliyuncs.com/api-ws/v1/inference/";
        // 是否打印逐步增量结果。默认关闭，仅在最终结束时打印一次。
        private const bool LogPartialResults = false;

        private static string GetAPIKey()
        {
            var cfg = AIAPISettings.Instance?.QwenASR;
            if (cfg == null || !cfg.IsConfigured())
            {
                Debug.LogError("[Qwen_ASR] 未找到或未配置 API Key。");
                return null;
            }
            return cfg.ApiKey;
        }

        private static QwenASRConfig GetConfig() => AIAPISettings.Instance?.QwenASR;

        /// <summary>
        /// 发送一次性流式识别请求（单次音频）
        /// </summary>
        public static async Task<string> TranscribeBytes(string apiKey, byte[] audioBytes, string format = "wav", int? sampleRate = null)
        {
            if (string.IsNullOrEmpty(apiKey)) apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(apiKey) || audioBytes == null || audioBytes.Length == 0)
            {
                Debug.LogError("[Qwen_ASR] 缺少 API Key 或音频为空。");
                return null;
            }

            // 保留 WAV 头，按照官方示例以 format=wav 发送整段二进制数据
            var config = GetConfig();
            string model = config?.Model ?? "paraformer-realtime-v2";
            int sr = sampleRate ?? 16000;

            string taskId = Guid.NewGuid().ToString("N");
            ClientWebSocket ws = null;
            
            try
            {
                ws = new ClientWebSocket();
                
                // 按官方示例设置鉴权头（不加 Bearer 前缀）
                ws.Options.SetRequestHeader("Authorization", apiKey);
                // 可选：启用数据巡检
                ws.Options.SetRequestHeader("X-DashScope-DataInspection", "enable");

                Debug.Log($"[Qwen_ASR] 连接到: {WebSocketUrl}");
                
                // 添加超时处理
                var connectTask = ws.ConnectAsync(new Uri(WebSocketUrl), CancellationToken.None);
                var timeoutTask = Task.Delay(10000); // 10秒超时
                
                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    Debug.LogError("[Qwen_ASR] WebSocket 连接超时。");
                    return null;
                }
                
                await connectTask; // 确保获取任何连接异常
                
                if (ws.State != WebSocketState.Open)
                {
                    Debug.LogError($"[Qwen_ASR] WebSocket 连接失败。状态: {ws.State}");
                    return null;
                }

                // 1) run-task（严格按照协议字段）
                var runTask = new JObject
                {
                    ["header"] = new JObject
                    {
                        ["action"] = "run-task",
                        ["task_id"] = taskId,
                        ["streaming"] = "duplex"
                    },
                    ["payload"] = new JObject
                    {
                        ["task_group"] = "audio",
                        ["task"] = "asr",
                        ["function"] = "recognition",
                        ["model"] = model,
                        ["parameters"] = new JObject
                        {
                            ["format"] = format,
                            ["sample_rate"] = sr
                            // 其他可选参数：vocabulary_id, disfluency_removal_enabled 等
                        },
                        ["input"] = new JObject()
                    }
                };

                var runJson = Encoding.UTF8.GetBytes(runTask.ToString(Formatting.None));
                await ws.SendAsync(new ArraySegment<byte>(runJson), WebSocketMessageType.Text, true, CancellationToken.None);
                Debug.Log($"[Qwen_ASR] 已发送 run-task: model={model}, sample_rate={sr}, format={format}");

                // 2) 等待 task-started 再发送音频
                if (!await WaitForTaskStarted(ws))
                {
                    Debug.LogError("[Qwen_ASR] 未收到 task-started，终止。");
                    return null;
                }

                // 3) 发送音频流。官方示例每100ms发送约100ms音频（此处按1KB/帧并延时100ms）
                const int chunkSize = 1024;
                int sent = 0;
                while (sent < audioBytes.Length && ws.State == WebSocketState.Open)
                {
                    int size = Math.Min(chunkSize, audioBytes.Length - sent);
                    await ws.SendAsync(new ArraySegment<byte>(audioBytes, sent, size), WebSocketMessageType.Binary, true, CancellationToken.None);
                    sent += size;
                    await Task.Delay(100);
                }
                Debug.Log($"[Qwen_ASR] 音频发送完成，共 {audioBytes.Length} bytes");

                // 4) finish-task（同样包含 streaming，并带空 input）
                var finishTask = new JObject
                {
                    ["header"] = new JObject
                    {
                        ["action"] = "finish-task",
                        ["task_id"] = taskId,
                        ["streaming"] = "duplex"
                    },
                    ["payload"] = new JObject
                    {
                        ["input"] = new JObject()
                    }
                };
                var finishJson = Encoding.UTF8.GetBytes(finishTask.ToString(Formatting.None));
                await ws.SendAsync(new ArraySegment<byte>(finishJson), WebSocketMessageType.Text, true, CancellationToken.None);
                Debug.Log("[Qwen_ASR] 已发送 finish-task");

                // 5) 接收直到 task-finished，取最终句子
                string finalText = await ReceiveUntilFinished(ws);
                
                // 在返回前关闭 WebSocket
                await CloseWebSocketAsync(ws);
                
                return finalText;
            }
            catch (WebSocketException wsEx)
            {
                Debug.LogError($"[Qwen_ASR] WebSocket 异常: {wsEx.Message}\n{wsEx.StackTrace}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qwen_ASR] 识别过程异常: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
            finally
            {
                // 确保 WebSocket 正确释放（不使用 await）
                if (ws != null)
                {
                    ws.Dispose();
                }
            }
        }

        /// <summary>
        /// 安全关闭 WebSocket 连接
        /// </summary>
        private static async Task CloseWebSocketAsync(ClientWebSocket ws)
        {
            if (ws == null) return;
            
            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "完成", CancellationToken.None);
                    Debug.Log("[Qwen_ASR] WebSocket 已正常关闭");
                }
            }
            catch (Exception closeEx)
            {
                Debug.LogWarning($"[Qwen_ASR] 关闭 WebSocket 时出错: {closeEx.Message}");
            }
        }

        private static async Task<bool> WaitForTaskStarted(ClientWebSocket ws)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            int maxAttempts = 50; // 最多等待5秒（50 * 100ms）
            int attempts = 0;
            
            while (attempts < maxAttempts)
            {
                attempts++;
                
                if (ws.State != WebSocketState.Open)
                {
                    Debug.LogWarning($"[Qwen_ASR] WebSocket 状态异常: {ws.State}");
                    return false;
                }
                
                try
                {
                    // 使用短超时避免阻塞
                    var receiveTask = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var timeoutTask = Task.Delay(100);
                    
                    if (await Task.WhenAny(receiveTask, timeoutTask) == timeoutTask)
                    {
                        continue; // 超时，重试
                    }
                    
                    var res = await receiveTask;
                    
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.LogWarning("[Qwen_ASR] 连接被关闭（等待 task-started）");
                        return false;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    if (!res.EndOfMessage) continue;

                    string msg = sb.ToString();
                    sb.Length = 0;
                    
                    var json = JObject.Parse(msg);
                    string evt = json["header"]?["event"]?.ToString();
                    if (evt == "task-started")
                    {
                        Debug.Log("[Qwen_ASR] 任务开启成功");
                        return true;
                    }
                    if (evt == "task-failed")
                    {
                        string err = json["header"]?["error_message"]?.ToString();
                        Debug.LogError($"[Qwen_ASR] 任务失败: {err ?? msg}");
                        return false;
                    }
                    // 其他事件先忽略，继续等待
                    Debug.Log($"[Qwen_ASR] 等待 task-started，收到事件: {evt ?? "<unknown>"}");
                }
                catch (JsonException jsonEx)
                {
                    Debug.LogWarning($"[Qwen_ASR] JSON 解析失败（等待 task-started）: {jsonEx.Message}");
                    sb.Length = 0; // 清空缓冲区
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Qwen_ASR] 接收消息失败: {ex.Message}");
                    return false;
                }
            }
            
            Debug.LogError("[Qwen_ASR] 等待 task-started 超时");
            return false;
        }

        private static async Task<string> ReceiveUntilFinished(ClientWebSocket ws)
        {
            var buffer = new byte[65536];
            var sb = new StringBuilder();
            string lastSentence = null; // 保存最新句子，避免把增量重复拼接
            int maxAttempts = 300; // 最多等待30秒
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                attempts++;
                
                if (ws.State != WebSocketState.Open)
                {
                    Debug.LogWarning($"[Qwen_ASR] WebSocket 状态: {ws.State}，停止接收");
                    break;
                }
                
                try
                {
                    var receiveTask = ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var timeoutTask = Task.Delay(100);
                    
                    if (await Task.WhenAny(receiveTask, timeoutTask) == timeoutTask)
                    {
                        continue; // 超时，重试
                    }
                    
                    var res = await receiveTask;
                    
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[Qwen_ASR] 服务端关闭连接");
                        break;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));
                    if (!res.EndOfMessage) continue;

                    string combined = sb.ToString();
                    sb.Length = 0;

                    var json = JObject.Parse(combined);
                    string evt = json["header"]?["event"]?.ToString();
                    
                    if (evt == "result-generated")
                    {
                        // 官方示例: payload.output.sentence.text
                        string sentence = json["payload"]?["output"]?["sentence"]?["text"]?.ToString();
                        if (string.IsNullOrEmpty(sentence))
                        {
                            // 兼容旧格式: payload.output[0].text
                            sentence = json["payload"]?["output"]?[0]?["text"]?.ToString();
                        }
                        if (!string.IsNullOrEmpty(sentence))
                        {
                            // 只保留最新句子，避免重复
                            lastSentence = sentence;
                        }
                        // 打印计费时长（若有）
                        var dur = json["payload"]?["usage"]?["duration"]?.ToString();
                        if (!string.IsNullOrEmpty(dur))
                        {
                            Debug.Log($"[Qwen_ASR] 任务计费时长（秒）: {dur}");
                        }
                    }
                    else if (evt == "task-finished")
                    {
                        Debug.Log($"[Qwen_ASR] 识别完成，最终结果: {lastSentence ?? "(空)"}");
                        break;
                    }
                    else if (evt == "task-failed")
                    {
                        string err = json["header"]?["error_message"]?.ToString();
                        Debug.LogError($"[Qwen_ASR] 任务失败: {err ?? combined}");
                        break;
                    }
                    else
                    {
                        Debug.Log($"[Qwen_ASR] 收到事件: {evt ?? "<unknown>"}");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Debug.LogWarning($"[Qwen_ASR] JSON 解析失败: {jsonEx.Message}");
                    sb.Length = 0; // 清空缓冲区
                }
                catch (WebSocketException wsEx)
                {
                    Debug.LogError($"[Qwen_ASR] WebSocket 接收异常: {wsEx.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Qwen_ASR] 接收消息异常: {ex.Message}");
                }
            }
            
            if (attempts >= maxAttempts)
            {
                Debug.LogWarning("[Qwen_ASR] 接收超时");
            }

            return string.IsNullOrWhiteSpace(lastSentence) ? null : lastSentence;
        }

        public static async Task<string> TranscribeFile(string apiKey, string path)
        {
            if (string.IsNullOrEmpty(apiKey)) apiKey = GetAPIKey();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError("[Qwen_ASR] 文件路径无效。");
                return null;
            }

            try
            {
                // 使用同步方法读取文件，兼容 .NET Framework 4.7.1
                byte[] bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                return await TranscribeBytes(apiKey, bytes, ext);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qwen_ASR] 读取文件失败: {ex.Message}");
                return null;
            }
        }
    }
}
