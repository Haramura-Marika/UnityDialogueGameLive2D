// 这个文件专门用来定义我们项目中模块间传递数据时所用的结构体
// 它不需要继承MonoBehaviour，因为它只是一个数据的“容器”，不附加到任何游戏对象上
// 也不需要using UnityEngine;

// System.Serializable 让这个结构体可以在Unity的Inspector面板中显示，方便调试
[System.Serializable]
public class AIResponseData
{
    public string dialogue;   // 对话内容
    public string emotion;    // 情绪指令 (e.g., "happy", "sad")
    public string action;     // 动作指令 (e.g., "nod", "wave_hand")
    // 为了和Gemini的默认行为兼容，我们暂时不需要intensity和story_event
    // public float intensity;   // 情绪强度 (0.0 to 1.0)
    // public string story_event; // 故事事件指令 (e.g., "event_A_triggered")
}