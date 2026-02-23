using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using Mono.Data.Sqlite;
using Newtonsoft.Json;
using AI.Providers;

/// <summary>
/// SQLite 数据库管理器，负责用户账户和对话历史记录的持久化存储。
/// 替代 PlayerPrefs，提供更可靠的本地数据存储方案。
/// </summary>
public class DatabaseManager : MonoBehaviour
{
    public static DatabaseManager Instance { get; private set; }

    private string dbPath;
    private string connectionString;

    /// <summary>
    /// 确保 DatabaseManager 实例存在。
    /// 如果场景中没有手动放置，会自动创建一个。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (Instance == null)
        {
            var go = new GameObject("[DatabaseManager]");
            Instance = go.AddComponent<DatabaseManager>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeDatabase();
    }

    /// <summary>
    /// 转义 SQL 字符串值，防止单引号导致语法错误
    /// </summary>
    private static string Esc(string value)
    {
        if (value == null) return "";
        return value.Replace("'", "''");
    }

    /// <summary>
    /// 初始化数据库：创建数据库文件和表结构
    /// </summary>
    private void InitializeDatabase()
    {
#if UNITY_EDITOR
        string dbFolder = Path.Combine(Application.dataPath, "..", "Database");
#else
        string dbFolder = Path.Combine(Application.persistentDataPath, "Database");
#endif
        if (!Directory.Exists(dbFolder))
        {
            Directory.CreateDirectory(dbFolder);
        }

        dbPath = Path.Combine(dbFolder, "game_data.db");
        connectionString = "URI=file:" + dbPath;

        Debug.Log($"[DatabaseManager] 数据库路径: {dbPath}");

        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS users (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            username TEXT NOT NULL UNIQUE,
                            password_hash TEXT NOT NULL,
                            created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                        );";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS chat_history (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            user_id INTEGER NOT NULL,
                            character_name TEXT NOT NULL,
                            slot_name TEXT NOT NULL DEFAULT 'autosave',
                            chat_data TEXT NOT NULL,
                            timestamp TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                            FOREIGN KEY (user_id) REFERENCES users(id)
                        );";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_chat_user_slot 
                        ON chat_history(user_id, slot_name);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS translation_cache (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            source_hash TEXT NOT NULL UNIQUE,
                            translated_text TEXT NOT NULL,
                            created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                        );";
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            Debug.Log("[DatabaseManager] 数据库初始化完成");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 数据库初始化失败: {ex.Message}");
        }
    }

    // ==========================================
    // 用户管理相关方法
    // ==========================================

    public bool RegisterUser(string username, string passwordHash)
    {
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                // 检查用户名是否已存在
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = '" + Esc(username) + "';";
                    long count = (long)cmd.ExecuteScalar();
                    Debug.Log($"[DatabaseManager] RegisterUser 检查 '{username}' 存在数量: {count}");
                    if (count > 0)
                    {
                        return false;
                    }
                }

                // 插入新用户
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO users (username, password_hash) VALUES ('" 
                        + Esc(username) + "', '" + Esc(passwordHash) + "');";
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            Debug.Log($"[DatabaseManager] 用户 {username} 注册成功");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 注册失败: {ex.Message}");
            return false;
        }
    }

    public bool ValidateUser(string username, string passwordHash)
    {
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT password_hash FROM users WHERE username = '" + Esc(username) + "';";
                    object result = cmd.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                    {
                        return false;
                    }

                    string savedHash = result.ToString();
                    return savedHash == passwordHash;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 验证用户失败: {ex.Message}");
            return false;
        }
    }

    public bool UserExists(string username)
    {
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = '" + Esc(username) + "';";
                    long count = (long)cmd.ExecuteScalar();
                    Debug.Log($"[DatabaseManager] UserExists '{username}' => {count}");
                    return count > 0;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 检查用户失败: {ex.Message}");
            return false;
        }
    }

    public int GetUserId(string username)
    {
        try
        {
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM users WHERE username = '" + Esc(username) + "';";
                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 获取用户ID失败: {ex.Message}");
        }

        return -1;
    }

    // ==========================================
    // 对话历史记录相关方法
    // ==========================================

    public void SaveChatHistory(string username, string characterName, List<ChatMessage> chatHistory, string slotName = "autosave")
    {
        try
        {
            int userId = GetUserId(username);
            if (userId < 0)
            {
                Debug.LogWarning($"[DatabaseManager] 用户 {username} 不存在，无法保存历史记录");
                return;
            }

            string chatDataJson = JsonConvert.SerializeObject(chatHistory);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM chat_history WHERE user_id = " + userId 
                        + " AND slot_name = '" + Esc(slotName) + "';";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT INTO chat_history (user_id, character_name, slot_name, chat_data, timestamp) VALUES ("
                        + userId + ", '"
                        + Esc(characterName) + "', '"
                        + Esc(slotName) + "', '"
                        + Esc(chatDataJson) + "', '"
                        + Esc(timestamp) + "');";
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            Debug.Log($"[DatabaseManager] 用户 {username} 的对话历史已保存 (角色: {characterName}, 存档: {slotName})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 保存对话历史失败: {ex.Message}");
        }
    }

    public ChatHistoryData LoadChatHistory(string username, string slotName = "autosave")
    {
        try
        {
            int userId = GetUserId(username);
            if (userId < 0)
            {
                Debug.LogWarning($"[DatabaseManager] 用户 {username} 不存在");
                return null;
            }

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT character_name, chat_data, timestamp FROM chat_history WHERE user_id = "
                        + userId + " AND slot_name = '" + Esc(slotName)
                        + "' ORDER BY id DESC LIMIT 1;";

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string characterName = reader.GetString(0);
                            string chatDataJson = reader.GetString(1);
                            string timestamp = reader.GetString(2);

                            var history = JsonConvert.DeserializeObject<List<ChatMessage>>(chatDataJson);

                            Debug.Log($"[DatabaseManager] 已加载用户 {username} 的对话历史 (角色: {characterName}, 时间: {timestamp})");

                            return new ChatHistoryData
                            {
                                characterName = characterName,
                                chatHistory = history,
                                timestamp = timestamp
                            };
                        }
                    }
                }
            }

            Debug.Log($"[DatabaseManager] 用户 {username} 没有存档 ({slotName})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 加载对话历史失败: {ex.Message}");
        }

        return null;
    }

    public List<ChatHistorySummary> GetSaveSlots(string username)
    {
        var result = new List<ChatHistorySummary>();

        try
        {
            int userId = GetUserId(username);
            if (userId < 0) return result;

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT slot_name, character_name, timestamp FROM chat_history WHERE user_id = "
                        + userId + " ORDER BY timestamp DESC;";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new ChatHistorySummary
                            {
                                slotName = reader.GetString(0),
                                characterName = reader.GetString(1),
                                timestamp = reader.GetString(2)
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 获取存档列表失败: {ex.Message}");
        }

        return result;
    }

    public void DeleteSaveSlot(string username, string slotName)
    {
        try
        {
            int userId = GetUserId(username);
            if (userId < 0) return;

            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM chat_history WHERE user_id = " + userId 
                        + " AND slot_name = '" + Esc(slotName) + "';";
                    cmd.ExecuteNonQuery();
                }

                conn.Close();
            }

            Debug.Log($"[DatabaseManager] 已删除用户 {username} 的存档 ({slotName})");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 删除存档失败: {ex.Message}");
        }
    }

    public string GetCurrentUsername()
    {
        return PlayerPrefs.GetString("current_user", string.Empty);
    }

    // ==========================================
    // 翻译缓存相关方法
    // ==========================================

    public string GetCachedTranslation(string sourceText)
    {
        try
        {
            string hash = ComputeHash(sourceText);
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT translated_text FROM translation_cache WHERE source_hash = '" + Esc(hash) + "';";
                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return result.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 查询翻译缓存失败: {ex.Message}");
        }
        return null;
    }

    public void SaveTranslationCache(string sourceText, string translatedText)
    {
        try
        {
            string hash = ComputeHash(sourceText);
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "INSERT OR REPLACE INTO translation_cache (source_hash, translated_text) VALUES ('"
                        + Esc(hash) + "', '" + Esc(translatedText) + "');";
                    cmd.ExecuteNonQuery();
                }
            }
            Debug.Log("[DatabaseManager] 翻译缓存已保存");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatabaseManager] 保存翻译缓存失败: {ex.Message}");
        }
    }

    private static string ComputeHash(string input)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}

[Serializable]
public class ChatHistoryData
{
    public string characterName;
    public List<ChatMessage> chatHistory;
    public string timestamp;
}

[Serializable]
public class ChatHistorySummary
{
    public string slotName;
    public string characterName;
    public string timestamp;
}
