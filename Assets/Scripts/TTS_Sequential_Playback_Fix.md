# TTS 分句播放顺序问题修复

## ?? 问题描述

### 用户反馈
> "你确实是分句入队，但是前面一句话没说完，后面一句话一入队就直接说后面的话了"

### 具体表现
```
预期行为：
AI生成: "你好！" → TTS播放: "你好！" ?
AI生成: "我是Kanari。" → TTS播放: "我是Kanari。" ?

实际行为：
AI生成: "你好！" → TTS开始播放: "你好..."
AI生成: "我是Kanari。" → TTS立即播放: "我是Kanari。" ?
结果：第一句被打断，只听到 "你...我是Kanari。"
```

## ?? 问题分析

### 执行时序图

#### 问题场景（修复前）
```
时间线：
T0: DialogueManager 收到 "你好！"
    ├─ 入队到 _ttsQueue
    └─ _isProcessingTTS = false

T1: Update() 检测到队列有数据
    ├─ _isProcessingTTS = true
    ├─ 调用 ProcessTTSRequest()
    │   ├─ 调用 PlayTTSOnMainThread()
    │   │   └─ 调用 audioManager.PlayTTS("你好！")
    │   │       ├─ 清空 audioQueue ? 问题1：立即清空
    │   │       ├─ 调用 provider.StreamTTS() (网络请求)
    │   │       └─ await 等待网络响应...
    │   └─ _isProcessingTTS = false ? 问题2：网络请求完成就返回
    └─ ? 关键：此时音频数据刚入队，还没开始播放！

T2: DialogueManager 收到 "我是Kanari。"
    └─ 入队到 _ttsQueue

T3: Update() 又检测到队列有数据
    ├─ _isProcessingTTS = false ? (因为上一个已经返回)
    ├─ 调用 ProcessTTSRequest()
    │   └─ 调用 PlayTTS("我是Kanari。")
    │       ├─ 清空 audioQueue ??? 致命问题！
    │       │   └─ "你好！" 的音频数据被清空！
    │       └─ 开始请求 "我是Kanari。" 的音频
    └─ 结果：只听到第二句

音频队列状态：
T1: [你好的PCM数据...] → 正在播放 "你..."
T3: [] → 被清空！→ [我是Kanari的PCM数据] → 播放 "我是Kanari"
```

### 根本原因

#### 1. **时序不匹配**
```csharp
// AudioManager.PlayTTS() 的生命周期
async Task PlayTTS(string text) 
{
    audioQueue.Clear();  // ? Step 1: 立即清空队列
    
    await StreamTTS(...); // Step 2: 网络请求 (1-3秒)
                          // 音频数据通过回调陆续加入 audioQueue
    
    // ? Step 3: 方法返回
    // ? 但此时音频才刚刚开始播放！
}

// DialogueManager.ProcessTTSRequest()
async Task ProcessTTSRequest()
{
    await PlayTTS(text);  // 等待网络请求完成
    _isProcessingTTS = false; // ? 立即标记为"未处理"
    // ? 但音频还在队列中等待播放！
}
```

#### 2. **队列管理问题**
```csharp
// 问题：每次 PlayTTS 都清空队列
audioQueue.Clear(); // ? 不管前一句是否播放完

// 结果：
// 句子1: "你好！" → 入队 100,000 样本
// 句子2: "我是Kanari" → Clear() → 句子1 的 99,000 样本被删除！
```

## ? 修复方案

### 核心思路
**等待前序音频播放完毕，再开始下一句**

### 修复策略

#### 1. **AudioManager: 等待队列清空**
```csharp
public async Task PlayTTS(string text, CancellationToken ct)
{
    // ? 修复1: 等待当前音频队列播放完毕
    int minQueueSize = SampleRate / 2; // 0.5秒的缓冲
    while (audioQueue.Count > minQueueSize && waitCount < 200)
    {
        await Task.Delay(50);
        Debug.Log($"等待前序音频播放... 队列剩余: {audioQueue.Count} 样本");
    }
    
    // ? 修复2: 不要立即清空，让前一句播放完
    // audioQueue.Clear(); // ? 删除这行
    
    // 开始新的 TTS 请求
    int queueSizeBefore = audioQueue.Count;
    await provider.StreamTTS(text, options, OnPcmChunk, ct);
    int queueSizeAfter = audioQueue.Count;
    
    // ? 修复3: 等待新音频开始播放（至少10%）
    int newDataSize = queueSizeAfter - queueSizeBefore;
    int targetQueueSize = queueSizeAfter - (newDataSize / 10);
    while (audioQueue.Count > targetQueueSize)
    {
        await Task.Delay(50);
    }
    
    // ? 此时音频已经开始播放，安全返回
}
```

#### 2. **DialogueManager: 添加延迟**
```csharp
private async Task ProcessTTSRequest((string text, Action onComplete) request)
{
    try
    {
        await PlayTTSOnMainThread(request.text, ...);
        
        // ? 额外保险：给一点时间让音频稳定播放
        await Task.Delay(100);
    }
    finally
    {
        _isProcessingTTS = false;
    }
}
```

### 修复后的时序

