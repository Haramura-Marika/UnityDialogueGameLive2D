#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using AI.Config;

namespace AI.Editor
{
    /// <summary>
    /// API 配置管理器的自定义编辑器
    /// 提供更友好的 Inspector 界面
    /// </summary>
    [CustomEditor(typeof(AIAPISettings))]
    public class AIAPISettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            AIAPISettings manager = (AIAPISettings)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "这是 AI 服务的统一配置文件。\n\n" +
                "请在下方填写各个服务的 API Key。\n" +
                "此文件应放置在 Resources 文件夹中，命名为 'APISettings'。\n" +
                "不要将包含真实 API Key 的文件提交到公共仓库！",
                MessageType.Info);
            EditorGUILayout.Space(10);

            // 绘制默认 Inspector
            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // 验证按钮
            if (GUILayout.Button("验证所有配置", GUILayout.Height(30)))
            {
                manager.ValidateConfigs();
            }

            EditorGUILayout.Space(5);

            // 配置状态概览
            EditorGUILayout.LabelField("配置状态概览", EditorStyles.boldLabel);
            DrawConfigStatus("DeepSeek", manager.DeepSeek.IsConfigured());
            DrawConfigStatus("Gemini", manager.Gemini.IsConfigured());
            DrawConfigStatus("Qwen", manager.Qwen.IsConfigured()); // 新增：Qwen 对话配置状态
            DrawConfigStatus("Qwen ASR", manager.QwenASR.IsConfigured());
            DrawConfigStatus("Qwen TTS", manager.QwenTTS.IsConfigured());
            DrawConfigStatus("Minimax TTS", manager.MinimaxTTS.IsConfigured());

            EditorGUILayout.Space(10);

            // 快速设置按钮
            EditorGUILayout.LabelField("快速设置", EditorStyles.boldLabel);
            
            if (GUILayout.Button("设置所有 Qwen 服务为相同 API Key"))
            {
                string key = manager.Qwen.ApiKey; // 优先读取对话服务的 Key
                if (string.IsNullOrEmpty(key))
                {
                    // 若为空，尝试读取 ASR 的 Key
                    key = manager.QwenASR.ApiKey;
                }
                if (string.IsNullOrEmpty(key))
                {
                    // 再尝试读取 TTS 的 Key
                    key = manager.QwenTTS.ApiKey;
                }
                if (string.IsNullOrEmpty(key))
                {
                    // 仍为空则让用户选择文本文件
                    string file = EditorUtility.OpenFilePanel("选择包含 API Key 的文本文件", "", "txt");
                    if (!string.IsNullOrEmpty(file))
                    {
                        key = System.IO.File.ReadAllText(file).Trim();
                    }
                }
                
                if (!string.IsNullOrEmpty(key))
                {
                    manager.Qwen.ApiKey = key;     // 新增：设置对话 Qwen 的 API Key
                    manager.QwenASR.ApiKey = key;
                    manager.QwenTTS.ApiKey = key;
                    EditorUtility.SetDirty(manager);
                    Debug.Log("已设置所有 Qwen 服务的 API Key");
                }
            }
        }

        private void DrawConfigStatus(string serviceName, bool isConfigured)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(serviceName, GUILayout.Width(120));
            
            if (isConfigured)
            {
                var oldColor = GUI.color;
                GUI.color = Color.green;
                EditorGUILayout.LabelField("已配置", EditorStyles.boldLabel);
                GUI.color = oldColor;
            }
            else
            {
                var oldColor = GUI.color;
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField("未配置", EditorStyles.boldLabel);
                GUI.color = oldColor;
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
