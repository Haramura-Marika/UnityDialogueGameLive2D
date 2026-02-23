# TTS 重复播放问题修复说明

## ?? 问题描述

### 症状
- TTS 会重复播放同一句话
- API 被重复调用
- 无法说出完整的句子
- 句子会被切断后反复播放前半部分

### 用户观察到的现象
```
AI: "你好！我是..."
TTS: "你好！" ?
TTS: "你好！" ? 重复
TTS: "你好！" ? 又重复
AI: "...Kanari"
TTS: "我是Kanari" ?
TTS: "我是Kanari" ? 重复
```

## ?? 问题根源分析

### 1. **位置跟踪混乱**

#### 问题代码
```csharp
int lastTTSPosition = 0; // 上次播放到的位置

// 在 onDelta 回调中
if (partialDialogue.Length > lastTTSPosition)
{
    string newContent = partialDialogue.Substring(lastTTSPosition);
    ttsBuffer.Append(newContent);
    
    if (sentenceEnd > 0 && !isTTSProcessing)
    {
        // 找到句子，立即更新位置
        lastTTSPosition += sentenceEnd + 1;  // ? 问题在这里！
        
        // 加入队列
        _ttsQueue.Enqueue((sentenceToPlay, () => { isTTSProcessing = false; }));
    }
}
```

#### 问题分析
1. **`lastTTSPosition` 被立即更新**，但句子还没播放
2. **下一次 `onDelta` 回调到来时**：
   - 检测到 `partialDialogue.Length > lastTTSPosition`
   - 但实际上这部分内容已经在队列中了
   - 导致提取出相同的内容
3. **`isTTSProcessing` 标记不可靠**：
   - 在队列中等待的请求不会设置这个标记
   - 只有正在播放的请求才设置
   - 导致重复检测同一句子

### 2. **缓冲区管理问题**

```csharp
// 第一次 onDelta: "你好！我是"
partialDialogue = "你好！"  // 提取出
lastTTSPosition = 0
newContent = "你好！"       // 从位置 0 开始
ttsBuffer = "你好！"        // 加入缓冲
sentenceEnd = 2            // 找到 "！"
lastTTSPosition = 3        // 更新为 3 ?
_ttsQueue.Enqueue("你好！") // 入队 ?

// 第二次 onDelta: "你好！我是Kanari"
partialDialogue = "你好！我是"
lastTTSPosition = 3
newContent = "我是"         // 从位置 3 开始 ?
ttsBuffer = "我是"          // 正确！

// 但如果流式响应慢，第二次 onDelta 又来了相同内容：
partialDialogue = "你好！"  // 还是这个（网络延迟/重传）
lastTTSPosition = 3
newContent = ""            // length(3) <= lastTTSPosition(3)，不提取 ?

// 问题：但如果 lastTTSPosition 更新时机不对...
```

### 3. **实际的 Bug 场景**

```csharp
// 时间线：
T0: onDelta("你好！")
    -> partialDialogue = "你好！"
    -> lastTTSPosition = 0
    -> 提取 "你好！" 
    -> lastTTSPosition 立即变成 3 ? 这是问题！
    -> 入队

T1: onDelta("你好！我")  // 网络慢，又收到部分重复数据
    -> partialDialogue = "你好！我"
    -> lastTTSPosition = 3  // 但这是基于之前提取的
    -> 检查：length(4) > lastTTSPosition(3) ?
    -> 提取新内容：substring(3) = "我"
    -> ttsBuffer.Append("我")  // 缓冲区现在是 "我"
    -> 找不到句子结束，不入队 ? 正确

// 实际问题出在这里：
T0: onDelta("你好！")
    -> 提取 "你好！"，入队
    -> isTTSProcessing = true
    -> lastTTSPosition += 3  // 变成 3

T0.5: onDelta("你好！") // 快速又来一次（流式响应可能重复）
    -> lastTTSPosition = 3
    -> BUT: 这个 lastTTSPosition 是基于 partialDialogue 的
    -> 如果 ttsBuffer 没清空？

// 真正的问题：
// lastTTSPosition 跟踪的是 partialDialogue 的位置
// 但 ttsBuffer 是独立的缓冲区
// 两者之间的同步出现了问题！
```

## ? 修复方案

### 核心思路
**分离"提取位置"和"播放位置"的概念**

- `lastExtractedPosition`：已经从 `partialDialogue` 中提取到缓冲区的位置
- `ttsBuffer`：等待播放的文本缓冲区

### 修复后的代码

