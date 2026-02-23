using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI.Providers;
using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace AI.Chat
{
    public static class CharacterCardParser
    {
        /// <summary>
        /// 解析用户自定义的文本格式角色卡
        /// </summary>
        /// <param name="text">原始文本内容</param>
        /// <param name="defaultName">默认角色名</param>
        /// <returns>生成的 CharacterProfile 实例</returns>
        public static CharacterProfile ParseFromText(string text, string defaultName = "Kanari")
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.characterName = defaultName;
            profile.userName = "User"; // 默认用户名

            if (string.IsNullOrEmpty(text)) return profile;

            // 简单的解析逻辑：查找关键词分割
            // 格式：
            // 角色描述：
            // ...
            // 第一条消息开场白：
            // ...

            string descKey = "角色描述：";
            string openingKey = "第一条消息开场白：";

            int descIndex = text.IndexOf(descKey);
            int openingIndex = text.IndexOf(openingKey);

            if (descIndex != -1)
            {
                int contentStart = descIndex + descKey.Length;
                int contentEnd = (openingIndex != -1 && openingIndex > contentStart) ? openingIndex : text.Length;
                profile.persona = text.Substring(contentStart, contentEnd - contentStart).Trim();
            }

            if (openingIndex != -1)
            {
                int contentStart = openingIndex + openingKey.Length;
                profile.openingMessage = text.Substring(contentStart).Trim();
            }
            else
            {
                // 如果没有找到开场白标记，但有描述标记，可能剩下的都是描述？
                // 或者如果两个标记都没找到，整个文本作为描述
                if (descIndex == -1)
                {
                    profile.persona = text.Trim();
                }
            }

            return profile;
        }

        public static async Task<CharacterProfile> ParseFromTextWithAI(string text, string defaultName = "Kanari")
        {
            var emptyProfile = ScriptableObject.CreateInstance<CharacterProfile>();
            emptyProfile.characterName = defaultName;
            emptyProfile.userName = "User";

            if (string.IsNullOrWhiteSpace(text)) return emptyProfile;

            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "system",
                    parts = new List<ChatPart>
                    {
                        new ChatPart
                        {
                            text = "你是角色卡解析器。请把任意格式角色卡转换成严格 JSON，字段必须是: characterName, userName, persona, openingMessage。" +
                                   " 如果未提供角色名，用给定默认名；如果未提供用户名，用 User。输出仅 JSON，不要包含多余文字或 Markdown。" +
                                   " 字符串内如需换行，请使用\\n转义。"
                        }
                    }
                },
                new ChatMessage
                {
                    role = "user",
                    parts = new List<ChatPart>
                    {
                        new ChatPart
                        {
                            text = $"默认角色名: {defaultName}\n\n角色卡内容:\n{text}"
                        }
                    }
                }
            };

            try
            {
                var response = await AIService.GetAIResponse(messages);
                Debug.Log($"[CharacterCardParser] AI raw response: {response}");
                var parsed = TryParseProfileFromResponse(response, defaultName) ?? emptyProfile;

                if (!string.IsNullOrWhiteSpace(parsed.openingMessage) && NeedsOpeningCleanup(parsed.openingMessage))
                {
                    parsed.openingMessage = await NormalizeOpeningMessageAsync(parsed.openingMessage) ?? parsed.openingMessage;
                }

                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CharacterCardParser] AI 解析失败: {ex.Message}");
                return emptyProfile;
            }
        }

        private static bool NeedsOpeningCleanup(string openingMessage)
        {
            if (string.IsNullOrWhiteSpace(openingMessage)) return false;

            if (openingMessage.Contains("*")) return true;

            int chineseCount = 0;
            int letterCount = 0;
            foreach (char c in openingMessage)
            {
                if (c >= 0x4e00 && c <= 0x9fff)
                {
                    chineseCount++;
                }
                else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    letterCount++;
                }
            }

            return letterCount > chineseCount;
        }

        private static async Task<string> NormalizeOpeningMessageAsync(string openingMessage)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "system",
                    parts = new List<ChatPart>
                    {
                        new ChatPart
                        {
                            text = "你是文本规范化助手。请把开场白改写为自然的中文，保持原意。" +
                                   " 去除任何用星号包裹的动作描写，把需要表达的动作改写进正常叙述。" +
                                   " 仅输出纯文本，不要输出 JSON 或 Markdown。"
                        }
                    }
                },
                new ChatMessage
                {
                    role = "user",
                    parts = new List<ChatPart>
                    {
                        new ChatPart { text = openingMessage }
                    }
                }
            };

            try
            {
                var response = await AIService.GetAIResponse(messages);
                return NormalizePlainText(response);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CharacterCardParser] 开场白规范化失败: {ex.Message}");
                return null;
            }
        }

        private static string NormalizePlainText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("```"))
            {
                int firstNewLine = trimmed.IndexOf('\n');
                if (firstNewLine >= 0)
                {
                    trimmed = trimmed.Substring(firstNewLine + 1);
                }
                if (trimmed.EndsWith("```"))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
                }
            }

            return trimmed.Trim().Trim('"');
        }

        private static CharacterProfile TryParseProfileFromResponse(string response, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;

            string candidate = NormalizeCandidate(response);
            if (string.IsNullOrWhiteSpace(candidate)) return null;

            JObject obj = TryParseObject(candidate);
            if (obj == null) return null;

            if (obj["dialogue"] != null && (obj["persona"] == null && obj["openingMessage"] == null))
            {
                var nested = obj["dialogue"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    var nestedObj = TryParseObject(NormalizeCandidate(nested));
                    if (nestedObj != null)
                    {
                        obj = nestedObj;
                    }
                }
            }

            var characterName = obj.Value<string>("characterName")
                                ?? obj.Value<string>("name")
                                ?? defaultName;
            var userName = obj.Value<string>("userName")
                           ?? obj.Value<string>("user")
                           ?? "User";
            var persona = obj.Value<string>("persona")
                          ?? obj.Value<string>("description")
                          ?? obj.Value<string>("character")
                          ?? string.Empty;
            var openingMessage = obj.Value<string>("openingMessage")
                                 ?? obj.Value<string>("opening")
                                 ?? obj.Value<string>("firstMessage")
                                 ?? obj.Value<string>("greeting")
                                 ?? string.Empty;

            if (string.IsNullOrWhiteSpace(persona) && string.IsNullOrWhiteSpace(openingMessage))
            {
                return null;
            }

            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.characterName = string.IsNullOrWhiteSpace(characterName) ? defaultName : characterName.Trim();
            profile.userName = string.IsNullOrWhiteSpace(userName) ? "User" : userName.Trim();
            profile.persona = persona?.Trim() ?? string.Empty;
            profile.openingMessage = openingMessage?.Trim() ?? string.Empty;
            return profile;
        }

        private static JObject TryParseObject(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;

            if (TryParseObjectInternal(candidate, out var obj))
            {
                return obj;
            }

            var fixedCandidate = FixJsonString(candidate);
            if (!string.Equals(fixedCandidate, candidate, StringComparison.Ordinal) &&
                TryParseObjectInternal(fixedCandidate, out obj))
            {
                return obj;
            }

            return null;
        }

        private static bool TryParseObjectInternal(string candidate, out JObject obj)
        {
            obj = null;
            try
            {
                var token = JToken.Parse(candidate);
                obj = token as JObject;
                return obj != null;
            }
            catch
            {
                return false;
            }
        }

        private static string FixJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new StringBuilder(input.Length + 32);
            bool inString = false;
            bool isEscaped = false;

            foreach (char c in input)
            {
                if (isEscaped)
                {
                    sb.Append(c);
                    isEscaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    sb.Append(c);
                    isEscaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    sb.Append(c);
                    continue;
                }

                if (inString)
                {
                    if (c == '\n')
                    {
                        sb.Append("\\n");
                        continue;
                    }

                    if (c == '\r')
                    {
                        sb.Append("\\r");
                        continue;
                    }

                    if (c == '\t')
                    {
                        sb.Append("\\t");
                        continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string NormalizeCandidate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var s = text.Trim();

            if (s.StartsWith("```"))
            {
                int firstNewLine = s.IndexOf('\n');
                if (firstNewLine >= 0)
                {
                    s = s.Substring(firstNewLine + 1);
                }
                if (s.EndsWith("```"))
                {
                    s = s.Substring(0, s.Length - 3);
                }
                s = s.Trim();
            }

            s = MaybeUnescapeJson(s);

            if (!s.StartsWith("{") || !s.EndsWith("}"))
            {
                int start = s.IndexOf('{');
                int end = s.LastIndexOf('}');
                if (start >= 0 && end > start)
                {
                    s = s.Substring(start, end - start + 1);
                }
            }

            return s.Trim();
        }

        private static string MaybeUnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            string trimmed = s.Trim();
            bool wrappedInQuotes = trimmed.Length > 1 && trimmed.StartsWith("\"") && trimmed.EndsWith("\"");
            if (wrappedInQuotes)
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            bool looksEscaped = wrappedInQuotes || trimmed.StartsWith("\\{") || trimmed.StartsWith("\\[");
            if (!looksEscaped) return trimmed;

            try
            {
                return trimmed.Replace("\\\"", "\"")
                              .Replace("\\\\", "\\")
                              .Replace("\\n", "\n")
                              .Replace("\\r", "\r")
                              .Replace("\\t", "\t");
            }
            catch
            {
                return trimmed;
            }
        }
    }
}
