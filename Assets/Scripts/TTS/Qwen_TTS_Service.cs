using AI.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AI.TTS
{
    // Qwen DashScope 流式TTS （SSE）
    public class QwenTTSProvider : ITTSProvider
    {
        private static readonly string apiUrl = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        private static readonly HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(100)
        };

        // 采用服务端SSE流式输出，持续回调音频分片
        public async Task StreamTTS(string text, TTSSessionOptions options, Action<byte[]> onPcmChunk, CancellationToken ct)
        {
            // 如果 options 中没有提供 API Key，从配置获取
            string apiKey = options?.ApiKey;
            string voiceId = options?.VoiceId;
            string languageType = options?.LanguageType;

            // 始终从配置中补全未指定的参数
            var config = AIAPISettings.Instance?.QwenTTS;
            if (config != null)
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    if (!config.IsConfigured())
                    {
                        Debug.LogWarning("[Qwen TTS] API Key 未配置，请到 Resources/APISettings 中设置。");
                        return;
                    }
                    apiKey = config.ApiKey;
                }
                if (string.IsNullOrEmpty(voiceId))
                    voiceId = config.VoiceId;
                if (string.IsNullOrEmpty(languageType))
                    languageType = config.LanguageType;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("[Qwen TTS] 未配置 API Key");
                return;
            }

            // 提取出实际要使用的参数变量
            string actualVoice = string.IsNullOrEmpty(voiceId) ? "Cherry" : voiceId;
            string actualLanguage = string.IsNullOrEmpty(languageType) ? "Chinese" : languageType;

            // 核心修改：采用多模态大模型的标准 Messages 数组结构，并将参数移入 parameters
            var body = new
            {
                model = config?.Model ?? "qwen3-tts-flash",
                input = new
                {
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = new[]
                            {
                                new { text = text }
                            }
                        }
                    }
                },
                parameters = new
                {
                    voice = actualVoice,
                    language_type = actualLanguage,
                    format = "pcm",          // 强制返回 pcm 裸流，避免处理 wav 头
                    sample_rate = 16000      // 确保采样率固定，方便 Unity 端处理
                }
            };

            Debug.Log($"[Qwen TTS] 开始合成 - 文本长度: {text?.Length ?? 0}, 声音: {actualVoice}, 语言: {actualLanguage}");

            var req = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("X-DashScope-SSE", "enable");
            req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            try
            {
                using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Debug.LogError($"[Qwen TTS] HTTP {resp.StatusCode}: {err}");
                        return;
                    }

                    using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while (!reader.EndOfStream && !ct.IsCancellationRequested && (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (line.StartsWith(":")) continue; // 注释行
                            if (!line.StartsWith("data:")) continue;

                            var payload = line.Substring("data:".Length).Trim();
                            if (payload == "[DONE]") break;

                            try
                            {
                                var jo = JObject.Parse(payload);

                                // 1) 检测并输出URL（某些接口返回）
                                var url = ExtractFinalUrl(jo);
                                if (!string.IsNullOrEmpty(url))
                                {
                                    Debug.Log($"[Qwen TTS] Final URL: {url}");
                                }

                                // 2) 检测音频数据（Base64）并解码，提取PCM
                                var (rawBytes, format, sampleRate) = ExtractAudioBase64(jo);
                                if (rawBytes != null && rawBytes.Length > 0)
                                {
                                    byte[] pcm = rawBytes;
                                    // 如果依然返回了 WAV（做个兜底兼容），提取data块
                                    if (IsWav(rawBytes))
                                    {
                                        var wavPcm = TryExtractPcmFromWav(rawBytes);
                                        if (wavPcm != null) pcm = wavPcm;
                                    }
                                    onPcmChunk?.Invoke(pcm);

                                    // 检查返回采样率是否和预期匹配
                                    if (sampleRate.HasValue && sampleRate.Value != 16000)
                                    {
                                        Debug.LogWarning($"[Qwen TTS] 返回采样率为 {sampleRate.Value}Hz，与当前播放器(16000Hz)不一致，可能导致变调。");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[Qwen TTS] SSE 解析失败: {ex.Message}");
                            }
                        }
                    }
                }

                Debug.Log("[Qwen TTS] 合成完成");
            }
            catch (TaskCanceledException)
            {
                Debug.LogWarning("[Qwen TTS] 请求取消或超时");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qwen TTS] 异常: {ex.Message}");
            }
        }

        private static (byte[] bytes, string format, int? sampleRate) ExtractAudioBase64(JObject jo)
        {
            // 常见可能字段：result.audio（string或对象）、result.audio.data、output.audio.data、audio.data
            JToken audioNode = jo.SelectToken("result.audio") ??
                                 jo.SelectToken("output.audio") ??
                                 jo.SelectToken("audio");

            if (audioNode == null) return (null, null, null);

            string base64 = null;
            string format = null;
            int? sr = null;

            if (audioNode.Type == JTokenType.Object)
            {
                base64 = audioNode["data"]?.ToString();
                format = audioNode["format"]?.ToString();
                if (int.TryParse(audioNode["sample_rate"]?.ToString(), out var srr)) sr = srr;
            }
            else if (audioNode.Type == JTokenType.String)
            {
                base64 = audioNode.ToString();
            }

            if (string.IsNullOrEmpty(base64)) return (null, format, sr);

            try
            {
                var bytes = Convert.FromBase64String(base64);
                return (bytes, format, sr);
            }
            catch
            {
                // 非base64或损坏
                return (null, format, sr);
            }
        }

        private static string ExtractFinalUrl(JObject jo)
        {
            // 可能位置：result.audio_url、output.audio_url
            var url = jo.SelectToken("result.audio_url")?.ToString();
            if (!string.IsNullOrEmpty(url)) return url;
            url = jo.SelectToken("output.audio_url")?.ToString();
            return url;
        }

        private static bool IsWav(byte[] data)
        {
            if (data == null || data.Length < 12) return false;
            return data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                   data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
        }

        private static byte[] TryExtractPcmFromWav(byte[] wav)
        {
            try
            {
                int i = 12;
                while (i + 8 <= wav.Length)
                {
                    // chunk id + size
                    string chunkId = Encoding.ASCII.GetString(wav, i, 4);
                    int size = BitConverter.ToInt32(wav, i + 4);

                    // 【核心修复】：增加死循环保护，如果解析出非法 size，直接跳出
                    if (size <= 0) break;

                    int dataStart = i + 8;
                    if (chunkId == "data")
                    {
                        if (dataStart + size <= wav.Length)
                        {
                            var pcm = new byte[size];
                            Buffer.BlockCopy(wav, dataStart, pcm, 0, size);
                            return pcm;
                        }
                        break;
                    }
                    i = dataStart + size;
                }
            }
            catch { }
            return null;
        }
    }
}