```csharp
int lastExtractedPosition = 0; // 上次提取到的位置（防止重复提取）
StringBuilder ttsBuffer = new StringBuilder(); // TTS缓冲区

onDelta: delta =>
{
    string partialDialogue = TryExtractDialoguePartial(accumulated);
    
    // 只处理从 lastExtractedPosition 开始的新内容
    if (partialDialogue.Length > lastExtractedPosition)
    {
        // 1. 提取新内容
        string newContent = partialDialogue.Substring(lastExtractedPosition);
        ttsBuffer.Append(newContent);
        lastExtractedPosition = partialDialogue.Length; // ? 立即更新提取位置
        
        // 2. 检查是否有完整句子
        string bufferedText = ttsBuffer.ToString();
        int sentenceEnd = FindSentenceEnd(bufferedText);
        
        if (sentenceEnd > 0)
        {
            // 3. 提取句子并入队
            string sentenceToPlay = bufferedText.Substring(0, sentenceEnd + 1).Trim();
            
            // 4. 清空已提取的部分
            ttsBuffer.Clear();
            if (sentenceEnd + 1 < bufferedText.Length)
            {
                ttsBuffer.Append(bufferedText.Substring(sentenceEnd + 1));
            }
            
            // 5. 入队（不需要回调，简化逻辑）
            _ttsQueue.Enqueue((sentenceToPlay, null));
        }
    }
}
```

### 关键改进

#### 1. **单一职责原则**
```csharp
// ? 之前：一个变量多个职责
int lastTTSPosition;  // 既跟踪提取位置，又跟踪播放位置

// ? 现在：职责分离
int lastExtractedPosition;  // 只负责跟踪提取位置
StringBuilder ttsBuffer;     // 只负责缓冲待播放内容
ConcurrentQueue _ttsQueue;   // 只负责管理播放队列
```

#### 2. **移除 isTTSProcessing 标记**
```csharp
// ? 之前：复杂的状态管理
bool isTTSProcessing = false;

if (sentenceEnd > 0 && !isTTSProcessing)  // 条件复杂
{
    isTTSProcessing = true;
    _ttsQueue.Enqueue((text, () => { isTTSProcessing = false; }));
}

// ? 现在：简化逻辑
if (sentenceEnd > 0)  // 简单直接
{
    _ttsQueue.Enqueue((text, null));
}
// 队列本身就是状态，不需要额外标记
```

#### 3. **等待队列清空**
```csharp
// ? 之前：等待单个标记
while (isTTSProcessing && waitCount < 100) { await Task.Delay(50); }

// ? 现在：等待队列清空
while ((_ttsQueue.Count > 0 || _isProcessingTTS) && waitCount < 200)
{
    await Task.Delay(50);
    waitCount++;
}
```

## ?? 执行流程对比

### Before (? 有 Bug)

```
Time 0: onDelta("你好！")
  ├─ lastTTSPosition = 0
  ├─ 提取 "你好！"
  ├─ lastTTSPosition = 3  ? 立即更新
  ├─ isTTSProcessing = true
  └─ 入队 "你好！"

Time 1: onDelta("你好！我") // 部分重复
  ├─ lastTTSPosition = 3
  ├─ 提取 substring(3) = "我"
  ├─ ttsBuffer = "我"  ? 这里是对的
  └─ 没找到句子结束 ?

Time 2: TTS 播放完成
  └─ isTTSProcessing = false

Time 3: onDelta("你好！")  // 又来了（网络问题）
  ├─ lastTTSPosition = 3  ? 但这是旧的状态！
  ├─ partialDialogue.Length(3) <= lastTTSPosition(3)
  ├─ 条件不满足，理论上不提取
  
// 实际 Bug 可能在这：
Time 3: onDelta 但 partialDialogue 解析失败，返回旧内容
  或者 lastTTSPosition 的更新时机不对
  或者 ttsBuffer 没清空导致重复
```

### After (? 修复)

```
Time 0: onDelta("你好！")
  ├─ lastExtractedPosition = 0
  ├─ 提取 newContent = "你好！"
  ├─ ttsBuffer.Append("你好！")
  ├─ lastExtractedPosition = 3  ? 更新提取位置
  ├─ 找到句子 "你好！"
  ├─ ttsBuffer.Clear()  ? 清空
  └─ 入队 "你好！"

Time 1: onDelta("你好！我")
  ├─ lastExtractedPosition = 3
  ├─ length(4) > 3 ?
  ├─ 提取 newContent = substring(3) = "我"
  ├─ ttsBuffer.Append("我")
  ├─ lastExtractedPosition = 4  ? 更新
  ├─ ttsBuffer = "我"
  └─ 没找到句子结束 ?

Time 2: onDelta("你好！") // 重复数据
  ├─ lastExtractedPosition = 4
  ├─ length(3) <= 4  ? 条件不满足
  └─ 跳过，不处理 ? 防止重复！

Time 3: onDelta("你好！我是Kanari。")
  ├─ lastExtractedPosition = 4
  ├─ length(9) > 4 ?
  ├─ 提取 newContent = "是Kanari。"
  ├─ ttsBuffer.Append("是Kanari。")
  ├─ ttsBuffer = "我是Kanari。"
  ├─ 找到句子 "我是Kanari。"
  ├─ ttsBuffer.Clear()
  └─ 入队 "我是Kanari。"
```

