using UnityEngine;

// 确保GameManager类继承自MonoBehaviour，这样它才能被附加到游戏对象上
public class GameManager : MonoBehaviour
{
    // 1. 定义一个公开的、静态的Instance属性，用于全局访问GameManager的唯一实例
    //    公开get，私有set，意味着外部只能读取，只有它自己能修改
    public static GameManager Instance { get; private set; }

    // 2. Awake方法在脚本实例被加载时调用，它比Start()方法更早执行
    private void Awake()
    {
        // 3. 单例模式的核心逻辑
        // 检查是否已经有实例存在了？
        if (Instance != null && Instance != this)
        {
            // 如果已经存在一个实例，并且那个实例不是我自己，
            // 那么就销毁我自己这个“冒牌货”，保证场上永远只有一个老大。
            Destroy(this.gameObject);
        }
        else
        {
            // 如果我是第一个实例，那么就把我自己赋值给这个静态实例
            Instance = this;

            // 4. (可选但强烈推荐) 让GameManager在加载新场景时不被销毁
            //    这确保了我们的“总指挥”在整个程序生命周期中都存在
            DontDestroyOnLoad(this.gameObject);
        }
    }

    // --- 在这里添加你的全局公共方法 ---

    // 这是一个测试方法，用来演示如何从外部调用
    public void PrintHello()
    {
        // Debug.Log会在Unity的控制台打印信息，非常适合调试
        Debug.Log("你好，来自GameManager的问候！");
    }
}