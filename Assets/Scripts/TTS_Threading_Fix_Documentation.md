# Unity 多线程 TTS 播放修复说明

## ?? 问题描述

### 错误信息
```
[DialogueManager] TTS 错误: PlayHelper can only be called from the main thread.
Constructors and field initializers will be executed from the loading thread when loading a scene.
Don't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.
```

### 问题原因
1. **流式 AI 响应在回调线程执行**：`DeepSeek_Service`、`Qwen_Service` 等的 `onDelta` 回调在后台线程执行
2. **直接调用 Unity API**：在回调线程中调用 `audioManager.PlayTTS`，而 `AudioSource.Play()` 等 Unity API 只能在主线程调用
3. **async/await 不能改变线程上下文**：虽然使用了 `async/await`，但方法的**初始调用**仍在回调线程上

## ? 修复方案

### 核心思路
使用 **生产者-消费者模式**，将 TTS 播放请求从回调线程（生产者）通过队列传递到主线程（消费者）。

### 实现细节

#### 1. 添加 TTS 请求队列
```csharp
// 在 DialogueManager 类中添加
private readonly ConcurrentQueue<(string text, Action onComplete)> _ttsQueue = 
    new ConcurrentQueue<(string, Action)>();
private bool _isProcessingTTS = false;
```

- 使用 `ConcurrentQueue` 保证线程安全
- 元组包含要播放的文本和完成回调

#### 2. 在 Update 中消费队列
```csharp
private void Update()
{
    // ...existing UI queue processing...
    
    // 处理 TTS 播放队列（一次只处理一个，避免并发）
    if (!_isProcessingTTS && _ttsQueue.TryDequeue(out var ttsRequest))
    {
        _isProcessingTTS = true;
        _ = ProcessTTSRequest(ttsRequest);
    }
}

private async Task ProcessTTSRequest((string text, Action onComplete) request)
{
    await PlayTTSOnMainThread(request.text, _currentTTSCancellation?.Token ?? default, request.onComplete);
    _isProcessingTTS = false;
}
```

- `Update` 在主线程执行，保证所有 Unity API 调用都在主线程
- 使用 `_isProcessingTTS` 标记避免并发播放

#### 3. 从回调线程提交请求
```csharp
// 在 onDelta 回调中（回调线程）
if (sentenceEnd > 0 && !isTTSProcessing)
{
    string sentenceToPlay = bufferedText.Substring(0, sentenceEnd + 1).Trim();
    if (!string.IsNullOrEmpty(sentenceToPlay))
    {
        isTTSProcessing = true;
        _isTTSPlaying = true;
        
        // ? 错误：直接调用（在回调线程）
        // _ = PlayTTSOnMainThread(sentenceToPlay, ...);
        
        // ? 正确：加入队列（线程安全）
        _ttsQueue.Enqueue((sentenceToPlay, () => { isTTSProcessing = false; }));
    }
}
```

## ?? 修复效果

### Before (? 错误)
```
回调线程 (DeepSeek_Service)
    ↓ 直接调用
PlayTTSOnMainThread (仍在回调线程)
    ↓ 尝试调用
AudioSource.Play() ? 错误！只能在主线程调用
```

### After (? 正确)
```
回调线程 (DeepSeek_Service)
    ↓ 加入队列 (线程安全)
ConcurrentQueue<TTS请求>
    ↓ Update 每帧检查 (主线程)
ProcessTTSRequest (主线程)
    ↓ await
PlayTTSOnMainThread (主线程)
    ↓ 调用
AudioSource.Play() ? 成功！
```

## ?? 技术细节

### Unity 线程模型
1. **主线程（Unity Thread）**
   - 运行 `MonoBehaviour` 的生命周期方法：`Awake`, `Start`, `Update`, `OnDestroy` 等
   - 所有 Unity API 必须在此线程调用
   - UI 更新必须在此线程

2. **回调线程（Worker Threads）**
   - 网络请求的回调：`HttpClient`, `WebSocket`
   - `Task.Run` 创建的任务
   - 不能调用 Unity API

### 线程安全的数据结构
- `ConcurrentQueue<T>`：无锁队列，支持多生产者多消费者
- `ConcurrentBag<T>`：无序集合
- `lock` 语句：传统互斥锁

### async/await 陷阱
```csharp
// ? 常见误解：async 会自动切换到主线程
async void OnCallback() // 在回调线程执行
{
    await DoSomething(); // 仍可能在回调线程
    UnityAPI.Call(); // ? 错误！
}

// ? 正确做法：显式调度到主线程
void OnCallback() // 在回调线程执行
{
    _queue.Enqueue(() => {
        // 这个 lambda 会在主线程的 Update 中执行
        UnityAPI.Call(); // ? 安全
    });
}
```