## ?? 其他改进

### 1. **清空队列**
```csharp
private void StopCurrentTTS()
{
    // 取消当前播放
    _currentTTSCancellation?.Cancel();
    
    // ? 清空队列
    while (_ttsQueue.TryDequeue(out _)) { }
    _isProcessingTTS = false;
}
```

### 2. **增强日志**
```csharp
// 入队时
UnityEngine.Debug.Log($"[DialogueManager] 分句TTS入队: {sentenceToPlay}");

// Update 处理时
UnityEngine.Debug.Log($"[DialogueManager] Update: 开始处理TTS队列，队列剩余: {_ttsQueue.Count}");

// 播放时
UnityEngine.Debug.Log($"[DialogueManager] 开始播放 TTS: {text?.Substring(0, Math.Min(20, text?.Length ?? 0))}...");
UnityEngine.Debug.Log($"[DialogueManager] TTS 播放完成");
```

## ?? 测试验证

### 测试场景

#### 1. **正常流式响应**
```
输入: "你好"
期望输出:
  [DialogueManager] 分句TTS入队: 你好！
  [DialogueManager] Update: 开始处理TTS队列，队列剩余: 0
  [DialogueManager] 开始播放 TTS: 你好！
  [DialogueManager] TTS 播放完成
  
  [DialogueManager] 分句TTS入队: 我是Kanari。
  [DialogueManager] Update: 开始处理TTS队列，队列剩余: 0
  [DialogueManager] 开始播放 TTS: 我是Kanari。
  [DialogueManager] TTS 播放完成
```

#### 2. **快速流式响应（多句同时到达）**
```
期望输出:
  [DialogueManager] 分句TTS入队: 你好！
  [DialogueManager] 分句TTS入队: 我是Kanari。
  [DialogueManager] 分句TTS入队: 很高兴见到你！
  [DialogueManager] Update: 开始处理TTS队列，队列剩余: 2
  [DialogueManager] 开始播放 TTS: 你好！
  [DialogueManager] TTS 播放完成
  [DialogueManager] Update: 开始处理TTS队列，队列剩余: 1
  [DialogueManager] 开始播放 TTS: 我是Kanari。
  ...
```

#### 3. **用户打断**
```
期望输出:
  [DialogueManager] 分句TTS入队: 你好！
  [DialogueManager] 开始播放 TTS: 你好！
  [用户发送新消息]
  [DialogueManager] 停止当前 TTS 播放
  [DialogueManager] 分句TTS 被打断
  [清空队列，开始处理新消息]
```

### 验证重复问题已修复

**检查日志中不应出现：**
- ? 同一句话入队多次
- ? 同一句话播放多次
- ? "队列剩余: X" 数字异常增长

**应该看到：**
- ? 每句话只入队一次
- ? 顺序播放完整
- ? 队列数量正常递减

## ?? 关键要点总结

### 问题本质
**位置跟踪混乱** + **状态管理复杂** = **重复提取和播放**

### 解决方案
1. **分离职责**：提取位置 vs 播放队列
2. **简化状态**：移除冗余标记
3. **防御编程**：清空队列、增强日志

### 最佳实践
```csharp
// ? 单一职责
int position;        // 只跟踪位置
StringBuilder buffer; // 只缓冲文本
Queue queue;         // 只管理队列

// ? 简单状态
if (hasNewContent) { process(); }  // 简单条件
// 而不是
if (hasNewContent && !isProcessing && !isPlaying && ...) { }

// ? 防御式清理
StopCurrentTTS() {
    Cancel();
    ClearQueue();  // 显式清理
    ResetFlags();
}
```

## ?? 修复完成

现在 TTS 应该能够：
1. ? 正确识别每个完整句子
2. ? 每句话只播放一次
3. ? API 只被调用必要的次数
4. ? 流畅地说出完整对话
5. ? 支持用户打断

测试时请观察控制台日志，确认：
- 每句话只入队一次
- 队列按顺序处理
- 没有重复播放
