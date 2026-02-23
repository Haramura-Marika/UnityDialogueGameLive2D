using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.EventSystems;
using AI.STT;

public class InputManager : MonoBehaviour
{
    public enum InputMode { Text, Voice }

    [Header("UI组件引用")]
    [SerializeField] private GameObject textInputGroup;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button inputModeButton;
    [SerializeField] private TextMeshProUGUI inputModeButtonText;
    [SerializeField] private GameObject voiceRecordButton;
    [SerializeField] private GameObject modeSelectButton; // 模型选择按钮

    [Header("本地录音设置")]
    [SerializeField] private int recordSampleRate = 16000;
    [SerializeField] private int maxRecordSeconds = 30;

    private InputMode currentMode = InputMode.Text;
    private string micDevice = null;
    private AudioClip recordingClip;
    private bool isRecording = false;

    public static event Action<string> OnMessageSent;

    private void Start()
    {
        sendButton.onClick.AddListener(SendMessage);
        chatInputField.onSubmit.AddListener((text) => SendMessage());
        inputModeButton.onClick.AddListener(ToggleInputMode);
        UpdateUIForCurrentMode();
        
        // 验证 API 配置
        if (AI.Config.AIAPISettings.Instance != null)
        {
            var config = AI.Config.AIAPISettings.Instance.QwenASR;
            if (config.IsConfigured())
            {
                Debug.Log("[InputManager] Qwen ASR 配置已加载");
            }
            else
            {
                Debug.LogWarning("[InputManager] Qwen ASR 未配置，请到 Resources/APISettings 设置 API Key。");
            }
        }
        else
        {
            Debug.LogError("[InputManager] AIAPISettings 未找到，请创建 Resources/APISettings 配置文件。");
        }
    }

    public void StartRecording()
    {
        if (currentMode != InputMode.Voice) return;
        if (isRecording) return;

        if (!MicrophoneIsAvailable())
        {
            Debug.LogWarning("[InputManager] 未检测到麦克风设备。");
            return;
        }

        try
        {
            recordingClip = Microphone.Start(micDevice, false, maxRecordSeconds, recordSampleRate);
            isRecording = true;
            Debug.Log($"[InputManager] 开始录音... (目标采样率: {recordSampleRate}Hz, 最长时间: {maxRecordSeconds}秒)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[InputManager] 启动录音失败: {ex.Message}");
            isRecording = false;
        }
    }

    private bool MicrophoneIsAvailable()
    {
        try { return Microphone.devices != null && Microphone.devices.Length > 0; }
        catch { return false; }
    }

    public async void StopRecording()
    {
        if (currentMode != InputMode.Voice) return;
        if (!isRecording || recordingClip == null)
        {
            Debug.LogWarning("[InputManager] 未处于录音状态。");
            return;
        }

        try
        {
            int position = Microphone.GetPosition(micDevice);
            int channels = recordingClip.channels;
            int srcRate = recordingClip.frequency; // 实际录音采样率

            Debug.Log($"[InputManager] 录音结束 - 位置: {position} samples, 声道: {channels}, 实际采样率: {srcRate}Hz");

            if (position <= 0)
            {
                Debug.LogWarning("[InputManager] 未检测到有效音频（position <= 0）。");
                Microphone.End(micDevice);
                isRecording = false;
                return;
            }

            // 先读取样本，再结束麦克风，避免个别平台 End 后数据丢失
            float[] allSamples = new float[position * channels];
            recordingClip.GetData(allSamples, 0);
            Microphone.End(micDevice);
            isRecording = false;

            // 转单声道（如为多声道，取均值）
            float[] mono = new float[position];
            if (channels == 1)
            {
                Array.Copy(allSamples, mono, mono.Length);
            }
            else
            {
                for (int i = 0, o = 0; i < allSamples.Length && o < mono.Length; i += channels, o++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++) sum += allSamples[i + c];
                    mono[o] = sum / channels;
                }
            }

            // 计算音频能量，检测是否为静音
            float totalEnergy = 0f;
            for (int i = 0; i < mono.Length; i++)
            {
                totalEnergy += Mathf.Abs(mono[i]);
            }
            float avgEnergy = totalEnergy / mono.Length;
            Debug.Log($"[InputManager] 音频平均能量: {avgEnergy:F6} (过小太低可能是静音)");

            if (avgEnergy < 0.0001f)
            {
                Debug.LogWarning("[InputManager] 音频能量过低，可能未录制到声音（检查麦克风权限或音量）");
            }

            // 统一重采样到 16k（或使用 recordSampleRate 指定值）
            int targetRate = Mathf.Max(8000, recordSampleRate);
            float[] resampled = mono;
            int usedRate = srcRate;
            if (srcRate != targetRate)
            {
                resampled = ResampleLinear(mono, srcRate, targetRate);
                usedRate = targetRate;
                Debug.Log($"[InputManager] 已重采样: {srcRate}Hz -> {targetRate}Hz, 样本数: {mono.Length} -> {resampled.Length}");
            }

            // 若实际采样率与设定不同，直接按 usedRate 写入 WAV 头
            byte[] pcm16 = FloatToPcm16(resampled);
            byte[] wav = WrapWavHeader(pcm16, usedRate, 1, 16);

            Debug.Log($"[InputManager] WAV 数据大小: {wav.Length} bytes ({wav.Length / 1024.0:F2} KB)");

            Debug.Log($"[InputManager] 开始 ASR 识别... (样本数: {resampled.Length}, 采样率: {usedRate}Hz)");
            
            // 调用 ASR 服务，API Key 从配置读取
            var text = await Qwen_ASR_Service.TranscribeBytes(null, wav, format: "wav", sampleRate: usedRate);
            
            if (!string.IsNullOrWhiteSpace(text))
            {
                Debug.Log($"[InputManager] ASR 识别成功: {text}");
                OnMessageSent?.Invoke(text);
            }
            else
            {
                Debug.LogWarning("[InputManager] ASR 未识别到文本或请求失败。建议:\n" +
                    "1. API Key 是否正确配置\n" +
                    "2. 网络连接是否正常\n" +
                    "3. 音频是否有效（检查音量和值）\n" +
                    "4. 查看 Qwen_ASR_Service 的详细错误日志");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[InputManager] ASR 识别异常: {ex.Message}\n堆栈: {ex.StackTrace}");
        }
    }

