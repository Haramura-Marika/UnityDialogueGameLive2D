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
        #region --- 测试代码 (后续会被AI指令替代) ---

        // --- 表情控制 (按主键盘数字键 0-4) ---
        if (Input.GetKeyDown(KeyCode.Alpha0)) { SetExpression(Expression.Default); }
        if (Input.GetKeyDown(KeyCode.Alpha1)) { SetExpression(Expression.Proud); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { SetExpression(Expression.Sad); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { SetExpression(Expression.Smile); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { SetExpression(Expression.Angry); }


        // --- 动作触发 (按字母键 Q, W, E, R, T) ---
        // 我们为每一个真实的身体动作都分配一个按键和触发器名字
        if (Input.GetKeyDown(KeyCode.Q)) { PlayActionTrigger("Hello"); }
        if (Input.GetKeyDown(KeyCode.W)) { PlayActionTrigger("Thinking"); }
        if (Input.GetKeyDown(KeyCode.E)) { PlayActionTrigger("Proud"); }  // 对应 02_Proud_Animation
        if (Input.GetKeyDown(KeyCode.R)) { PlayActionTrigger("Shy"); }    // 对应 05_Shy_Animation
        // 注意：Idle是默认返回状态，通常不需要手动触发

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
        // 注意：这里的数字需要和你Blend Tree里的阈值(Threshold)完全对应
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