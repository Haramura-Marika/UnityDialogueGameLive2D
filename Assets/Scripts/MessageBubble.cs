using UnityEngine;
using UnityEngine.UI; // 我们需要它来引用Image组件
using TMPro;          // 引入TextMeshPro的命名空间
using System.Collections; // 添加协程支持

// 这个脚本应该被附加到"气泡背景"的Image对象上
[ExecuteInEditMode] // (可选) 这行代码能让布局在编辑器里也实时更新，方便预览
public class MessageBubble : MonoBehaviour
{
    [Header("组件引用")]
    [Tooltip("请把【作为子对象的】TextMeshPro文本组件拖到这里")]
    public TextMeshProUGUI textComponent; // 用于显示文字的TMP组件

    [Header("布局设置")]
    [Tooltip("气泡的最大宽度，超过这个宽度文字就会换行")]
    public float maxWidth = 800f; // 气泡的最大宽度

    [Tooltip("文字与气泡边框的间距")]
    public Vector2 padding = new Vector2(30, 30); // 内边距 (x代表左右，y代表上下)

    private RectTransform rectTransform; // 自身(气泡背景)的RectTransform

    void Awake()
    {
        // 获取自身的RectTransform组件
        rectTransform = GetComponent<RectTransform>();
    }

    private void Start()
    {
        StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null; // 等待一帧，确保文本和布局已初始化
        UpdateLayout();
    }

    // 当Inspector里的值被修改时，在编辑器模式下也调用布局更新，方便预览
    private void OnValidate()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }
        if (textComponent != null)
        {
            // 延迟到下一帧更新，避免在OnValidate中直接修改RectTransform
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && textComponent != null)
                {
                    UpdateLayout();
                }
            };
            #endif
        }
    }

    /// <summary>
    /// 公共方法，用于从外部设置文本并更新布局
    /// </summary>
    public void SetText(string newText)
    {
        if (textComponent == null) return;

        // 1. 更新文本内容
        textComponent.text = newText;

        Canvas.ForceUpdateCanvases();

        // 2. 更新布局
        UpdateLayout();
    }

    /// <summary>
    /// 核心方法：根据文本内容计算并更新UI尺寸
    /// </summary>
    /// <summary>
    /// 核心方法：根据文本内容计算并更新UI尺寸
    /// </summary>
    private void UpdateLayout()
    {
        if (textComponent == null || rectTransform == null) return;

        // 获取自身的RectTransform和文本的RectTransform
        RectTransform textRect = textComponent.GetComponent<RectTransform>();

        // 强制文本组件立刻计算它理想的大小
        textComponent.ForceMeshUpdate();

        // 获取文本在不换行时的理想宽度
        float singleLinePreferredWidth = textComponent.GetPreferredValues(textComponent.text).x;

        float finalWidth;

        if (singleLinePreferredWidth < maxWidth)
        {
            finalWidth = singleLinePreferredWidth;
        }
        else
        {
            finalWidth = maxWidth;
        }

        // 【Plan B 关键代码】: 在计算最终高度之前，先强制应用一次宽度
        // 这会“提醒”TextMeshPro：“你的新宽度是finalWidth，请准备好在这个宽度下换行”
        textRect.sizeDelta = new Vector2(finalWidth, 0);
        textComponent.ForceMeshUpdate(true); // 再次强制刷新以应用新宽度

        // 现在，在宽度已经被正确应用的前提下，再次计算理想高度
        float finalHeight = textComponent.GetPreferredValues(textComponent.text, finalWidth, 0).y;

        // 手动设置【文本框自身】的最终大小
        textRect.sizeDelta = new Vector2(finalWidth, finalHeight);

        // 手动设置【气泡背景】的大小（文本大小 + 内边距）
        rectTransform.sizeDelta = new Vector2(finalWidth + padding.x, finalHeight + padding.y);
    }
}