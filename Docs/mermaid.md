```mermaid
flowchart TD
    %% ================= 核心适配与代理层 =================
    subgraph Core_Layer ["核心适配与代理层"]
        DM["DialogueManager<br>(主线程调度核心)"]
        Enum{"AIModelProvider<br>(模型枚举配置)"}
        AIService["AIService<br>(静态门面类 / 统一接口)"]
        
        Models[/"多模型底层服务<br>DeepSeek / Qwen / Gemini / Minimax"/]
        
        DM -->|根据配置切换| Enum
        Enum -.->|决定具体实现| AIService
        DM -->|"调用 GetAIResponse(Stream)"| AIService
        AIService -->|屏蔽底层 API 差异| Models
    end

    %% ================= 流式并发与输出管线 =================
    subgraph Streaming_Pipeline ["流式并发与 UI/TTS 管线"]
        Queue["ConcurrentQueue< Action ><br>(跨线程安全调度)"]
        Extract["TryExtractDialoguePartial()<br>(增量提取不完整 JSON)"]
        
        TTSBuffer["StringBuilder 缓冲区<br>(实时检测 。！？ 断句)"]
        TTSQueue["_ttsQueue<br>(主线程 Update 串行播放)"]
        
        Models -- "SSE 异步流式返回" --> Queue
        Queue -->|回到主线程| DM
        DM -->|1. 刷新文本气泡| Extract
        DM -->|2. 累加语音文本| TTSBuffer
        TTSBuffer -->|匹配到完整句子| TTSQueue
    end

    %% ================= 解析容错与后处理管线 =================
    subgraph Parse_Pipeline ["JSON 容错与数值解析管线"]
        ParseLogic["ParseAIResponse()<br>(多层级降级解析策略)"]
        Fallback["直接解析 ➔ 反转义 ➔<br>拆包已知结构 ➔ 字符串兜底匹配"]
        
        StatsLogic["ExecuteCharacterAction()<br>(执行动作与属性增减)"]
        Clamp["Mathf.Clamp<br>(限制 0~100 范围)"]
        Event(("OnStatsChanged 事件<br>(通知 UI 模块更新)"))
        
        Models -. "完整接收后" .-> ParseLogic
        ParseLogic --- Fallback
        ParseLogic -->|解析出 emotion/action/5维变化| StatsLogic
        StatsLogic --> Clamp
        Clamp -->|触发数值变更| Event
    end

    %% 美化样式
    classDef core fill:#e3f2fd,stroke:#1e88e5,stroke-width:2px;
    classDef stream fill:#fff3e0,stroke:#fb8c00,stroke-width:2px;
    classDef parse fill:#f1f8e9,stroke:#43a047,stroke-width:2px;
    
    class DM,AIService,Models core;
    class Extract,TTSQueue stream;
    class ParseLogic,StatsLogic parse;