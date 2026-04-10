using UnityEngine;

public class Live2DController : MonoBehaviour
{
    #region --- 引用与变量 ---

    private Animator characterAnimator;

    // 枚举(Enum)现在只包含【面部表情】
    public enum Expression
    {
        Default,
        Proud,
        Sad,
        Smile,
        Angry
    }

    #endregion


    #region --- Unity生命周期方法 ---

    private void Awake()
    {
        characterAnimator = GetComponent<Animator>();
        if (characterAnimator == null)
        {
            Debug.LogError("Live2DController: 在对象上找不到Animator组件！");
        }
    }

    // Update会每帧被调用一次，用于测试
    private void Update()
    {
        #region --- 测试代码  ---

        // --- 表情控制 (数字键 1-5) ---
        if (Input.GetKeyDown(KeyCode.Alpha1)) { SetExpression(Expression.Default); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { SetExpression(Expression.Proud); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { SetExpression(Expression.Sad); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { SetExpression(Expression.Smile); }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { SetExpression(Expression.Angry); }


        // --- 动作触发 (数字键 6-9) ---
        if (Input.GetKeyDown(KeyCode.Alpha6)) { PlayActionTrigger("Hello"); }
        if (Input.GetKeyDown(KeyCode.Alpha7)) { PlayActionTrigger("Thinking"); }
        if (Input.GetKeyDown(KeyCode.Alpha8)) { PlayActionTrigger("Proud"); }    // 对应 02_Proud_Animation
        if (Input.GetKeyDown(KeyCode.Alpha9)) { PlayActionTrigger("Shy"); }      // 对应 05_Shy_Animation

        #endregion
    }

    #endregion


    #region --- 公共控制方法 ---

    /// <summary>
    /// 设置角色的【面部表情】
    /// </summary>
    public void SetExpression(Expression expression)
    {
        if (characterAnimator == null) return;
        float expressionValue = 0f;
        switch (expression)
        {
            case Expression.Default: expressionValue = 0f; break; // 对应 06_Default_Exp
            case Expression.Proud: expressionValue = 0.25f; break; // 对应 07_Proud_Exp
            case Expression.Sad: expressionValue = 0.5f; break; // 对应 08_Sad_Exp
            case Expression.Smile: expressionValue = 0.75f; break; // 对应 09_Smile_Exp
            case Expression.Angry: expressionValue = 1.0f; break; // 对应 10_Angry_Exp
        }
        characterAnimator.SetFloat("ExpressionState", expressionValue);
    }

    /// <summary>
    /// 播放一个一次性的【身体动作】（通过触发器Trigger）
    /// </summary>
    public void PlayActionTrigger(string triggerName)
    {
        if (characterAnimator == null) return;
        characterAnimator.SetTrigger(triggerName);
    }

    #endregion
}