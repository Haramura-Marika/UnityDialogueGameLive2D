using System;
using System.Collections.Generic;

namespace AI.Providers
{
    // 通用对话消息数据模型（取代 GeminiContent/GeminiPart 的通用命名）
    [Serializable]
    public class ChatMessage
    {
        public string role;                 // user / assistant / system（或 model -> 将在 Provider 内部规范化）
        public List<ChatPart> parts;        // 目前仅使用 text
    }

    [Serializable]
    public class ChatPart
    {
        public string text;                 // 纯文本
    }
}
