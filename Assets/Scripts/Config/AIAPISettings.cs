using System;
using UnityEngine;

namespace AI.Config
{
    /// <summary>
    /// API 配置基类
    /// </summary>
    [Serializable]
    public abstract class APIConfigBase
    {
        [SerializeField] private string apiKey = "";

        /// <summary>
        /// 用于派生类指定环境变量名称（如 "GEMINI_API_KEY"）
        /// </summary>
        protected virtual string EnvVarName => null;

        public string ApiKey
        {
            get
            {
                // 优先从环境变量读取
                if (!string.IsNullOrEmpty(EnvVarName))
                {
                    // 依次查找 Process、User、Machine 级别
                    var envValue = System.Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.Process);
                    if (string.IsNullOrEmpty(envValue))
                        envValue = System.Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.User);
                    if (string.IsNullOrEmpty(envValue))
                        envValue = System.Environment.GetEnvironmentVariable(EnvVarName, EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrEmpty(envValue))
                    {
                        return envValue;
                    }
                }
                return apiKey;
            }
            set => apiKey = value;
        }

        public virtual bool IsConfigured()
        {
            return !string.IsNullOrEmpty(ApiKey);
        }
    }

    /// <summary>
    /// DeepSeek API 配置
    /// </summary>
    [Serializable]
    public class DeepSeekConfig : APIConfigBase
    {
        protected override string EnvVarName => "DEEPSEEK_API_KEY";

        [SerializeField] private string model = "deepseek-chat";
        [SerializeField] [Range(0f, 2f)] private float temperature = 1.0f;
        [SerializeField] private int maxTokens = 8192;
        [SerializeField] private bool streamingEnabled = false; // 是否流式输出

        public string Model
        {
            get => model;
            set => model = value;
        }

        [Tooltip("Temperature (0-2): 控制输出的随机性，值越高越随机")]
        public float Temperature
        {
            get => temperature;
            set => temperature = Mathf.Clamp(value, 0f, 2f);
        }

        public int MaxTokens
        {
            get => maxTokens;
            set => maxTokens = Mathf.Max(1, value);
        }

        public bool StreamingEnabled
        {
            get => streamingEnabled;
            set => streamingEnabled = value;
        }
    }

    /// <summary>
    /// Gemini API 配置
    /// </summary>
    [Serializable]
    public class GeminiConfig : APIConfigBase
    {
        protected override string EnvVarName => "GEMINI_API_KEY";

        [SerializeField] private string model = "gemini-2.0-flash-exp";
        [SerializeField] [Range(0f, 2f)] private float temperature = 1.0f;
        [SerializeField] private int maxTokens = 8192;
        [SerializeField] private bool streamingEnabled = false; // 是否流式输出

        public string Model
        {
            get => model;
            set => model = value;
        }

        [Tooltip("Temperature (0-2): 控制输出的随机性，值越高越随机")]
        public float Temperature
        {
            get => temperature;
            set => temperature = Mathf.Clamp(value, 0f, 2f);
        }

        public int MaxTokens
        {
            get => maxTokens;
            set => maxTokens = Mathf.Max(1, value);
        }

        public bool StreamingEnabled
        {
            get => streamingEnabled;
            set => streamingEnabled = value;
        }
    }

    /// <summary>
    /// Qwen (通义千问) 对话配置
    /// </summary>
    [Serializable]
    public class QwenConfig : APIConfigBase
    {
        protected override string EnvVarName => "QWEN_API_KEY";

        [SerializeField] private string model = "qwen-plus";
        [SerializeField] [Range(0f, 2f)] private float temperature = 1.0f;
        [SerializeField] private int maxTokens = 8192;
        [SerializeField] private bool streamingEnabled = false; // 是否流式输出

        public string Model
        {
            get => model;
            set => model = value;
        }

        [Tooltip("Temperature (0-2): 控制输出的随机性，值越高越随机")]
        public float Temperature
        {
            get => temperature;
            set => temperature = Mathf.Clamp(value, 0f, 2f);
        }

        public int MaxTokens
        {
            get => maxTokens;
            set => maxTokens = Mathf.Max(1, value);
        }

        public bool StreamingEnabled
        {
            get => streamingEnabled;
            set => streamingEnabled = value;
        }
    }

    /// <summary>
    /// Qwen ASR (语音识别) 配置
    /// </summary>
    [Serializable]
    public class QwenASRConfig : APIConfigBase
    {
        protected override string EnvVarName => "QWEN_ASR_API_KEY";

        [SerializeField] private string model = "paraformer-realtime-v2";
        [SerializeField] private bool enableLid = true;
        [SerializeField] private bool enableItn = false;

        public string Model
        {
            get => model;
            set => model = value;
        }

        public bool EnableLid
        {
            get => enableLid;
            set => enableLid = value;
        }

        public bool EnableItn
        {
            get => enableItn;
            set => enableItn = value;
        }
    }

    /// <summary>
    /// Qwen TTS (语音合成) 配置
    /// </summary>
    [Serializable]
    public class QwenTTSConfig : APIConfigBase
    {
        protected override string EnvVarName => "QWEN_TTS_API_KEY";

        [SerializeField] private string model = "qwen3-tts-flash";
        [SerializeField] private string voiceId = "Cherry";
        [SerializeField] private string languageType = "Chinese";
        [SerializeField] private int sampleRate = 16000;

        public string Model
        {
            get => model;
            set => model = value;
        }

        public string VoiceId
        {
            get => voiceId;
            set => voiceId = value;
        }

        public string LanguageType
        {
            get => languageType;
            set => languageType = value;
        }

        public int SampleRate
        {
            get => sampleRate;
            set => sampleRate = value;
        }
    }

    /// <summary>
    /// Minimax TTS 配置
    /// </summary>
    [Serializable]
    public class MinimaxTTSConfig : APIConfigBase
    {
        protected override string EnvVarName => "MINIMAX_API_KEY";

        [SerializeField] private string groupId = "";
        [SerializeField] private string voiceId = "male-qn-qingse";
        [SerializeField] private string model = "speech-02-turbo";
        [SerializeField] private int sampleRate = 16000;

        public string GroupId
        {
            get => groupId;
            set => groupId = value;
        }

        public string VoiceId
        {
            get => voiceId;
            set => voiceId = value;
        }

        public string Model
        {
            get => model;
            set => model = value;
        }

        public int SampleRate
        {
            get => sampleRate;
            set => sampleRate = value;
        }

        public override bool IsConfigured()
        {
            return base.IsConfigured() && !string.IsNullOrEmpty(groupId);
        }
    }

    /// <summary>
    /// AI 服务配置管理器 - ScriptableObject 模式
    /// 通过 Unity Inspector 方便管理各类 API 配置
    /// 创建方式: Assets -> Create -> AI Config -> API Settings
    /// </summary>
    [CreateAssetMenu(fileName = "APISettings", menuName = "AI Config/API Settings", order = 1)]
    public class AIAPISettings : ScriptableObject
    {
        private static AIAPISettings _instance;

        [Header("=== DeepSeek 配置 ===")]
        [SerializeField] private DeepSeekConfig deepSeekConfig = new DeepSeekConfig();

        [Header("=== Gemini 配置 ===")]
        [SerializeField] private GeminiConfig geminiConfig = new GeminiConfig();

        [Header("=== Qwen (通义千问对话) 配置 ===")]
        [SerializeField] private QwenConfig qwenConfig = new QwenConfig();

        [Header("=== Qwen ASR (语音识别) 配置 ===")]
        [SerializeField] private QwenASRConfig qwenASRConfig = new QwenASRConfig();

        [Header("=== Qwen TTS (语音合成) 配置 ===")]
        [SerializeField] private QwenTTSConfig qwenTTSConfig = new QwenTTSConfig();

        [Header("=== Minimax TTS 配置 ===")]
        [SerializeField] private MinimaxTTSConfig minimaxTTSConfig = new MinimaxTTSConfig();

        /// <summary>
        /// 单例实例 - 从 Resources 文件夹加载
        /// 确保在 Resources 文件夹中有一个名为 "APISettings" 的配置文件
        /// </summary>
        public static AIAPISettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<AIAPISettings>("APISettings");
                    if (_instance == null)
                    {
                        Debug.LogWarning("[AIAPISettings] 未找到 Resources/APISettings 配置文件。" +
                            "\n请在 Project 窗口右键 -> Create -> AI Config -> API Settings 创建配置文件，" +
                            "\n并放入到 Resources 文件夹中，命名为 'APISettings'");
                    }
                }
                return _instance;
            }
        }

        // 配置访问器
        public DeepSeekConfig DeepSeek => deepSeekConfig;
        public GeminiConfig Gemini => geminiConfig;
        public QwenConfig Qwen => qwenConfig;
        public QwenASRConfig QwenASR => qwenASRConfig;
        public QwenTTSConfig QwenTTS => qwenTTSConfig;
        public MinimaxTTSConfig MinimaxTTS => minimaxTTSConfig;

        /// <summary>
        /// 验证所有配置是否正确
        /// </summary>
        public void ValidateConfigs()
        {
            Debug.Log("=== API 配置验证 ===");
            Debug.Log($"DeepSeek: {(deepSeekConfig.IsConfigured() ? "已配置" : "未配置")}");
            Debug.Log($"Gemini: {(geminiConfig.IsConfigured() ? "已配置" : "未配置")}");
            Debug.Log($"Qwen: {(qwenConfig.IsConfigured() ? "已配置" : "未配置")}");
            Debug.Log($"Qwen ASR: {(qwenASRConfig.IsConfigured() ? "已配置" : "未配置")}");
            Debug.Log($"Qwen TTS: {(qwenTTSConfig.IsConfigured() ? "已配置" : "未配置")}");
            Debug.Log($"Minimax TTS: {(minimaxTTSConfig.IsConfigured() ? "已配置" : "未配置")}");
        }

#if UNITY_EDITOR
        [ContextMenu("验证配置")]
        private void ValidateConfigsInEditor()
        {
            ValidateConfigs();
        }

        [ContextMenu("设置 Qwen API Key (示例)")]
        private void SetQwenAPIKeyExample()
        {
            string exampleKey = "sk-your-api-key-here";
            qwenASRConfig.ApiKey = exampleKey;
            qwenTTSConfig.ApiKey = exampleKey;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log("请在 Inspector 中修改为实际的 API Key");
        }
#endif
    }
}
