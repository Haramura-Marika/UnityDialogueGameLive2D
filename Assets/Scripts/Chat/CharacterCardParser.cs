using UnityEngine;
using System.Text.RegularExpressions;
using System;

namespace AI.Chat
{
    public static class CharacterCardParser
    {
        /// <summary>
        /// 纯本地解析角色卡文本。
        /// 拆分为两部分：openingMessage（纯开场白，直接显示在屏幕上）和 rawCardText（原始完整文本，发给 AI 理解背景）。
        /// persona 字段保留开场白之前的人设描述部分。
        /// </summary>
        /// <param name="text">原始文本内容</param>
        /// <param name="defaultName">默认角色名</param>
        /// <returns>生成的 CharacterProfile 实例</returns>
        public static CharacterProfile ParseFromText(string text, string defaultName = "Kanari")
        {
            var profile = ScriptableObject.CreateInstance<CharacterProfile>();
            profile.characterName = defaultName;
            profile.userName = "User";

            if (string.IsNullOrEmpty(text)) return profile;

            // 保存原始完整文本（占位符替换后的版本会在最后处理）
            profile.rawCardText = text;

            // 匹配 "角色描述:" / "人设:" / "persona:" / "description:" （冒号可选）
            var descMatch = Regex.Match(text,
                @"(?:^|\n)\s*(角色描述|人设|persona|description)\s*[:：]?\s*(?:\n|$)",
                RegexOptions.IgnoreCase);
            // 匹配 "第一条消息（开场白）" 等开场白标签（冒号可选）
            var openingMatch = Regex.Match(text,
                @"(?:^|\n)\s*(第一条消息（开场白）|第一条消息\(开场白\)|开场白|openingMessage|opening|first_mes|greeting)\s*[:：]?\s*(?:\n|$)",
                RegexOptions.IgnoreCase);

            if (descMatch.Success || openingMatch.Success)
            {
                if (descMatch.Success)
                {
                    int contentStart = descMatch.Index + descMatch.Length;
                    int contentEnd = (openingMatch.Success && openingMatch.Index > contentStart) ? openingMatch.Index : text.Length;
                    profile.persona = text.Substring(contentStart, contentEnd - contentStart).Trim();
                }
                else if (openingMatch.Success)
                {
                    // 没有显式 persona 标签，但有开场白标签：开场白之前的所有文本视为 persona
                    string beforeOpening = text.Substring(0, openingMatch.Index).Trim();
                    if (!string.IsNullOrWhiteSpace(beforeOpening))
                    {
                        profile.persona = beforeOpening;
                    }
                }

                if (openingMatch.Success)
                {
                    int contentStart = openingMatch.Index + openingMatch.Length;
                    profile.openingMessage = text.Substring(contentStart).Trim();
                }
            }
            else
            {
                // 简单多行格式（如 Kanari.txt）
                // 第1行=角色名, 第2行=用户名, 第3行=人设, 第4行=开场白
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 1 && !string.IsNullOrWhiteSpace(lines[0]))
                    profile.characterName = lines[0].Trim();
                if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]))
                    profile.userName = lines[1].Trim();
                if (lines.Length >= 3 && !string.IsNullOrWhiteSpace(lines[2]))
                    profile.persona = lines[2].Trim();
                if (lines.Length >= 4 && !string.IsNullOrWhiteSpace(lines[3]))
                    profile.openingMessage = lines[3].Trim();
            }

            // 替换 {{char}} / {{user}} 占位符
            ReplacePlaceholders(profile);

            return profile;
        }

        /// <summary>
        /// 替换角色卡中的 {{char}} 和 {{user}} 占位符
        /// </summary>
        private static void ReplacePlaceholders(CharacterProfile profile)
        {
            if (profile == null) return;

            string charName = profile.characterName ?? "Kanari";
            string userName = profile.userName ?? "User";

            if (!string.IsNullOrEmpty(profile.persona))
            {
                profile.persona = profile.persona
                    .Replace("{{char}}", charName)
                    .Replace("{{user}}", userName);
            }

            if (!string.IsNullOrEmpty(profile.openingMessage))
            {
                profile.openingMessage = profile.openingMessage
                    .Replace("{{char}}", charName)
                    .Replace("{{user}}", userName);
            }

            if (!string.IsNullOrEmpty(profile.rawCardText))
            {
                profile.rawCardText = profile.rawCardText
                    .Replace("{{char}}", charName)
                    .Replace("{{user}}", userName);
            }
        }
    }
}

