using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AI.Providers;

public enum AIModelProvider
{
    Gemini,
    DeepSeek,
    Qwen,
}

public static class AIService
{
    private static AIModelProvider _currentProvider = AIModelProvider.DeepSeek;

    public static void SetProvider(AIModelProvider provider) => _currentProvider = provider;
    public static AIModelProvider GetProvider() => _currentProvider;

    // 统一对外接口：使用通用 ChatMessage 历史（一次性返回）
    public static Task<string> GetAIResponse(List<ChatMessage> chatHistory)
    {
        switch (_currentProvider)
        {
            case AIModelProvider.Gemini:
                return Gemini_Service.GetAIResponse(chatHistory);
            case AIModelProvider.DeepSeek:
                return DeepSeek_Service.GetAIResponse(chatHistory);
            case AIModelProvider.Qwen:
                return Qwen_Service.SendMessage(null, chatHistory);
            default:
                return Task.FromResult("{\"dialogue\":\"当前选择的模型未实现\", \"emotion\":\"Sad\", \"action\":\"\"}");
        }
    }

    // 新增：流式回调接口（若当前 Provider 未实现，则退化为一次性 onCompleted）
    public static async Task GetAIResponseStream(
        List<ChatMessage> chatHistory,
        Action<string> onDelta,
        Action<string> onCompleted,
        Action<string> onError,
        CancellationToken ct)
    {
        switch (_currentProvider)
        {
            case AIModelProvider.Qwen:
                await Qwen_Service.SendMessageStream(null, chatHistory, onDelta, onCompleted, onError, ct);
                break;
            case AIModelProvider.DeepSeek:
                await DeepSeek_Service.SendStream(null, chatHistory, onDelta, onCompleted, onError, ct);
                break;
            case AIModelProvider.Gemini:
                await Gemini_Service.SendStream(null, chatHistory, onDelta, onCompleted, onError, ct);
                break;
            default:
                onError?.Invoke("当前选择的模型未实现流式接口");
                break;
        }
    }
}
