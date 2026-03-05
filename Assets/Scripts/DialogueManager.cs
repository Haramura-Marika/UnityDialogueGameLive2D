using UnityEngine;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using TMPro;
using System.Collections.Generic;
using AI.Providers;
using AI.Chat; // 引入 CharacterProfile 所在的命名空间
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("核心组件")] [SerializeField] private Live2DController live2DController;
    [SerializeField] private AudioManager audioManager; // AudioManager的引用

    [Header("聊天UI组件")] [SerializeField] private Transform chatContainer;
    [SerializeField] private GameObject userMessagePrefab;
    [SerializeField] private GameObject aiMessagePrefab;
    [SerializeField] private UnityEngine.UI.ScrollRect chatScrollRect; // 可选：滚动视图引用，用于自动滚动到底部

    [Header("角色卡")]
    // [SerializeField] private CharacterProfile currentCharacter; // 已移除
    [SerializeField]
    private TextAsset characterCardFile; // 支持拖拽文本文件

    private CharacterProfile _activeProfile; // 当前激活的角色配置
    private bool isWaitingForAI = false;
    private List<ChatMessage> chatHistory;

    // 当前登录用户名（用于数据库存储）
    private string _currentUsername;

    // 角色数值
    public int currentAffinity { get; private set; } = 50;
    public int currentMood { get; private set; } = 50;
    public int currentEnergy { get; private set; } = 50;
    public int currentStress { get; private set; } = 50;
    public int currentTrust { get; private set; } = 50;

    // (affinity, mood, energy, stress, trust)
    public static event Action<int, int, int, int, int> OnStatsChanged;

    // 流式UI调度
    private readonly ConcurrentQueue<Action> _uiQueue = new ConcurrentQueue<Action>();
    private float _lastStreamUiUpdateTime = 0f;
    private const float StreamUiInterval = 0.05f; // 50ms 刷新一次
    private Stopwatch _streamStopwatch; // 后台线程安全计时器

    // TTS 管理
    private CancellationTokenSource _currentTTSCancellation;
    private bool _isTTSPlaying = false;

    // TTS 播放队列（用于从回调线程调度到主线程）
    private readonly ConcurrentQueue<(string text, Action onComplete)> _ttsQueue =
        new ConcurrentQueue<(string, Action)>();

    private bool _isProcessingTTS = false; // 标记是否正在处理 TTS

    private bool _chatInitialized = false;
    private bool _chatInitializing = false;

    private void Awake()
    {
        // 单例模式
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        // 暂停时不处理任何队列
        if (UIManager.Instance != null && UIManager.Instance.IsPaused())
        {
            return;
        }

        // 执行回调线程的UI操作（主线程执行）
        int max = 32;
        while (max-- > 0 && _uiQueue.TryDequeue(out var act))
        {
            try
            {
                act?.Invoke();
            }
            catch
            {
            }
        }

        // 处理 TTS 播放队列，一次只处理一个（避免并发）
        if (!_isProcessingTTS && _ttsQueue.TryDequeue(out var ttsRequest))
        {
            UnityEngine.Debug.Log($"[DialogueManager] Update: 开始处理TTS队列，剩余数量: {_ttsQueue.Count}");
            _isProcessingTTS = true;
            _ = ProcessTTSRequest(ttsRequest);
        }
    }

    /// <summary>
    /// 处理单个 TTS 请求（串行执行）
    /// </summary>
    private async Task ProcessTTSRequest((string text, Action onComplete) request)
    {
        try
        {
            await PlayTTSOnMainThread(request.text, _currentTTSCancellation?.Token ?? default, request.onComplete);

            // ? 等待音频真正播放完毕
            // PlayTTS 只是将音频数据加入队列，需要等待队列播放完
            if (audioManager != null)
            {
                // 给一点时间让音频队列播放
                await Task.Delay(100);
            }
        }
        finally
        {
            _isProcessingTTS = false;
        }
    }

    private void Start()
    {
        _streamStopwatch = Stopwatch.StartNew();

        // 确保 chatContainer 有正确的布局组件配置
        EnsureChatContainerLayout();

        // 不在 Start 中初始化对话，由 UIManager.EnterGame() 调用 InitChat()
    }

    /// <summary>
    /// 由 UIManager 在登录成功进入游戏时调用，初始化或恢复对话
    /// </summary>
    public void InitChat()
    {
        // 防止重复初始化
        if (_chatInitialized || _chatInitializing)
        {
            return;
        }

        _chatInitializing = true;

        // 刷新当前用户名（登录后才有值）
        _currentUsername = PlayerPrefs.GetString("current_user", string.Empty);

        try
        {
            // 尝试从数据库加载历史记录
            if (!string.IsNullOrEmpty(_currentUsername) && DatabaseManager.Instance != null)
            {
                var savedHistory = DatabaseManager.Instance.LoadChatHistory(_currentUsername);
                if (savedHistory != null && savedHistory.chatHistory != null && savedHistory.chatHistory.Count >= 2)
                {
                    // 合法性校验：如果历史记录中只有系统消息+开场白（2条），
                    // 说明没有真正的对话内容，直接开始新对话即可
                    if (savedHistory.chatHistory.Count > 2)
                    {
                        Debug.Log(
                            $"[DialogueManager] 从数据库加载用户 {_currentUsername} 的历史记录 (角色: {savedHistory.characterName}, 消息数: {savedHistory.chatHistory.Count})");

                        var textAsset = Resources.Load<TextAsset>($"Characters/{savedHistory.characterName}");
                        if (textAsset != null)
                        {
                            _activeProfile =
                                CharacterCardParser.ParseFromText(textAsset.text, savedHistory.characterName);
                        }
                        else
                        {
                            _activeProfile = ScriptableObject.CreateInstance<CharacterProfile>();
                            _activeProfile.characterName = savedHistory.characterName;
                        }

                        this.chatHistory = savedHistory.chatHistory;
                        currentAffinity = savedHistory.affinity;
                        currentMood = savedHistory.mood;
                        currentEnergy = savedHistory.energy;
                        currentStress = savedHistory.stress;
                        currentTrust = savedHistory.trust;
                        ClearUI();
                        RebuildUIFromHistory();

                        // 初始更新UI
                        OnStatsChanged?.Invoke(currentAffinity, currentMood, currentEnergy, currentStress,
                            currentTrust);
                        return;
                    }
                }
            }

            InitializeChatHistory();
        }
        finally
        {
            _chatInitializing = false;
        }
    }

    /// <summary>
    /// 确保 chatContainer (Content) 有正确的布局组件配置
    /// </summary>
    private void EnsureChatContainerLayout()
    {
        if (chatContainer == null) return;

        // chatContainer 应该是 ScrollView -> Viewport -> Content
        var rectTransform = chatContainer as RectTransform;
        if (rectTransform == null) return;

        // 添加 VerticalLayoutGroup（如果没有）
        var layoutGroup = chatContainer.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = chatContainer.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        }

        // 配置 VerticalLayoutGroup - 让行容器填充整个宽度
        layoutGroup.childAlignment = TextAnchor.UpperLeft;
        layoutGroup.childControlWidth = true; // 控制宽度，让行容器填满
        layoutGroup.childControlHeight = true;
        layoutGroup.childForceExpandWidth = true; // 强制拉伸宽度
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.spacing = 10f;
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);

        // Content 需要 ContentSizeFitter 让高度随内容增长（这样才能滚动）
        var sizeFitter = chatContainer.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (sizeFitter == null)
        {
            sizeFitter = chatContainer.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        }

        sizeFitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        sizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        // 设置 Content 的锚点：顶部对齐，水平拉伸
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(1, 1);
        rectTransform.pivot = new Vector2(0.5f, 1);

        // 重置位置，确保从顶部开始，并且左右没有偏移
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(0, 0); // 关键：将左右偏移（Right/Left）重置为0
    }

    // --- 新增功能区域 ---

    /// <summary>
    /// 切换角色（通过 Resources/Characters 下的文件名）
    /// </summary>
    public void SwitchCharacter(string characterName)
    {
        var textAsset = Resources.Load<TextAsset>($"Characters/{characterName}");
        if (textAsset == null)
        {
            UnityEngine.Debug.LogError($"[DialogueManager] 找不到角色卡文件: Characters/{characterName}");
            return;
        }

        var profile = BuildProfileFromCard(textAsset, characterName);
        StartNewChat(profile);
    }

    /// <summary>
    /// 保存当前进度
    /// </summary>
    public void SaveProgress(string slotName = "autosave")
    {
        // 优先保存到数据库
        if (!string.IsNullOrEmpty(_currentUsername) && DatabaseManager.Instance != null && chatHistory != null)
        {
            string charName = _activeProfile != null ? _activeProfile.characterName : "Unknown";
            DatabaseManager.Instance.SaveChatHistory(_currentUsername, charName, chatHistory, currentAffinity,
                currentMood, currentEnergy, currentStress, currentTrust, slotName);
            return;
        }

        // 回退到文件保存
        var data = BuildSaveData();
        if (data == null) return;
        AI.Chat.SaveSystem.SaveGame(slotName, data);
    }

    public void SaveProgressToPath(string path)
    {
        var data = BuildSaveData();
        if (data == null) return;
        AI.Chat.SaveSystem.SaveGameToPath(path, data);
    }

    /// <summary>
    /// 读取进度
    /// </summary>
    public void LoadProgress(string slotName = "autosave")
    {
        // 优先从数据库加载
        if (!string.IsNullOrEmpty(_currentUsername) && DatabaseManager.Instance != null)
        {
            var historyData = DatabaseManager.Instance.LoadChatHistory(_currentUsername, slotName);
            if (historyData != null)
            {
                var textAsset = Resources.Load<TextAsset>($"Characters/{historyData.characterName}");
                if (textAsset != null)
                {
                    _activeProfile = CharacterCardParser.ParseFromText(textAsset.text, historyData.characterName);
                }
                else
                {
                    _activeProfile = ScriptableObject.CreateInstance<CharacterProfile>();
                    _activeProfile.characterName = historyData.characterName;
                }

                this.chatHistory = historyData.chatHistory;
                currentAffinity = historyData.affinity;
                currentMood = historyData.mood;
                currentEnergy = historyData.energy;
                currentStress = historyData.stress;
                currentTrust = historyData.trust;
                ClearUI();
                RebuildUIFromHistory();

                // 初始更新UI
                OnStatsChanged?.Invoke(currentAffinity, currentMood, currentEnergy, currentStress, currentTrust);
                Debug.Log($"[DialogueManager] 从数据库加载进度: {historyData.characterName} ({historyData.timestamp})");
                return;
            }
        }

        // 回退到文件加载
        var data = AI.Chat.SaveSystem.LoadGame(slotName);
        ApplyLoadedData(data);
    }

    public void LoadProgressFromPath(string path)
    {
        var data = AI.Chat.SaveSystem.LoadGameFromPath(path);
        ApplyLoadedData(data);
    }

    private SaveData BuildSaveData()
    {
        if (chatHistory == null) return null;

        return new AI.Chat.SaveData
        {
            characterName = _activeProfile != null ? _activeProfile.characterName : "Unknown",
            chatHistory = this.chatHistory,
            affinity = currentAffinity,
            mood = currentMood,
            energy = currentEnergy,
            stress = currentStress,
            trust = currentTrust,
            timestamp = DateTime.Now.ToString()
        };
    }

    private void ApplyLoadedData(AI.Chat.SaveData data)
    {
        if (data == null) return;

        var textAsset = Resources.Load<TextAsset>($"Characters/{data.characterName}");
        if (textAsset != null)
        {
            _activeProfile = CharacterCardParser.ParseFromText(textAsset.text, data.characterName);
        }
        else
        {
            _activeProfile = ScriptableObject.CreateInstance<CharacterProfile>();
            _activeProfile.characterName = data.characterName;
        }

        this.chatHistory = data.chatHistory;
        currentAffinity = data.affinity;
        currentMood = data.mood;
        currentEnergy = data.energy;
        currentStress = data.stress;
        currentTrust = data.trust;

        ClearUI();
        RebuildUIFromHistory();

        // 初始更新UI
        OnStatsChanged?.Invoke(currentAffinity, currentMood, currentEnergy, currentStress, currentTrust);

        UnityEngine.Debug.Log($"[DialogueManager] 已加载进度: {data.characterName} ({data.timestamp})");
    }

    /// <summary>
    /// 重启当前对话（相当于重新开始游戏）
    /// </summary>
    public void RestartChat()
    {
        _chatInitialized = false;
        _chatInitializing = false;
        _activeProfile = null;

        // 清除数据库中的旧存档
        if (!string.IsNullOrEmpty(_currentUsername) && DatabaseManager.Instance != null)
        {
            DatabaseManager.Instance.DeleteSaveSlot(_currentUsername, "autosave");
        }

        ClearUI();

        // 重新从角色卡文件初始化（async方法，内部会设置 _chatInitialized）
        InitializeChatHistory();
    }

    /// <summary>
    /// 重置会话状态（切换用户时调用），清除对话数据和UI，但不重新初始化
    /// </summary>
    public void ResetSession()
    {
        _chatInitialized = false;
        _chatInitializing = false;
        _activeProfile = null;
        _currentUsername = null;
        chatHistory = null;

        // 停止当前 TTS
        StopCurrentTTS();

        ClearUI();

        Debug.Log("[DialogueManager] 会话已重置，等待新用户登录");
    }

    private void ClearUI()
    {
        if (chatContainer == null) return;
        foreach (Transform child in chatContainer)
        {
            Destroy(child.gameObject);
        }
    }

    private void RebuildUIFromHistory()
    {
        if (chatHistory == null) return;

        // 跳过 [0]=系统消息 和 [1]=开场白，只恢复真正的对话内容
        for (int i = 2; i < chatHistory.Count; i++)
        {
            var msg = chatHistory[i];

            string text = "";
            if (msg.parts != null)
            {
                foreach (var p in msg.parts) text += p.text;
            }

            if (msg.role == "assistant")
            {
                var data = ParseAIResponse(text);
                if (data == null)
                {
                    string unescaped = UnescapeJsonLike(text);
                    if (unescaped != text)
                    {
                        data = ParseAIResponse(unescaped);
                    }
                }

                if (data != null && !string.IsNullOrEmpty(data.dialogue))
                {
                    AddMessageToUI(data.dialogue, false);
                }
                else
                {
                    AddMessageToUI(text, false);
                }
            }
            else
            {
                AddMessageToUI(text, true);
            }
        }
    }

    private string UnescapeJsonLike(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        try
        {
            // 处理常见的反斜杠转义
            return s.Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }
        catch
        {
            return s;
        }
    }

    // --- 结束新增功能区域 ---

    private void OnEnable()
    {
        InputManager.OnMessageSent += HandleMessageSent;
    }

    private void OnDisable()
    {
        InputManager.OnMessageSent -= HandleMessageSent;
    }

    /// <summary>
    /// 停止当前正在播放的 TTS
    /// </summary>
    private void StopCurrentTTS()
    {
        if (_isTTSPlaying && _currentTTSCancellation != null)
        {
            UnityEngine.Debug.Log("[DialogueManager] 停止当前 TTS 播放");
            _currentTTSCancellation.Cancel();
            _currentTTSCancellation = null;
            _isTTSPlaying = false;

            // 清空 TTS 队列
            while (_ttsQueue.TryDequeue(out _))
            {
            }

            _isProcessingTTS = false;

            // 清空 AudioManager 的音频队列
            if (audioManager != null)
            {
                // AudioManager 会在 PlayTTS 被取消时自动清理
            }
        }
    }

    private async void HandleMessageSent(string message)
    {
        // 暂停时不处理消息
        if (UIManager.Instance != null && UIManager.Instance.IsPaused())
        {
            return;
        }

        if (isWaitingForAI) return;

        // chatHistory 尚未初始化（Start 中的异步初始化还未完成）
        if (chatHistory == null)
        {
            UnityEngine.Debug.LogWarning("[DialogueManager] chatHistory 尚未初始化，忽略本次消息");
            return;
        }

        isWaitingForAI = true;

        // 用户发送新消息时，立即打断当前的 TTS
        StopCurrentTTS();

        AddMessageToUI(message, true);
        chatHistory.Add(new ChatMessage
            { role = "user", parts = new List<ChatPart> { new ChatPart { text = message } } });

        // 新建"思考中"气泡
        MessageBubble thinkingBubble = AddMessageToUI("...", false);

        // 构造发送给AI的对话历史（克隆一份，把当前状态注入到最后一条用户消息中，避免污染UI和数据库存档）
        var chatHistoryForAI = new List<ChatMessage>();
        foreach (var msg in chatHistory)
        {
            chatHistoryForAI.Add(new ChatMessage
            {
                role = msg.role,
                parts = new List<ChatPart> { new ChatPart { text = msg.parts != null && msg.parts.Count > 0 ? msg.parts[0].text : "" } }
            });
        }

        var lastUserMsg = chatHistoryForAI[chatHistoryForAI.Count - 1];
        lastUserMsg.parts[0].text += $"\n\n[系统提示：你当前的状态值（范围0-100）：好感度={currentAffinity}，心情={currentMood}，精力={currentEnergy}，压力={currentStress}，信任度={currentTrust}。请结合这些状态值以及历史对话，调整你的预期、态度和用词，并在返回的JSON中给出合理的变化量。]";

        // 根据当前 Provider 与配置，决定是否使用流式（支持 Qwen、DeepSeek 和 Gemini）
        var provider = AIService.GetProvider();
        string serviceProp = provider == AIModelProvider.Qwen ? "Qwen"
            : provider == AIModelProvider.DeepSeek ? "DeepSeek"
            : provider == AIModelProvider.Gemini ? "Gemini"
            : provider == AIModelProvider.Minimax ? "Minimax"
            : "Gemini";
        bool useStream = (provider == AIModelProvider.Qwen
                          || provider == AIModelProvider.DeepSeek
                          || provider == AIModelProvider.Gemini
                          || provider == AIModelProvider.Minimax)
                         && ChatConfig.GetStreamingEnabled(serviceProp, false);

        if (useStream)
        {
            var cts = new CancellationTokenSource();
            var sb = new StringBuilder();
            var tcs = new TaskCompletionSource<string>();

            // 流式 TTS 控制：边生成边播放（分句播放）
            StringBuilder ttsBuffer = new StringBuilder(); // TTS缓冲区
            int lastExtractedPosition = 0; // 上次提取到的位置（防止重复提取）

            // 创建 TTS CancellationToken
            _currentTTSCancellation = new CancellationTokenSource();

            // 启动流式
            _ = AIService.GetAIResponseStream(
                chatHistoryForAI,
                onDelta: delta =>
                {
                    if (string.IsNullOrEmpty(delta)) return;
                    sb.Append(delta);

                    string accumulated = sb.ToString();
                    string partialDialogue = TryExtractDialoguePartial(accumulated);

                    if (!string.IsNullOrEmpty(partialDialogue))
                    {
                        // 更新UI显示
                        float now = (float)_streamStopwatch.Elapsed.TotalSeconds;
                        if ((now - _lastStreamUiUpdateTime) >= StreamUiInterval)
                        {
                            _lastStreamUiUpdateTime = now;
                            _uiQueue.Enqueue(() =>
                            {
                                if (thinkingBubble != null)
                                {
                                    thinkingBubble.SetText(partialDialogue);
                                }
                            });
                        }

                        // 流式 TTS：检查是否有新的完整句子可以播放
                        // 只处理从 lastExtractedPosition 开始的新内容
                        if (partialDialogue.Length > lastExtractedPosition)
                        {
                            string newContent = partialDialogue.Substring(lastExtractedPosition);
                            ttsBuffer.Append(newContent);
                            lastExtractedPosition = partialDialogue.Length; // 更新已提取位置

                            // 检查是否包含句子结束标记（。！？\n等）
                            string bufferedText = ttsBuffer.ToString();
                            int sentenceEnd = FindSentenceEnd(bufferedText);

                            if (sentenceEnd > 0)
                            {
                                // 找到完整句子，立即播放
                                string sentenceToPlay = bufferedText.Substring(0, sentenceEnd + 1).Trim();
                                if (!string.IsNullOrEmpty(sentenceToPlay))
                                {
                                    _isTTSPlaying = true;

                                    // 清空已提取的部分
                                    ttsBuffer.Clear();
                                    if (sentenceEnd + 1 < bufferedText.Length)
                                    {
                                        ttsBuffer.Append(bufferedText.Substring(sentenceEnd + 1));
                                    }

                                    // 将 TTS 播放请求加入队列，由主线程 Update 处理
                                    UnityEngine.Debug.Log($"[DialogueManager] 分句TTS入队: {sentenceToPlay}");
                                    _ttsQueue.Enqueue((sentenceToPlay, null));
                                }
                            }
                        }
                    }
                },
                onCompleted: finalText => { tcs.TrySetResult(finalText); },
                onError: err =>
                {
                    UnityEngine.Debug.LogError($"[DialogueManager] 流式错误: {err}");
                    tcs.TrySetException(new Exception(err ?? "stream error"));
                },
                ct: cts.Token);

            string finalJson = null;
            try
            {
                finalJson = await tcs.Task; // 等待最终JSON
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DialogueManager] 流式失败: {ex.Message}");
                if (thinkingBubble != null) thinkingBubble.SetText("抱歉，我好像走神了...");
                StopCurrentTTS();
                isWaitingForAI = false;
                return;
            }

            // 解析AI返回的JSON
            AIResponseData responseData = ParseAIResponse(finalJson);
            if (responseData != null)
            {
                // 等待 TTS 队列清空
                int waitCount = 0;
                while ((_ttsQueue.Count > 0 || _isProcessingTTS) && waitCount < 200)
                {
                    await Task.Delay(50);
                    waitCount++;
                }

                // 播放剩余缓冲区中的文本（如果有）
                string remainingText = ttsBuffer.ToString().Trim();
                if (!string.IsNullOrEmpty(remainingText))
                {
                    UnityEngine.Debug.Log($"[DialogueManager] 播放剩余文本: {remainingText}");
                    try
                    {
                        if (audioManager != null && _currentTTSCancellation != null)
                        {
                            await audioManager.PlayTTS(remainingText, _currentTTSCancellation.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        UnityEngine.Debug.Log("[DialogueManager] 剩余TTS 被打断");
                    }
                }

                _isTTSPlaying = false;

                // 确保最终文本显示在UI上
                if (thinkingBubble != null)
                {
                    thinkingBubble.SetText(responseData.dialogue);
                }

                ExecuteCharacterAction(responseData);
                chatHistory.Add(new ChatMessage
                    { role = "assistant", parts = new List<ChatPart> { new ChatPart { text = finalJson } } });

                // 自动保存到数据库
                AutoSaveToDatabase();
            }
            else
            {
                if (thinkingBubble != null) thinkingBubble.SetText("抱歉，我好像走神了...");
            }

            isWaitingForAI = false;
            return;
        }

        // 非流式原流程
        string aiResponseJson = await AIService.GetAIResponse(chatHistoryForAI);
        AIResponseData responseData2 = ParseAIResponse(aiResponseJson);

        if (responseData2 != null)
        {
            _currentTTSCancellation = new CancellationTokenSource();
            var ttsCts = _currentTTSCancellation;
            _isTTSPlaying = true;
            try
            {
                if (audioManager != null)
                {
                    await audioManager.PlayTTS(responseData2.dialogue, ttsCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.Log("[DialogueManager] TTS 被用户打断");
            }
            finally
            {
                _isTTSPlaying = false;
            }

            if (thinkingBubble != null)
            {
                thinkingBubble.SetText(responseData2.dialogue);
            }

            ExecuteCharacterAction(responseData2);
            chatHistory.Add(new ChatMessage
                { role = "assistant", parts = new List<ChatPart> { new ChatPart { text = aiResponseJson } } });

            // 自动保存到数据库
            AutoSaveToDatabase();
        }
        else
        {
            if (thinkingBubble != null)
            {
                thinkingBubble.SetText("抱歉，我好像走神了...");
            }
        }

        isWaitingForAI = false;
    }

    /// <summary>
    /// 在主线程上播放 TTS（通过 Update 队列调度，避免线程问题）
    /// </summary>
    private async Task PlayTTSOnMainThread(string text, CancellationToken ct, Action onComplete)
    {
        try
        {
            // 确保在主线程上执行
            if (!UnityEngine.Application.isPlaying)
            {
                UnityEngine.Debug.LogWarning("[DialogueManager] 游戏未运行，跳过 TTS 播放");
                onComplete?.Invoke();
                return;
            }

            if (audioManager == null)
            {
                UnityEngine.Debug.LogWarning("[DialogueManager] audioManager 为空，跳过 TTS 播放");
                onComplete?.Invoke();
                return;
            }

            UnityEngine.Debug.Log(
                $"[DialogueManager] 开始播放 TTS: {text?.Substring(0, Math.Min(20, text?.Length ?? 0))}...");
            await audioManager.PlayTTS(text, ct);
            UnityEngine.Debug.Log($"[DialogueManager] TTS 播放完成");
        }
        catch (OperationCanceledException)
        {
            UnityEngine.Debug.Log("[DialogueManager] 分句TTS 被打断");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DialogueManager] TTS 错误: {ex.Message}\n堆栈: {ex.StackTrace}");
        }
        finally
        {
            onComplete?.Invoke();
        }
    }

    private string TryExtractDialoguePartial(string acc)
    {
        if (string.IsNullOrEmpty(acc)) return null;

        // 第一步: 尝试直接解析为合法的 JSON 对象
        try
        {
            var token = JToken.Parse(acc);
            var asObj = token as JObject;
            if (asObj != null && asObj["dialogue"] != null)
            {
                var rd = asObj.ToObject<AIResponseData>();
                if (!string.IsNullOrEmpty(rd?.dialogue)) return rd.dialogue;
            }
        }
        catch
        {
        }

        // 第二步: 使用字符串方式提取 "dialogue":"..." 段, 支持增量式的JSON
        // 查找 "dialogue":" 起始位置
        int dialogueKeyIndex = acc.IndexOf("\"dialogue\"");
        if (dialogueKeyIndex < 0) return null;

        // 查找 : " 开始提取 dialogue 值的起始位置
        int colonIndex = acc.IndexOf(':', dialogueKeyIndex);
        if (colonIndex < 0) return null;

        int valueStartIndex = acc.IndexOf('"', colonIndex);
        if (valueStartIndex < 0) return null;

        valueStartIndex++; // 移动到值的开始位置

        // 开始根据决策树提取 dialogue 值，支持转义字符
        var result = new StringBuilder();
        bool inEscape = false;

        for (int i = valueStartIndex; i < acc.Length; i++)
        {
            char c = acc[i];

            if (inEscape)
            {
                // 处理转义字符
                switch (c)
                {
                    case '"':
                        result.Append('"');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case '\\':
                        result.Append('\\');
                        break;
                    default:
                        result.Append(c);
                        break;
                }

                inEscape = false;
            }
            else if (c == '\\')
            {
                // 遇到反斜线，标记为转义状态
                inEscape = true;
            }
            else if (c == '"')
            {
                // 遇到双引号，表示 dialogue 值结束
                break;
            }
            else
            {
                // 普通字符直接添加
                result.Append(c);
            }
        }

        string extracted = result.ToString();
        return string.IsNullOrEmpty(extracted) ? null : extracted;
    }

    /// <summary>
    /// 初始化对话历史，包含系统指令
    /// </summary>
    private void InitializeChatHistory()
    {
        CharacterProfile profileToUse = null;

        // 优先使用 Inspector 中拖入的角色卡文件
        if (characterCardFile != null)
        {
            string defaultName = characterCardFile.name;
            profileToUse = CharacterCardParser.ParseFromText(characterCardFile.text, defaultName);
        }

        // 如果没有配置角色卡，从 Resources 加载默认的
        if (profileToUse == null)
        {
            var defaultFile = Resources.Load<TextAsset>("Characters/Kanari");
            if (defaultFile != null)
            {
                profileToUse = CharacterCardParser.ParseFromText(defaultFile.text, "Kanari");
            }
            else
            {
                profileToUse = ScriptableObject.CreateInstance<CharacterProfile>();
                profileToUse.characterName = "Kanari";
                profileToUse.userName = "User";
                profileToUse.persona = "你是一个名为\"Kanari\"的虚拟少女，性格活泼可爱，对世界充满好奇。";
                profileToUse.openingMessage = "你好呀！我是Kanari，很高兴见到你！";
            }
        }

        StartNewChat(profileToUse);
    }

    private CharacterProfile BuildProfileFromCard(TextAsset cardFile, string defaultName)
    {
        if (cardFile == null) return null;
        return CharacterCardParser.ParseFromText(cardFile.text, defaultName);
    }

    private void EnsureProfileDefaults(CharacterProfile profile)
    {
        if (profile == null) return;
        if (string.IsNullOrEmpty(profile.characterName)) profile.characterName = "Kanari";
        if (string.IsNullOrEmpty(profile.userName)) profile.userName = "User";
        if (string.IsNullOrEmpty(profile.persona))
        {
            profile.persona = "你是一个名为\"" + profile.characterName + "\"的虚拟少女，性格活泼可爱，对世界充满好奇。";
        }
        if (string.IsNullOrEmpty(profile.openingMessage))
        {
            profile.openingMessage = "你好呀！我是" + profile.characterName + "，很高兴见到你！";
        }
    }

    public void StartNewChat(CharacterProfile profileToUse)
    {
        if (profileToUse == null)
        {
            profileToUse = ScriptableObject.CreateInstance<CharacterProfile>();
            profileToUse.characterName = "Kanari";
        }

        EnsureProfileDefaults(profileToUse);

        string personaSafe = string.IsNullOrEmpty(profileToUse.persona) ? "" : profileToUse.persona;
        string openingSafe = string.IsNullOrEmpty(profileToUse.openingMessage) ? "" : profileToUse.openingMessage;
        string userSafe = string.IsNullOrEmpty(profileToUse.userName) ? "User" : profileToUse.userName;
        string charSafe = string.IsNullOrEmpty(profileToUse.characterName) ? "Kanari" : profileToUse.characterName;
        string rawCardSafe = string.IsNullOrEmpty(profileToUse.rawCardText) ? "" : profileToUse.rawCardText;

        _activeProfile = profileToUse;
        string charName = charSafe;
        string userName = userSafe;

        currentAffinity = 50;
        currentMood = 50;
        currentEnergy = 50;
        currentStress = 50;
        currentTrust = 50;
        OnStatsChanged?.Invoke(currentAffinity, currentMood, currentEnergy, currentStress, currentTrust);

        // 构建角色背景部分：优先使用原始完整角色卡文本，让 AI 自行理解；否则使用提取的 persona
        string characterBackground = !string.IsNullOrWhiteSpace(rawCardSafe) ? rawCardSafe : personaSafe;

        string systemPrompt = "你扮演一个名为\"" + charName + "\"的角色。以下是该角色的完整背景设定，请仔细阅读并理解角色的性格、经历和说话方式，在后续对话中严格按照角色设定来回复：\n\n" +
            characterBackground + "\n\n" +
            "你的回复必须是一个严格合法的JSON对象，包含以下数值更新字段（全部为变化量，非绝对值，增减幅度范围建议 -5 到 5）：\n" +
            "- dialogue: 你的对话内容\n" +
            "- emotion: 表情指令 (Default, Proud, Sad, Smile, Angry)\n" +
            "- action: 动作指令 (Hello, Thinking, Proud, Shy, 或留空)\n" +
            "- affinityChange: 你对用户好感度的变化。被夸奖友好时增加，反之减少。\n" +
            "- moodChange: 心情变化。遇到开心事增加，遇到坏事减少。\n" +
            "- energyChange: 精力消耗。长时间聊天会消耗精力(负数)，被鼓励或休息提示时恢复(正数)。\n" +
            "- stressChange: 压力变化。聊到负面话题、冲突、或者被指责时增加，得到安慰时减少。\n" +
            "- trustChange: 信任度变化。持续友好、真诚交流增加，被欺骗、不耐烦对待或冒犯降低。\n" +
            "示例: {\"dialogue\":\"很高兴认识你！\",\"emotion\":\"Smile\",\"action\":\"Hello\",\"affinityChange\":1,\"moodChange\":2,\"energyChange\":-1,\"stressChange\":0,\"trustChange\":1}\n" +
            "【绝对禁止】输出思考过程、分析过程、推理过程或任何<think>...</think>内容。\n" +
            "【绝对禁止】在dialogue字段使用颜文字或特殊表情符号。只能使用标准的文字和标点。";

/*
        string firstMessageJson = BuildFirstMessageJson(openingSafe);
*/

        string firstMessageJson = "{\"dialogue\":\"很高兴认识你！\",\"emotion\":\"Smile\",\"action\":\"Hello\",\"affinityChange\":0,\"moodChange\":0,\"energyChange\":0,\"stressChange\":0,\"trustChange\":0}";

        chatHistory = new List<ChatMessage>
        {
            new ChatMessage { role = "system", parts = new List<ChatPart> { new ChatPart { text = systemPrompt } } },
            new ChatMessage
                { role = "assistant", parts = new List<ChatPart> { new ChatPart { text = firstMessageJson } } }
        };

        ClearUI();
        // 开场白直接显示在屏幕上，不经过 AI
        AddMessageToUI(openingSafe, false);
        ExecuteCharacterAction(new AIResponseData { dialogue = openingSafe, emotion = "Smile", action = "Hello" });
    }

    private string BuildSystemPrompt(string charName, string persona)
    {
        return "你扮演一个名为\"" + charName + "\"的角色。\n" +
               persona + "\n\n" +
               "你的回复必须是一个严格合法的JSON对象，包含以下数值更新字段（全部为变化量，非绝对值，增减幅度范围建议 -5 到 5）：\n" +
               "- dialogue: 你的对话内容\n" +
               "- emotion: 表情指令 (Default, Proud, Sad, Smile, Angry)\n" +
               "- action: 动作指令 (Hello, Thinking, Proud, Shy, 或留空)\n" +
               "- affinityChange: 你对用户好感度的变化。被夸奖友好时增加，反之减少。\n" +
               "- moodChange: 心情变化。遇到开心事增加，遇到坏事减少。\n" +
               "- energyChange: 精力消耗。长时间聊天会消耗精力(负数)，被鼓励或休息提示时恢复(正数)。\n" +
               "- stressChange: 压力变化。聊到负面话题、冲突、或者被指责时增加，得到安慰时减少。\n" +
               "- trustChange: 信任度变化。持续友好、真诚交流增加，被欺骗、不耐烦对待或冒犯降低。\n" +
               "示例: {\"dialogue\":\"很高兴认识你！\",\"emotion\":\"Smile\",\"action\":\"Hello\",\"affinityChange\":1,\"moodChange\":2,\"energyChange\":-1,\"stressChange\":0,\"trustChange\":1}\n" +
               "【绝对禁止】输出思考过程、分析过程、推理过程或任何<think>...</think>内容。\n" +
               "【绝对禁止】在dialogue字段使用颜文字或特殊表情符号。只能使用标准的文字和标点。";
    }

    private string BuildFirstMessageJson(string dialogue)
    {
        string escapedDialogue = dialogue
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
        return
            $"{{\"dialogue\":\"{escapedDialogue}\",\"emotion\":\"Smile\",\"action\":\"Hello\",\"affinityChange\":0,\"moodChange\":0,\"energyChange\":0,\"stressChange\":0,\"trustChange\":0}}";
    }

    private MessageBubble AddMessageToUI(string message, bool isUserMessage)
    {
        if (chatContainer == null)
        {
            UnityEngine.Debug.LogError("[DialogueManager] chatContainer 为空，无法添加消息气泡！");
            return null;
        }

        GameObject messagePrefab = isUserMessage ? userMessagePrefab : aiMessagePrefab;
        if (messagePrefab == null)
        {
            string prefabName = isUserMessage ? "userMessagePrefab" : "aiMessagePrefab";
            UnityEngine.Debug.LogError("[DialogueManager] " + prefabName + " 为空，请在 Inspector 中配置！");
            return null;
        }

        GameObject rowContainer = new GameObject(isUserMessage ? "UserMessageRow" : "AIMessageRow");
        rowContainer.transform.SetParent(chatContainer, false);
        var rowRect = rowContainer.AddComponent<RectTransform>();
        var rowLayout = rowContainer.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        rowLayout.spacing = 0;
        rowLayout.padding = new RectOffset(0, 0, 0, 0);
        rowLayout.childAlignment = TextAnchor.UpperLeft;

        GameObject newBubbleObject = Instantiate(messagePrefab, rowContainer.transform);
        if (newBubbleObject == null)
        {
            Destroy(rowContainer);
            return null;
        }

        var bubbleLayoutElement = newBubbleObject.GetComponent<UnityEngine.UI.LayoutElement>();
        if (bubbleLayoutElement == null)
        {
            bubbleLayoutElement = newBubbleObject.AddComponent<UnityEngine.UI.LayoutElement>();
        }

        bubbleLayoutElement.flexibleWidth = 0;

        MessageBubble bubbleComponent = newBubbleObject.GetComponent<MessageBubble>();
        if (bubbleComponent != null)
        {
            bubbleComponent.SetText(message);
            if (bubbleComponent.textComponent != null)
            {
                bubbleComponent.textComponent.alignment = TextAlignmentOptions.TopLeft;
            }
        }

        RefreshLayoutAndScrollToBottom();
        return bubbleComponent;
    }

    private void RefreshLayoutAndScrollToBottom()
    {
        if (chatContainer != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(chatContainer as RectTransform);
        }

        if (chatScrollRect != null)
        {
            chatScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private string MaybeUnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        string trimmed = s.Trim();
        if (trimmed.Length > 1 && trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }

        bool looksEscaped = trimmed.Contains("\\\"") || trimmed.StartsWith("\\{") || trimmed.StartsWith("\\[");
        if (!looksEscaped) return trimmed;
        try
        {
            return trimmed.Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }
        catch
        {
            return trimmed;
        }
    }

    private AIResponseData ParseAIResponse(string json)
    {
        try
        {
            UnityEngine.Debug.Log($"[DialogueManager] 收到AI的原始回复: {json}");

            string TrimCodeFences(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                s = s.Trim();
                if (s.StartsWith("```"))
                {
                    int firstNewLine = s.IndexOf('\n');
                    if (firstNewLine >= 0) s = s.Substring(firstNewLine + 1);
                    if (s.EndsWith("```")) s = s.Substring(0, s.Length - 3);
                    s = s.Trim();
                }

                return s;
            }

            string candidate = json?.Trim();
            candidate = TrimCodeFences(candidate);
            candidate = MaybeUnescapeJson(candidate);

            AIResponseData TryParseDirect(JToken token)
            {
                if (token == null) return null;
                if (token.Type == JTokenType.Object)
                {
                    var obj = (JObject)token;
                    if (obj["dialogue"] != null) return obj.ToObject<AIResponseData>();
                }

                return null;
            }

            JToken rootToken = null;
            try
            {
                rootToken = JToken.Parse(candidate);
            }
            catch
            {
                string unescapedCandidate = MaybeUnescapeJson(candidate);
                try
                {
                    rootToken = JToken.Parse(unescapedCandidate);
                }
                catch
                {
                    int startIndex = candidate.IndexOf('{');
                    int endIndex = candidate.LastIndexOf('}');
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        string inner = candidate.Substring(startIndex, endIndex - startIndex + 1);
                        inner = MaybeUnescapeJson(inner);
                        rootToken = JToken.Parse(inner);
                    }
                }
            }

            var direct = TryParseDirect(rootToken);
            if (direct != null)
            {
                UnityEngine.Debug.Log(
                    $"[DialogueManager] 解析为AIResponseData: dialogue='{direct.dialogue}', emotion='{direct.emotion}', action='{direct.action}'");
                return direct;
            }

            string ExtractContentFromKnownWrappers(JToken token)
            {
                if (token == null) return null;
                var content = token["choices"]?[0]?["message"]?["content"]?.Value<string>()
                              ?? token["choices"]?[0]?["text"]?.Value<string>();
                if (!string.IsNullOrEmpty(content)) return content;
                var partText = token["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>();
                if (!string.IsNullOrEmpty(partText)) return partText;
                var qwen = token["output_text"]?.Value<string>()
                           ?? token["response"]?.Value<string>()
                           ?? token["data"]?["content"]?.Value<string>();
                if (!string.IsNullOrEmpty(qwen)) return qwen;
                return null;
            }

            string innerContent = ExtractContentFromKnownWrappers(rootToken);
            if (!string.IsNullOrEmpty(innerContent))
            {
                string inner = TrimCodeFences(innerContent).Trim();
                try
                {
                    var innerToken = JToken.Parse(inner);
                    var asDirect = TryParseDirect(innerToken);
                    if (asDirect != null) return asDirect;
                }
                catch
                {
                    string unescaped = inner.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r");
                    try
                    {
                        var innerToken2 = JToken.Parse(unescaped);
                        var asDirect2 = TryParseDirect(innerToken2);
                        if (asDirect2 != null) return asDirect2;
                    }
                    catch
                    {
                    }
                }

                return new AIResponseData { dialogue = inner, emotion = "Default", action = string.Empty };
            }

            if (rootToken != null && rootToken.Type == JTokenType.Object)
            {
                var obj = (JObject)rootToken;
                var data = new AIResponseData
                {
                    dialogue = obj["dialogue"]?.Value<string>(),
                    emotion = obj["emotion"]?.Value<string>(),
                    action = obj["action"]?.Value<string>(),
                    affinityChange = obj["affinityChange"]?.Value<int>() ?? 0,
                    moodChange = obj["moodChange"]?.Value<int>() ?? 0,
                    energyChange = obj["energyChange"]?.Value<int>() ?? 0,
                    stressChange = obj["stressChange"]?.Value<int>() ?? 0,
                    trustChange = obj["trustChange"]?.Value<int>() ?? 0
                };
                if (!string.IsNullOrEmpty(data.dialogue)) return data;
            }

            UnityEngine.Debug.LogError("[DialogueManager] 在AI的回复中未能找到可用的对话内容！");
            return null;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[DialogueManager] JSON解析出错: {ex.Message}");
            return null;
        }
    }

    private void ExecuteCharacterAction(AIResponseData responseData)
    {
        if (responseData != null)
        {
            currentAffinity = Mathf.Clamp(currentAffinity + responseData.affinityChange, 0, 100);
            currentMood = Mathf.Clamp(currentMood + responseData.moodChange, 0, 100);
            currentEnergy = Mathf.Clamp(currentEnergy + responseData.energyChange, 0, 100);
            currentStress = Mathf.Clamp(currentStress + responseData.stressChange, 0, 100);
            currentTrust = Mathf.Clamp(currentTrust + responseData.trustChange, 0, 100);
            OnStatsChanged?.Invoke(currentAffinity, currentMood, currentEnergy, currentStress, currentTrust);
        }

        if (live2DController == null) return;
        if (System.Enum.TryParse(responseData?.emotion ?? "Default", true,
                out Live2DController.Expression newExpression))
        {
            live2DController.SetExpression(newExpression);
        }

        if (!string.IsNullOrEmpty(responseData?.action))
        {
            live2DController.PlayActionTrigger(responseData.action);
        }
    }

    private int FindSentenceEnd(string text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        char[] chineseEnds = { '。', '！', '？', '；', '\n' };
        char[] englishEnds = { '.', '!', '?' };
        int lastPos = -1;
        foreach (char c in chineseEnds)
        {
            int pos = text.IndexOf(c);
            if (pos >= 0 && (lastPos < 0 || pos < lastPos)) lastPos = pos;
        }

        if (lastPos < 0)
        {
            foreach (char c in englishEnds)
            {
                int pos = text.IndexOf(c);
                while (pos >= 0)
                {
                    if (pos == text.Length - 1 || text[pos + 1] == ' ' || text[pos + 1] == '\n' ||
                        text[pos + 1] == '\r')
                    {
                        if (lastPos < 0 || pos < lastPos) lastPos = pos;
                        break;
                    }

                    pos = text.IndexOf(c, pos + 1);
                }
            }
        }

        if (lastPos < 0 && text.Length > 30)
        {
            int commaPos = text.IndexOf('，');
            if (commaPos < 0) commaPos = text.IndexOf(',');
            if (commaPos >= 15) return commaPos;
        }

        return lastPos;
    }

    private void AutoSaveToDatabase()
    {
        if (string.IsNullOrEmpty(_currentUsername) || DatabaseManager.Instance == null || chatHistory == null)
            return;
        string charName = _activeProfile != null ? _activeProfile.characterName : "Unknown";
        DatabaseManager.Instance.SaveChatHistory(_currentUsername, charName, chatHistory, currentAffinity, currentMood,
            currentEnergy, currentStress, currentTrust);
    }
}
