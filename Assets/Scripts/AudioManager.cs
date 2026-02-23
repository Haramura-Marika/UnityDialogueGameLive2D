using UnityEngine;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;
using AI.TTS;

[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    public enum TTSProviderType { Minimax, Qwen /*, Future: Azure, ElevenLabs, etc.*/ }

    [Header("TTS Provider 选择")]
    [SerializeField] private TTSProviderType providerType = TTSProviderType.Minimax;

    // 注意：API Key 现已移至 Resources/APISettings 配置文件中管理
    // 不再需要在每个 Manager 中单独配置

    private AudioSource ttsAudioSource;
    private CancellationTokenSource cancellationTokenSource;

    private readonly Queue<float> audioQueue = new Queue<float>();
    private readonly object queueLock = new object();
    private const int SampleRate = 16000;
    private const int Channels = 1;

    private ITTSProvider provider;
    private bool isAudioSourcePlaying = false; // 添加标记来跟踪播放状态

    private void Awake()
    {
        ttsAudioSource = GetComponent<AudioSource>();
        var clip = AudioClip.Create("StreamingTTS", SampleRate * 2, Channels, SampleRate, true, OnAudioRead);
        ttsAudioSource.clip = clip;
        ttsAudioSource.loop = true;

        ResolveProvider();
        
        // 验证配置
        if (AI.Config.AIAPISettings.Instance != null)
        {
            switch (providerType)
            {
                case TTSProviderType.Qwen:
                    if (AI.Config.AIAPISettings.Instance.QwenTTS.IsConfigured())
                        Debug.Log("[AudioManager] ? Qwen TTS 配置已加载");
                    else
                        Debug.LogWarning("[AudioManager] ?? Qwen TTS 未配置！");
                    break;
                case TTSProviderType.Minimax:
                    if (AI.Config.AIAPISettings.Instance.MinimaxTTS.IsConfigured())
                        Debug.Log("[AudioManager] ? Minimax TTS 配置已加载");
                    else
                        Debug.LogWarning("[AudioManager] ?? Minimax TTS 未配置！");
                    break;
            }
        }
        else
        {
            Debug.LogError("[AudioManager] ? AIAPISettings 未找到！请创建 Resources/APISettings 配置文件。");
        }
    }

    private void ResolveProvider()
    {
        switch (providerType)
        {
            case TTSProviderType.Qwen:
                provider = new QwenTTSProvider();
                break;
            case TTSProviderType.Minimax:
            default:
                provider = new MinimaxTTSProvider();
                break;
        }
    }

    public async Task PlayTTS(string text, CancellationToken externalCt = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("TTS文本为空，不进行播放。");
            return;
        }

        // ? 等待当前音频队列播放完毕（队列中样本数少于 0.5 秒）
        int waitCount = 0;
        int minQueueSize = SampleRate / 2; // 0.5 秒的音频数据
        while (audioQueue.Count > minQueueSize && waitCount < 200) // 最多等待 10 秒
        {
            await Task.Delay(50);
            waitCount++;
            
            if (waitCount % 10 == 0) // 每 500ms 打印一次
            {
                Debug.Log($"[AudioManager] 等待前序音频播放... 队列剩余: {audioQueue.Count} 样本");
            }
        }
        
        // 确保在主线程上操作 AudioSource
        if (!isAudioSourcePlaying)
        {
            ttsAudioSource.Play();
            isAudioSourcePlaying = true;
        }

        // 取消之前的 TTS 网络请求（但不影响已经在队列中的音频）
        cancellationTokenSource?.Cancel();
        cancellationTokenSource = new CancellationTokenSource();

        // 合并外部和内部 CancellationToken（如果需要）
        CancellationToken linkedToken = externalCt == default 
            ? cancellationTokenSource.Token 
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, externalCt).Token;

        try
        {
            // TTS 选项内部会自动读取配置，传 null 让方法内自己去读取！
            TTSSessionOptions options = new TTSSessionOptions
            {
                ApiKey = null,  // 让方法内部自动读取
                VoiceId = null, // 使用方法内的默认值
                LanguageType = null // 使用方法内的默认值
            };

            Debug.Log($"[AudioManager] 开始请求 TTS: {text?.Substring(0, Math.Min(20, text?.Length ?? 0))}...");
            
            // 记录请求前的队列大小
            int queueSizeBefore;
            lock (queueLock) { queueSizeBefore = audioQueue.Count; }
            
            await provider.StreamTTS(text, options, OnPcmChunk, linkedToken);
            
            // 记录请求后的队列大小
            int queueSizeAfter;
            lock (queueLock) { queueSizeAfter = audioQueue.Count; }
            
            Debug.Log($"[AudioManager] TTS 请求完成，新增音频: {queueSizeAfter - queueSizeBefore} 样本 (总队列: {queueSizeAfter})");
            
            // ? 等待新加入的音频数据开始播放（至少播放一部分）
            int newDataSize = queueSizeAfter - queueSizeBefore;
            if (newDataSize > 0)
            {
                // 等待至少 10% 的新数据被消费
                int targetQueueSize = queueSizeAfter - (newDataSize / 10);
                waitCount = 0;
                while (audioQueue.Count > targetQueueSize && waitCount < 100)
                {
                    await Task.Delay(50);
                    waitCount++;
                }
                Debug.Log($"[AudioManager] 音频开始播放，队列剩余: {audioQueue.Count} 样本");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.Log("[AudioManager] TTS播放已被取消！");
            
            // 只有在取消时才清空音频队列
            lock (queueLock)
            {
                audioQueue.Clear();
            }
            
            throw; // 重新抛出让调用者知道被取消了
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AudioManager] TTS Error: {ex.Message}");
        }
    }

    private void OnPcmChunk(byte[] pcmBytes)
    {
        // 转为 float 并入队
        lock (queueLock)
        {
            for (int i = 0; i < pcmBytes.Length; i += 2)
            {
                if (i + 1 < pcmBytes.Length)
                {
                    short sample = (short)(pcmBytes[i + 1] << 8 | pcmBytes[i]);
                    audioQueue.Enqueue(sample / 32768.0f);
                }
            }
        }
    }

    private void OnAudioRead(float[] data)
    {
        lock (queueLock)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (audioQueue.Count > 0)
                {
                    data[i] = audioQueue.Dequeue();
                }
                else
                {
                    data[i] = 0;
                }
            }
        }
    }

    private void OnDestroy()
    {
        cancellationTokenSource?.Cancel();
        isAudioSourcePlaying = false;
    }
}
