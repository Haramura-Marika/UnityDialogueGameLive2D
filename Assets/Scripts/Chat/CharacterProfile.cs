using UnityEngine;

namespace AI.Chat
{
    [CreateAssetMenu(fileName = "NewCharacter", menuName = "AI/Character Profile")]
    public class CharacterProfile : ScriptableObject
    {
        [Tooltip("角色的名字，用于替换 {{char}}")]
        public string characterName = "Kanari";
        
        [Tooltip("用户的名字，用于替换 {{user}}")]
        public string userName = "User";

        [TextArea(10, 30)] 
        [Tooltip("角色的人设描述")]
        public string persona = ""; // 留空，运行时按角色名自动兜底
        
        [TextArea(5, 15)] 
        [Tooltip("角色的第一句开场白")]
        public string openingMessage = ""; // 留空，运行时按角色名自动兜底
    }
}