```
时间线：
T0: 收到 "你好！"
    └─ 入队到 _ttsQueue

T1: Update() 处理队列
    ├─ _isProcessingTTS = true
    ├─ PlayTTS("你好！")
    │   ├─ 等待前序队列... (队列为空，立即继续)
    │   ├─ StreamTTS() → 网络请求
    │   ├─ 音频数据入队: [PCM数据 100,000 样本]
    │   ├─ 等待 10% 播放完成... ? 新增
    │   │   └─ 队列: 100,000 → 90,000 样本
    │   └─ 返回 ?
    ├─ Task.Delay(100) ? 新增
    └─ _isProcessingTTS = false

T2: 音频正在播放 "你好！"
    └─ 队列: 90,000 → 80,000 → ... 样本

T3: 收到 "我是Kanari。"
    └─ 入队到 _ttsQueue

T4: Update() 处理队列
    ├─ _isProcessingTTS = true
    ├─ PlayTTS("我是Kanari。")
    │   ├─ 等待前序队列... ? 关键！
    │   │   └─ 队列: 70,000 → 60,000 → ... → 8,000 (< 0.5秒)
    │   │   └─ 等待完成！"你好！" 播放结束
    │   ├─ StreamTTS() → 网络请求
    │   ├─ 音频数据入队: [PCM数据 150,000 样本]
    │   ├─ 等待 10% 播放完成...
    │   └─ 返回
    └─ _isProcessingTTS = false

T5: 音频继续播放 "我是Kanari。"
    └─ 队列: 135,000 → 120,000 → ...

? 结果：听到完整的 "你好！我是Kanari。"
```

## ?? 技术细节

### 音频队列大小计算

```csharp
const int SampleRate = 16000;  // 16kHz 采样率
const int Channels = 1;         // 单声道

// 1 秒音频 = 16000 样本
// 0.5 秒音频 = 8000 样本
int minQueueSize = SampleRate / 2; // 8000 样本

// 典型的一句话：
// "你好！" ≈ 1 秒 ≈ 16,000 样本
// "我是Kanari。" ≈ 2 秒 ≈ 32,000 样本
```

### 等待策略

```csharp
// 策略1: 等待前序音频播放完（留 0.5 秒缓冲）
while (audioQueue.Count > minQueueSize)
{
    await Task.Delay(50);
}

// 策略2: 等待新音频开始播放（10% 已消费）
int newDataSize = queueSizeAfter - queueSizeBefore;
int targetQueueSize = queueSizeAfter - (newDataSize / 10);
while (audioQueue.Count > targetQueueSize)
{
    await Task.Delay(50);
}

// 策略3: DialogueManager 额外保险延迟
await Task.Delay(100);
```

### 为什么需要三层等待？

1. **AudioManager 前等待**：确保前一句播放完
2. **AudioManager 后等待**：确保新音频开始播放
3. **DialogueManager 延迟**：额外保险，防止极端情况

## ?? 测试验证

### 日志示例

#### 正常流式播放
```
[DialogueManager] 分句TTS入队: 你好！
[DialogueManager] Update: 开始处理TTS队列，队列剩余: 0
[AudioManager] 开始请求 TTS: 你好！...
[AudioManager] TTS 请求完成，新增音频: 16000 样本 (总队列: 16000)
[AudioManager] 音频开始播放，队列剩余: 14400 样本
[DialogueManager] TTS 播放完成

[DialogueManager] 分句TTS入队: 我是Kanari。
[DialogueManager] Update: 开始处理TTS队列，队列剩余: 0
[AudioManager] 等待前序音频播放... 队列剩余: 12000 样本 ?
[AudioManager] 等待前序音频播放... 队列剩余: 9000 样本 ?
[AudioManager] 等待前序音频播放... 队列剩余: 6000 样本 ? (< 8000，继续)
[AudioManager] 开始请求 TTS: 我是Kanari。...
[AudioManager] TTS 请求完成，新增音频: 32000 样本 (总队列: 38000)
[AudioManager] 音频开始播放，队列剩余: 34800 样本
[DialogueManager] TTS 播放完成
```

### 验证要点

**? 应该看到：**
1. 每句话按顺序播放
2. "等待前序音频播放" 日志（如果有前序音频）
3. 队列大小逐渐减少
4. 听到完整的连续对话

**? 不应该看到：**
1. 队列突然从大数字变成 0（被清空）
2. 前一句被打断
3. 音频跳跃或重叠

## ?? 优化建议

### 未来可改进的地方

#### 1. **更精确的播放状态追踪**
```csharp
public class AudioManager
{
    private int _currentPlayingId = 0;
    
    public async Task<int> PlayTTS(string text)
    {
        int playId = ++_currentPlayingId;
        // ... 播放逻辑
        return playId;
    }
    
    public bool IsPlaying(int playId)
    {
        // 检查特定 ID 的音频是否还在播放
    }
}
```

#### 2. **音频完成回调**
```csharp
public async Task PlayTTS(string text, Action onComplete = null)
{
    await StreamTTS(...);
    
    // 监控队列，等待播放完成
    while (audioQueue.Count > 0)
    {
        await Task.Delay(50);
    }
    
    onComplete?.Invoke(); // 真正播放完成
}
```

#### 3. **优先级队列**
```csharp
// 紧急消息可以打断当前播放
public async Task PlayTTS(string text, int priority = 0)
{
    if (priority > currentPriority)
    {
        // 清空队列，立即播放
    }
    else
    {
        // 等待当前播放完成
    }
}
```

## ?? 总结

### 问题本质
**异步操作的完成时机 ≠ 音频播放完成时机**

### 解决方案
**在多个层次添加等待机制，确保音频按顺序播放**

### 关键改进
1. ? 等待前序音频队列
2. ? 不立即清空队列
3. ? 等待新音频开始播放
4. ? 额外的安全延迟

### 效果
- ? 分句按顺序完整播放
- ? 不会打断前一句
- ? 流畅的对话体验
- ? 支持流式生成 + 流式播放

现在你的虚拟少女 Kanari 应该能流畅地说出完整句子了！???
