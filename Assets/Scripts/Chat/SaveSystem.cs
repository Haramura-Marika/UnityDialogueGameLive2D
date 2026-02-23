using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using AI.Providers;

namespace AI.Chat
{
    [Serializable]
    public class SaveData
    {
        public string characterName;
        public List<ChatMessage> chatHistory;
        public string timestamp;
    }

    public static class SaveSystem
    {
        public static void SaveGame(string slotName, SaveData data)
        {
            string path = Path.Combine(Application.persistentDataPath, $"{slotName}.json");
            SaveGameToPath(path, data);
        }

        public static SaveData LoadGame(string slotName)
        {
            string path = Path.Combine(Application.persistentDataPath, $"{slotName}.json");
            return LoadGameFromPath(path);
        }

        public static void SaveGameToPath(string path, SaveData data)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[SaveSystem] 保存路径为空，已取消保存。");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
                Debug.Log($"[SaveSystem] Game saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Failed to save game: {ex.Message}");
            }
        }

        public static SaveData LoadGameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[SaveSystem] 读取路径为空，已取消读取。");
                return null;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveSystem] Save file not found: {path}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<SaveData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Failed to load game: {ex.Message}");
                return null;
            }
        }
    }
}
