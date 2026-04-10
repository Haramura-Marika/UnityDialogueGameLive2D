import sys
p = r'd:\Apps\Unity\Project\Live2D\Assets\Scripts\UI\StatsUIManager.cs'
with open(p, 'r', encoding='utf-8-sig') as f:
    lines = f.readlines()
lines[5] = '/// 管理并显示当前的数值信息（好感度、心情、精力、压力、信任度）\n'
lines[10] = '    [Header("Slider 引用 (在 Inspector 中拖入 Slider 对象)")] [SerializeField]\n'
lines[18] = '    [Header("UI 文本引用 (可选，用于显示具体数值)")] [SerializeField]\n'
lines[29] = '    // 默认均从 50 (对应数值50) 开始显示\n'
lines[60] = '        // 首次直接设置，不做动画\n'
with open(p, 'w', encoding='utf-8-sig') as f:
    f.writelines(lines)
print('Done')
