# AI 服务配置指南

## ?? 概述

本项目使用**模块化配置管理**，将所有 API Key 和服务配置统一存储在 `AIAPISettings` 中。

### ? 优势
- **关注点分离**：Manager 类只负责业务逻辑，Service 类自己读取配置
- **易于维护**：所有 API 配置集中管理，无需修改代码
- **安全性**：配置文件可单独维护，可添加到 `.gitignore`
- **灵活切换**：切换模型或服务只需修改配置，无需改代码

---

## ?? 快速开始

### 1. 创建配置文件

1. 在 Unity 编辑器中，右键点击 `Assets` 文件夹
2. 选择 `Create > AI Config > API Settings`
3. 将生成的配置文件命名为 `APISettings`
4. **重要**：将该文件移动到 `Assets/Resources/` 文件夹中

### 2. 配置 API Key

在 Unity Inspector 中选中 `APISettings` 文件，填写以下信息：

#### DeepSeek 配置
- **API Key**: 填入 DeepSeek API 密钥
- **Model**: `deepseek-chat` (默认)

#### Gemini 配置
- **API Key**: 填入 Google Gemini API 密钥  
- **Model**: `gemini-2.0-flash` (默认)

#### Qwen ASR (语音识别) 配置
- **API Key**: 填入阿里云 DashScope API 密钥
- **Model**: `qwen3-asr-flash` (默认)
- **Enable Lid**: 是否启用语言识别 (默认: true)
- **Enable Itn**: 是否启用文本反归一化 (默认: false)

#### Qwen TTS (语音合成) 配置
- **API Key**: 填入阿里云 DashScope API 密钥
- **Model**: `qwen3-tts-flash` (默认)
- **Voice**: 音色名称，如 `Cherry` (默认)
- **Language Type**: `Chinese` / `English` 等

#### Minimax TTS 配置
- **API Key**: 填入 Minimax API 密钥
- **Group ID**: 填入 Minimax Group ID
- **Voice ID**: 音色 ID，如 `female-shaonv` (默认)

### 3. 验证配置

点击 Inspector 中的 **"?? 验证所有配置"** 按钮，检查配置是否正确。

---

## ?? 文件结构

```
Assets/
├── Resources/
│   └── APISettings.asset          # 所有 API 配置文件（需手动创建）
└── Scripts/
    └── Config/
        ├── APIConfig.cs           # 配置类定义
        ├── AIAPISettings.cs       # 配置管理器（ScriptableObject）
        └── Editor/
            └── AIAPISettingsEditor.cs  # 自定义 Inspector 界面
```

---

## ?? 在代码中使用

### 方式 1: Service 自动读取（推荐）

Service 类会自动从 `AIAPISettings.Instance` 读取配置：

```csharp
// Gemini_Service.cs 示例
var config = AIAPISettings.Instance?.Gemini;
string apiKey = config.ApiKey;
```

### 方式 2: Manager 中获取配置

```csharp
// 获取特定服务的配置
var geminiConfig = AIAPISettings.Instance?.Gemini;
var deepSeekConfig = AIAPISettings.Instance?.DeepSeek;

// 使用配置
if (geminiConfig != null && geminiConfig.IsConfigured())
{
    string apiKey = geminiConfig.ApiKey;
    // 使用 apiKey...
}
```

---

## ?? 添加新服务

如需添加新的 AI 服务，只需 3 步：

### 1. 在 `APIConfig.cs` 中定义配置类

```csharp
[Serializable]
public class NewServiceConfig : APIConfigBase
{
    [SerializeField] private string model = "default-model";
    
    public string Model
    {
        get => model;
        set => model = value;
    }
}
```

### 2. 在 `AIAPISettings.cs` 中添加配置字段

```csharp
[Header("=== New Service 配置 ===")]
[SerializeField] private NewServiceConfig newServiceConfig = new NewServiceConfig();

public NewServiceConfig NewService => newServiceConfig;
```

### 3. 在编辑器中添加验证

在 `AIAPISettingsEditor.cs` 的 `OnInspectorGUI()` 方法中添加：

```csharp
DrawConfigStatus("New Service", manager.NewService.IsConfigured());
```

---

## ?? 注意事项

1. **配置文件位置**：必须放在 `Assets/Resources/` 目录下，命名为 `APISettings`
2. **安全性**：不要将包含真实 API Key 的配置文件提交到公共仓库
3. **单例模式**：通过 `AIAPISettings.Instance` 访问配置
4. **验证配置**：使用 `IsConfigured()` 方法检查配置是否有效

---

## ?? .gitignore 配置

建议将配置文件添加到 `.gitignore`：

```gitignore
# 不要提交包含真实 API Key 的配置文件
Assets/Resources/APISettings.asset
Assets/Resources/APISettings.asset.meta
```

---

## ?? 当前支持的服务

| 服务 | 用途 | 配置类 |
|------|------|--------|
| **DeepSeek** | AI 对话 | `DeepSeekConfig` |
| **Gemini** | AI 对话 | `GeminiConfig` |
| **Qwen ASR** | 语音识别 | `QwenASRConfig` |
| **Qwen TTS** | 语音合成 | `QwenTTSConfig` |
| **Minimax TTS** | 语音合成 | `MinimaxTTSConfig` |

---

## ?? 常见问题

### Q: 为什么我的 Service 获取不到配置？
A: 确保：
1. 配置文件在 `Resources` 文件夹中
2. 文件名为 `APISettings`
3. 已填写正确的 API Key

### Q: 如何切换 AI 模型？
A: 在 Unity Inspector 中打开 `APISettings` 文件，修改对应服务的 Model 字段即可。

### Q: 为什么编辑器显示命名冲突？
A: 项目使用 `AIAPISettings` 类名以避免与 Live2D.Cubism 库的命名冲突。

---

**配置完成后，即可开始使用各项 AI 服务！** ??
