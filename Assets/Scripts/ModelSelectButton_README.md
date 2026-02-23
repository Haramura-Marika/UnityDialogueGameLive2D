# ModelSelectButton 使用说明

## 概述
`ModelSelectButton.cs` 是一个Unity UI按钮控制器，用于在运行时动态切换AI模型提供商（Gemini、DeepSeek、Qwen）。

## 功能特点
- ? 运行时动态切换AI模型
- ? 循环切换三种模型：Gemini → DeepSeek → Qwen → Gemini
- ? 自动显示当前模型名称
- ? 支持手动设置特定模型
- ? 使用反射避免编译顺序问题

## 快速开始

### 1. 在Canvas中设置按钮

#### 方法A：创建新按钮
1. 在 Hierarchy 中右键点击你的 Canvas
2. 选择 `UI` > `Button`
3. 重命名为 "ModelSelectButton"

#### 方法B：使用现有按钮
使用你已经创建的 `ModelSelectButton`

### 2. 添加脚本组件
1. 选中按钮对象
2. 在 Inspector 面板中点击 `Add Component`
3. 搜索并添加 `Model Select Button` 脚本

### 3. 配置组件（可选）

脚本会自动查找组件，但你也可以手动指定：

**UI 组件：**
- `Switch Button`: 切换按钮（不指定则自动从当前对象获取）
- `Button Text`: 显示文本的Text组件（不指定则自动从子对象查找）

**模型配置：**
- `Current Provider Index`: 初始模型索引
  - 0 = Gemini
  - 1 = DeepSeek（默认）
  - 2 = Qwen

### 4. 测试
1. 运行游戏
2. 点击按钮，模型会按顺序切换
3. 按钮文本会显示当前模型名称："模型: Gemini" / "模型: DeepSeek" / "模型: Qwen"

## API 使用

### 通过代码切换模型

```csharp
// 获取按钮组件
ModelSelectButton modelButton = GetComponent<ModelSelectButton>();

// 通过索引设置模型
modelButton.SetModelByIndex(0);  // 设置为 Gemini
modelButton.SetModelByIndex(1);  // 设置为 DeepSeek
modelButton.SetModelByIndex(2);  // 设置为 Qwen

// 获取当前模型信息
int currentIndex = modelButton.GetCurrentProviderIndex();
string currentName = modelButton.GetCurrentProviderName();
Debug.Log($"当前模型: {currentName}");
```

### Inspector 事件设置

你也可以在 Inspector 中为其他按钮设置点击事件：

1. 选中其他按钮
2. 在 `On Click ()` 事件中点击 `+`
3. 拖入 ModelSelectButton 对象
4. 选择函数：`ModelSelectButton` > `SetModelByIndex`
5. 在参数框中输入模型索引（0/1/2）

## 注意事项

### 编译顺序问题
本脚本使用反射技术访问 `AIService` 和 `AIModelProvider`，以避免 Unity 脚本编译顺序问题。这意味着：

- ? 不会产生编译错误
- ? 运行时自动查找并调用 AIService
- ?? 如果 AIService 不存在，会在控制台输出警告但不会崩溃

### 文本组件
- 脚本默认使用 Unity 标准的 `Text` 组件
- 如果你的项目使用 TextMeshPro，可以修改代码中的 `Text` 为 `TextMeshProUGUI` 并添加 `using TMPro;`

### 日志输出
脚本会在控制台输出以下日志信息：
- 初始化信息
- 模型切换信息
- 错误和警告信息

## 故障排除

### 问题：按钮点击没有反应
**解决方案：**
- 检查 Canvas 是否有 `GraphicRaycaster` 组件
- 检查场景是否有 `EventSystem` 对象
- 检查按钮的 `Interactable` 是否勾选

### 问题：按钮文本不显示
**解决方案：**
- 确保按钮的子对象有 Text 组件
- 手动在 Inspector 中指定 `Button Text` 组件

### 问题：切换模型无效
**解决方案：**
- 检查控制台是否有错误信息
- 确保 `AIService.cs` 存在于 `Assets/Scripts/` 目录
- 确认 `AIAPISettings` 配置正确

## 扩展功能

### 添加下拉菜单
你可以创建一个 Dropdown 来显示所有可用模型：

```csharp
using UnityEngine.UI;

public class ModelDropdown : MonoBehaviour
{
    [SerializeField] private Dropdown dropdown;
    [SerializeField] private ModelSelectButton modelButton;
    
    private void Start()
    {
        dropdown.ClearOptions();
        dropdown.AddOptions(new System.Collections.Generic.List<string> 
        { 
            "Gemini", "DeepSeek", "Qwen" 
        });
        
        dropdown.onValueChanged.AddListener(OnDropdownChanged);
    }
    
    private void OnDropdownChanged(int index)
    {
        modelButton.SetModelByIndex(index);
    }
}
```

### 保存用户选择
你可以使用 PlayerPrefs 保存用户的模型选择：

```csharp
// 在 ModelSelectButton.cs 的 SwitchToNextModel() 中添加：
PlayerPrefs.SetInt("SelectedAIModel", currentProviderIndex);
PlayerPrefs.Save();

// 在 Start() 中添加：
if (PlayerPrefs.HasKey("SelectedAIModel"))
{
    currentProviderIndex = PlayerPrefs.GetInt("SelectedAIModel");
    SetModelByIndex(currentProviderIndex);
}
```

## 相关文件
- `Assets/Scripts/AIService.cs` - AI服务管理器
- `Assets/Scripts/Config/AIAPISettings.cs` - API配置
- `Assets/Scripts/DialogueManager.cs` - 对话管理器（使用AI服务）

## 技术支持
如遇问题，请检查：
1. Unity 控制台的错误日志
2. AIService 是否正确初始化
3. API 配置是否正确（在 Resources/APISettings）
