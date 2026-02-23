using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 模型切换按钮控制器
/// 允许在运行时切换不同的 AI 模型提供商（Gemini、DeepSeek、Qwen）
/// 注意：此脚本需要在 AIService.cs 编译后才能正常工作
/// </summary>
public class ModelSelectButton : MonoBehaviour
{
    [Header("UI 组件")]
    [SerializeField] private Button switchButton;
    [SerializeField] private Text buttonText; // 使用标准 Text 组件
    
    [Header("模型配置")]
    [SerializeField] private int currentProviderIndex = 1; // 0=Gemini, 1=DeepSeek, 2=Qwen
    
    // 模型显示名称映射
    private readonly string[] providerNames = { "Gemini", "DeepSeek", "Qwen" };
    
    private void Awake()
    {
        // 如果没有手动指定按钮，尝试从当前对象获取
        if (switchButton == null)
        {
            switchButton = GetComponent<Button>();
        }
        
        // 如果没有指定文本组件，尝试从子对象查找
        if (buttonText == null)
        {
            buttonText = GetComponentInChildren<Text>();
        }
        
        // 注册按钮点击事件
        if (switchButton != null)
        {
            switchButton.onClick.AddListener(OnSwitchButtonClicked);
        }
        else
        {
            Debug.LogWarning("[ModelSelectButton] 未找到 Button 组件！请在 Inspector 中指定或将此脚本挂载到 Button 对象上。");
        }
    }
    
    private void Start()
    {
        // 初始化：从 AIService 获取当前模型
        try
        {
            // 使用反射来避免编译时依赖
            var aiServiceType = Type.GetType("AIService");
            if (aiServiceType != null)
            {
                var getProviderMethod = aiServiceType.GetMethod("GetProvider");
                if (getProviderMethod != null)
                {
                    var currentProvider = getProviderMethod.Invoke(null, null);
                    currentProviderIndex = (int)currentProvider;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ModelSelectButton] 无法获取当前模型: {ex.Message}");
        }
        
        UpdateButtonText();
        Debug.Log($"[ModelSelectButton] 初始化完成，当前模型: {providerNames[currentProviderIndex]}");
    }
    
    /// <summary>
    /// 按钮点击事件处理
    /// </summary>
    private void OnSwitchButtonClicked()
    {
        // 循环切换到下一个模型
        SwitchToNextModel();
    }
    
    /// <summary>
    /// 切换到下一个模型
    /// </summary>
    private void SwitchToNextModel()
    {
        // 切换到下一个模型（循环）
        currentProviderIndex = (currentProviderIndex + 1) % providerNames.Length;
        
        // 应用到 AIService（使用反射）
        try
        {
            var aiServiceType = Type.GetType("AIService");
            if (aiServiceType != null)
            {
                var setProviderMethod = aiServiceType.GetMethod("SetProvider");
                var providerEnumType = Type.GetType("AIModelProvider");
                
                if (setProviderMethod != null && providerEnumType != null)
                {
                    var providerValue = Enum.ToObject(providerEnumType, currentProviderIndex);
                    setProviderMethod.Invoke(null, new object[] { providerValue });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModelSelectButton] 切换模型失败: {ex.Message}");
        }
        
        // 更新按钮文本
        UpdateButtonText();
        
        Debug.Log($"[ModelSelectButton] 已切换到模型: {providerNames[currentProviderIndex]}");
    }
    
    /// <summary>
    /// 手动设置模型（通过索引）
    /// </summary>
    /// <param name="providerIndex">0=Gemini, 1=DeepSeek, 2=Qwen</param>
    public void SetModelByIndex(int providerIndex)
    {
        if (providerIndex < 0 || providerIndex >= providerNames.Length)
        {
            Debug.LogWarning($"[ModelSelectButton] 无效的模型索引: {providerIndex}");
            return;
        }
        
        currentProviderIndex = providerIndex;
        
        // 应用到 AIService
        try
        {
            var aiServiceType = Type.GetType("AIService");
            if (aiServiceType != null)
            {
                var setProviderMethod = aiServiceType.GetMethod("SetProvider");
                var providerEnumType = Type.GetType("AIModelProvider");
                
                if (setProviderMethod != null && providerEnumType != null)
                {
                    var providerValue = Enum.ToObject(providerEnumType, providerIndex);
                    setProviderMethod.Invoke(null, new object[] { providerValue });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModelSelectButton] 设置模型失败: {ex.Message}");
        }
        
        UpdateButtonText();
        Debug.Log($"[ModelSelectButton] 手动设置模型: {providerNames[currentProviderIndex]}");
    }
    
    /// <summary>
    /// 更新按钮显示文本
    /// </summary>
    private void UpdateButtonText()
    {
        if (buttonText != null)
        {
            buttonText.text = $"模型: {providerNames[currentProviderIndex]}";
        }
    }
    
    /// <summary>
    /// 获取当前选中的模型索引
    /// </summary>
    public int GetCurrentProviderIndex()
    {
        return currentProviderIndex;
    }
    
    /// <summary>
    /// 获取当前选中的模型名称
    /// </summary>
    public string GetCurrentProviderName()
    {
        return providerNames[currentProviderIndex];
    }
    
    private void OnDestroy()
    {
        // 清理事件监听
        if (switchButton != null)
        {
            switchButton.onClick.RemoveListener(OnSwitchButtonClicked);
        }
    }
}