    private static float[] ResampleLinear(float[] input, int srcRate, int dstRate)
    {
        if (input == null || input.Length == 0 || srcRate <= 0 || dstRate <= 0 || srcRate == dstRate)
            return input;

        double ratio = (double)dstRate / srcRate;
        int newLen = Math.Max(1, (int)Mathf.Round((float)(input.Length * ratio)));
        float[] output = new float[newLen];
        double step = (double)srcRate / dstRate;
        for (int i = 0; i < newLen; i++)
        {
            double pos = i * step;
            int idx = (int)Math.Floor(pos);
            double frac = pos - idx;
            float a = input[Mathf.Clamp(idx, 0, input.Length - 1)];
            float b = input[Mathf.Clamp(idx + 1, 0, input.Length - 1)];
            output[i] = (float)(a + (b - a) * frac);
        }
        return output;
    }

    private static byte[] FloatToPcm16(float[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        int bi = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            short val = (short)Mathf.RoundToInt(clamped * 32767f);
            bytes[bi++] = (byte)(val & 0xff);
            bytes[bi++] = (byte)((val >> 8) & 0xff);
        }
        return bytes;
    }

    private static byte[] WrapWavHeader(byte[] pcm, int sampleRate, int channels, int bitsPerSample)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int subchunk2Size = pcm.Length;
        int chunkSize = 36 + subchunk2Size;

        byte[] header = new byte[44];
        header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
        BitConverter.GetBytes(chunkSize).CopyTo(header, 4);
        header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';
        header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
        BitConverter.GetBytes(16).CopyTo(header, 16);
        BitConverter.GetBytes((short)1).CopyTo(header, 20);
        BitConverter.GetBytes((short)channels).CopyTo(header, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(header, 24);
        BitConverter.GetBytes(byteRate).CopyTo(header, 28);
        BitConverter.GetBytes((short)blockAlign).CopyTo(header, 32);
        BitConverter.GetBytes((short)bitsPerSample).CopyTo(header, 34);
        header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
        BitConverter.GetBytes(subchunk2Size).CopyTo(header, 40);

        byte[] wav = new byte[header.Length + pcm.Length];
        Buffer.BlockCopy(header, 0, wav, 0, header.Length);
        Buffer.BlockCopy(pcm, 0, wav, header.Length, pcm.Length);
        return wav;
    }

    private void ToggleInputMode()
    {
        currentMode = (currentMode == InputMode.Text) ? InputMode.Voice : InputMode.Text;
        UpdateUIForCurrentMode();
    }

    private void UpdateUIForCurrentMode()
    {
        if (currentMode == InputMode.Text)
        {
            textInputGroup.SetActive(true);
            voiceRecordButton.SetActive(false);
            inputModeButtonText.text = "文";
            // 文字输入模式下显示模型选择按钮
            if (modeSelectButton != null)
            {
                modeSelectButton.SetActive(true);
            }
        }
        else
        {
            textInputGroup.SetActive(false);
            voiceRecordButton.SetActive(true);
            inputModeButtonText.text = "语";
            // 语音输入模式下隐藏模型选择按钮
            if (modeSelectButton != null)
            {
                modeSelectButton.SetActive(false);
            }
        }
    }

    private void SendMessage()
    {
        string message = chatInputField.text;
        if (string.IsNullOrWhiteSpace(message)) return;
        OnMessageSent?.Invoke(message);
        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }
}