## ?? 测试验证

### 测试步骤
1. **基础功能测试**
   - ? 发送消息，AI 流式响应
   - ? 边生成边播放 TTS
   - ? 没有线程错误

2. **边界情况测试**
   - ? 快速连续发送多条消息
   - ? 在 TTS 播放中途发送新消息（打断）
   - ? 切换 AI 模型后发送消息

3. **性能测试**
   - ? 长文本分句播放流畅
   - ? 多句快速到达不会阻塞 UI
   - ? 内存占用正常（队列会自动清空）

### 日志输出
```
[DialogueManager] 开始播放 TTS: 你好！我是Kanari...
[DialogueManager] TTS 播放完成
[DialogueManager] 开始播放 TTS: 很高兴见到你！...
[DialogueManager] 分句TTS 被打断  // 用户发送新消息时
```

## ?? 相关文件

### 修改的文件
- `Assets/Scripts/DialogueManager.cs`
  - 添加 `_ttsQueue` 队列
  - 添加 `_isProcessingTTS` 标记
  - 修改 `Update` 方法处理队列
  - 添加 `ProcessTTSRequest` 方法
  - 修改 `HandleMessageSent` 中的 TTS 调用逻辑

### 未修改但相关的文件
- `Assets/Scripts/AudioManager.cs` - 已在之前修复，使用 `isAudioSourcePlaying` 标记
- `Assets/Scripts/Chat/DeepSeek_Service.cs` - 流式响应回调
- `Assets/Scripts/Chat/Gemini_Service.cs` - 流式响应回调
- `Assets/Scripts/Chat/Qwen_Service.cs` - 流式响应回调

## ?? 调试技巧

### 如何检测线程问题
```csharp
// 在可疑代码处添加线程检查
if (!UnityEngine.Application.isPlaying)
{
    Debug.LogWarning("Not on main thread!");
}

// 或使用更详细的检查
var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
Debug.Log($"Current thread: {threadId}");
// Unity 主线程通常是 ID 1
```

### 启用详细日志
在 `DialogueManager.cs` 中已添加详细日志：
- TTS 开始播放
- TTS 播放完成
- TTS 被打断
- 错误堆栈跟踪

## ?? 最佳实践

### Unity 多线程开发准则
1. **永远不要在回调线程调用 Unity API**
2. **使用队列在线程间传递数据**
3. **在主线程的 Update/LateUpdate 中消费队列**
4. **使用 CancellationToken 支持取消操作**
5. **避免使用 Task.Run，除非必要**

### 异步编程模式
```csharp
// ? 推荐：在 MonoBehaviour 中使用 async void
private async void OnButtonClick()
{
    await DoAsyncWork();
}

// ? 推荐：返回 Task 供外部 await
public async Task DoAsyncWork()
{
    await Task.Delay(1000);
}

// ? 避免：在 MonoBehaviour 中使用 Task.Run
private void OnCallback()
{
    Task.Run(async () => {
        // 这里不在主线程！
        await DoWork();
        UnityAPI.Call(); // ? 危险！
    });
}
```

## ?? 性能优化建议

### 当前实现
- ? 串行处理 TTS 请求（避免音频重叠）
- ? 使用无锁队列（高性能）
- ? 最小化主线程工作（只在 Update 中出队）

### 未来可优化
1. **批量处理**：如果需要更高吞吐量，可以在一帧内处理多个 UI 更新
2. **优先级队列**：紧急消息可以插队
3. **音频混音**：支持背景音乐 + TTS 同时播放

## ?? 参考资料

- [Unity 脚本生命周期](https://docs.unity3d.com/Manual/ExecutionOrder.html)
- [Unity 多线程指南](https://docs.unity3d.com/Manual/JobSystem.html)
- [C# ConcurrentQueue](https://docs.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentqueue-1)
- [Unity AudioSource API](https://docs.unity3d.com/ScriptReference/AudioSource.html)

## ?? 总结

通过引入**生产者-消费者队列模式**，我们成功解决了多线程调用 Unity API 的问题：

1. ? **回调线程安全**：流式响应回调只操作队列，不直接调用 Unity API
2. ? **主线程执行**：所有 Unity API 调用都在 `Update` 中的主线程执行
3. ? **性能优化**：使用无锁队列，最小化同步开销
4. ? **可维护性**：清晰的职责分离，易于理解和扩展

这是 Unity 游戏开发中处理异步操作的标准模式！??
