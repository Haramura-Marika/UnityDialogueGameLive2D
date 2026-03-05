using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 管理并显示当前的数值信息（好感度、心情、精力、压力、信任度�?
/// 使用 Slider 组件作为进度条，避免图片拉伸变形
/// </summary>
public class StatsUIManager : MonoBehaviour
{
    [Header("Slider 引用 (�?Inspector 中拖�?Slider 对象)")] [SerializeField]
    private Slider affinitySlider;

    [SerializeField] private Slider moodSlider;
    [SerializeField] private Slider energySlider;
    [SerializeField] private Slider stressSlider;
    [SerializeField] private Slider trustSlider;

    [Header("UI 文本引用 (可选，用于显示具体数�?")] [SerializeField]
    private TextMeshProUGUI affinityText;

    [SerializeField] private TextMeshProUGUI moodText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private TextMeshProUGUI stressText;
    [SerializeField] private TextMeshProUGUI trustText;

    [Header("动画设置")] [SerializeField] private float lerpSpeed = 5f;
    
    // 目标值（用于平滑过渡）
    // 默认均从 50 (对应数值50) 开始显�?
    private float _targetAffinity = 50f;
    private float _targetMood = 50f;
    private float _targetEnergy = 50f;
    private float _targetStress = 50f;
    private float _targetTrust = 50f;

    private void Awake()
    {
        // 在 Awake 中初始化 Slider，确保在 OnEnable 订阅事件之前完成
        InitSlider(affinitySlider);
        InitSlider(moodSlider);
        InitSlider(energySlider);
        InitSlider(stressSlider);
        InitSlider(trustSlider);
    }

    private void Start()
    {
        // 尝试初始获取当前数值
        if (DialogueManager.Instance != null)
        {
            UpdateStatsUI(
                DialogueManager.Instance.currentAffinity,
                DialogueManager.Instance.currentMood,
                DialogueManager.Instance.currentEnergy,
                DialogueManager.Instance.currentStress,
                DialogueManager.Instance.currentTrust
            );
        }

        // 首次直接设置，不做动�?
        ApplySliderImmediate();
    }

    private void OnEnable()
    {
        DialogueManager.OnStatsChanged += UpdateStatsUI;
    }

    private void OnDisable()
    {
        DialogueManager.OnStatsChanged -= UpdateStatsUI;
    }

    private void Update()
    {
        // 平滑过渡到目标�?
        float t = Time.unscaledDeltaTime * lerpSpeed;
        LerpSlider(affinitySlider, _targetAffinity, t);
        LerpSlider(moodSlider, _targetMood, t);
        LerpSlider(energySlider, _targetEnergy, t);
        LerpSlider(stressSlider, _targetStress, t);
        LerpSlider(trustSlider, _targetTrust, t);
    }

    private void UpdateStatsUI(int affinity, int mood, int energy, int stress, int trust)
    {
        _targetAffinity = affinity;
        _targetMood = mood;
        _targetEnergy = energy;
        _targetStress = stress;
        _targetTrust = trust;

        if (affinityText != null) affinityText.text = $"好感度: {affinity}";
        if (moodText != null) moodText.text = $"心情值: {mood}";
        if (energyText != null) energyText.text = $"精力值: {energy}";
        if (stressText != null) stressText.text = $"压力值: {stress}";
        if (trustText != null) trustText.text = $"信任度: {trust}";
    }

    private void InitSlider(Slider slider)
    {
        if (slider == null) return;
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 50f;

        // 设置为不可交互并关掉过渡，也可以避免置灰
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        // 禁用所有的射线检测，彻底阻止鼠标在 UI 上的物理拖拽等交互事件
        Graphic[] graphics = slider.GetComponentsInChildren<Graphic>(true);
        foreach (var g in graphics)
        {
            g.raycastTarget = false;
        }

        // 彻底切断该组件的事件接收（避免重复添加 CanvasGroup）
        CanvasGroup cg = slider.gameObject.GetComponent<CanvasGroup>();
        if (cg == null)
        {
            cg = slider.gameObject.AddComponent<CanvasGroup>();
        }
        cg.interactable = false;
        cg.blocksRaycasts = false;
    }

    private void LerpSlider(Slider slider, float target, float t)
    {
        if (slider == null) return;
        slider.value = Mathf.Lerp(slider.value, target, t);
    }

    private void ApplySliderImmediate()
    {
        if (affinitySlider != null) affinitySlider.value = _targetAffinity;
        if (moodSlider != null) moodSlider.value = _targetMood;
        if (energySlider != null) energySlider.value = _targetEnergy;
        if (stressSlider != null) stressSlider.value = _targetStress;
        if (trustSlider != null) trustSlider.value = _targetTrust;
    }
}
