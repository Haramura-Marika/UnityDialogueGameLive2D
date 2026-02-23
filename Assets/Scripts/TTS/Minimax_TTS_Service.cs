using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using AI.Config;

namespace AI.TTS
{
    // 可扩展的 TTS Provider 接口
    public interface ITTSProvider
    {
        Task StreamTTS(string text, TTSSessionOptions options, Action<byte[]> onPcmChunk, CancellationToken ct);
    }

    // 各个 Provider 使用的会话选项
    public class TTSSessionOptions
    {
        public string ApiKey { get; set; }
        public string GroupId { get; set; }
        public string VoiceId { get; set; }
        public string LanguageType { get; set; } // 仅用于Qwen 等服务
    }

    // Minimax TTS Provider 实现，回调音频的PCM字节块到上游播放器
    public class MinimaxTTSProvider : ITTSProvider
    {
        private ClientWebSocket webSocket;

        public async Task StreamTTS(string text, TTSSessionOptions options, Action<byte[]> onPcmChunk, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            // 如果 options 中没有提供 API Key，从配置获取
            string apiKey = options?.ApiKey;
            string groupId = options?.GroupId;
            string voiceId = options?.VoiceId;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(groupId))
            {
                if (AIAPISettings.Instance == null || !AIAPISettings.Instance.MinimaxTTS.IsConfigured())
                {
                    Debug.LogWarning("[Minimax TTS] API Key 或 GroupId 未配置！请在 Resources/APISettings 中设置。");
                    return;
                }
                
                var config = AIAPISettings.Instance.MinimaxTTS;
                apiKey = config.ApiKey;
                groupId = config.GroupId;
                
                // 如果没有指定 voice，使用配置中的默认值
                if (string.IsNullOrEmpty(voiceId))
                    voiceId = config.VoiceId;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[Minimax TTS] 未配置 API Key");
                return;
            }

            Debug.Log($"[Minimax TTS] 开始合成 - 文本长度: {text?.Length ?? 0}, 语音: {voiceId}");

            webSocket = new ClientWebSocket();
            string url = "wss://api.minimaxi.com/ws/v1/t2a_v2";
            webSocket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            try
            {
                await webSocket.ConnectAsync(new Uri(url), ct);

                var connectionResponse = await ReceiveJsonMessage<MinimaxBaseResponse>(ct);
                if (connectionResponse?.@event != "connected_success")
                {
                    Debug.LogError($"[Minimax TTS] 连接失败: {connectionResponse?.base_resp?.status_msg}");
                    return;
                }

                var startRequest = new MinimaxTaskStartRequest(voiceId);
                await SendJsonMessage(startRequest, ct);
                var startResponse = await ReceiveJsonMessage<MinimaxBaseResponse>(ct);
                if (startResponse?.@event != "task_started")
                {
                    Debug.LogError($"[Minimax TTS] 启动失败: {startResponse?.base_resp?.status_msg}");
                    return;
                }

                var continueRequest = new MinimaxTaskContinueRequest(text);
                await SendJsonMessage(continueRequest, ct);

                // 接收音频块
                while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var response = await ReceiveJsonMessage<MinimaxStreamResponse>(ct);
                    if (response == null) break;

                    if (response?.base_resp != null && response.base_resp.status_code != 0)
                    {
                        Debug.LogError($"[Minimax TTS] 流错误: {response.base_resp.status_msg}");
                        break;
                    }

                    if (response?.data?.audio != null)
                    {
                        byte[] audioBytes = HexStringToByteArray(response.data.audio);
                        onPcmChunk?.Invoke(audioBytes);
                    }

                    if (response.is_final)
                    {
                        break;
                    }
                }

                if (webSocket != null && webSocket.State == WebSocketState.Open)
                {
                    var finishRequest = new MinimaxEventRequest("task_finish");
                    await SendJsonMessage(finishRequest, CancellationToken.None);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Finished", CancellationToken.None);
                }
                
                Debug.Log("[Minimax TTS] 合成完成");
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[Minimax TTS] 请求取消");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minimax TTS] WebSocket 异常: {ex.Message}");
            }
            finally
            {
                webSocket?.Dispose();
            }
        }

        private async Task SendJsonMessage(object payload, CancellationToken ct)
        {
            if (webSocket.State != WebSocketState.Open) return;
            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            byte[] sendBytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, ct);
        }

        private async Task<T> ReceiveJsonMessage<T>(CancellationToken ct)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    if (webSocket.State != WebSocketState.Open) return default(T);
                    result = await webSocket.ReceiveAsync(buffer, ct);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    using (var reader = new StreamReader(ms, Encoding.UTF8))
                    {
                        string message = await reader.ReadToEndAsync();
                        Debug.Log($"[Minimax RECV]: {message}");
                        return JsonConvert.DeserializeObject<T>(message);
                    }
                }
            }
            return default(T);
        }

        private static byte[] HexStringToByteArray(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
            if (hex.Length % 2 != 0)
            {
                hex = hex.Substring(0, hex.Length - 1);
            }
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }

    #region Minimax API Data Structures
    public class MinimaxEventRequest
    {
        public string @event { get; set; }
        public MinimaxEventRequest(string eventName) { this.@event = eventName; }
    }

    public class MinimaxTaskStartRequest
    {
        public string @event => "task_start";
        public string model => "speech-02-turbo";
        public VoiceSetting voice_setting { get; set; }
        public AudioSetting audio_setting { get; set; }

        public MinimaxTaskStartRequest(string voiceId)
        {
            this.voice_setting = new VoiceSetting { voice_id = voiceId };
            this.audio_setting = new AudioSetting();
        }
    }

    public class MinimaxTaskContinueRequest
    {
        public string @event => "task_continue";
        public string text { get; set; }
        public MinimaxTaskContinueRequest(string textToSpeak) { this.text = textToSpeak; }
    }

    public class VoiceSetting { public string voice_id { get; set; } }
    public class AudioSetting { public string format => "pcm"; public int sample_rate => 16000; }

    public class MinimaxBaseResponse
    {
        public string @event { get; set; }
        public BaseResp base_resp { get; set; }
    }

    public class MinimaxStreamResponse : MinimaxBaseResponse
    {
        public Data data { get; set; }
        public bool is_final { get; set; }
    }

    public class Data { public string audio { get; set; } }
    public class BaseResp { public int status_code { get; set; } public string status_msg { get; set; } }
    #endregion
}
