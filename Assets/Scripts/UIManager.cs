using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI管理器 - 管理所有UI界面的显示和隐藏
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI 面板")]
    [SerializeField] private GameObject startPanel;      // 开始面板
    [SerializeField] private GameObject loginPanel;      // 登录面板（新增）
    [SerializeField] private GameObject gamePanel;       // 游戏面板
    [SerializeField] private GameObject pausePanel;      // 暂停面板

    [Header("开始面板按钮")]
    [SerializeField] private Button startButton;         // 开始游戏按钮
    [SerializeField] private Button quitButton;          // 退出按钮

    [Header("暂停菜单按钮")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button backToStartButton;
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button switchUserButton;      // 切换用户按钮（原loadGameButton）

    [Header("游戏内按钮")]
    [SerializeField] private Button pauseButton;

    private bool isPaused = false;

    private void Awake()
    {
        // 单例模式
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // 确保数据库初始化
        DatabaseManager.EnsureInitialized();

        // 绑定按钮事件
        if (startButton != null)
            startButton.onClick.AddListener(OnStartButtonClicked);
        
        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);
        
        if (pauseButton != null)
            pauseButton.onClick.AddListener(PauseGame);
        
        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeGame);
        
        if (backToStartButton != null)
            backToStartButton.onClick.AddListener(BackToStart);

        if (newGameButton != null)
            newGameButton.onClick.AddListener(OnNewGameClicked);

        if (saveGameButton != null)
            saveGameButton.onClick.AddListener(OnSaveGameClicked);

        if (switchUserButton != null)
            switchUserButton.onClick.AddListener(OnSwitchUserClicked);

        // 初始显示开始面板
        ShowStartPanel();
    }

    private void Update()
    {
        // ESC键快捷暂停/继续
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (gamePanel != null && gamePanel.activeSelf)
            {
                if (isPaused)
                    ResumeGame();
                else
                    PauseGame();
            }
        }
    }

    // ==========================================
    // 面板切换
    // ==========================================

    /// <summary>
    /// 显示开始界面
    /// </summary>
    public void ShowStartPanel()
    {
        SetPanelActive(startPanel, true);
        SetPanelActive(loginPanel, false);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] 显示开始面板");
    }

    /// <summary>
    /// 点击"开始游戏"按钮 → 显示登录面板
    /// </summary>
    private void OnStartButtonClicked()
    {
        SetPanelActive(startPanel, false);
        SetPanelActive(loginPanel, true);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Debug.Log("[UIManager] 显示登录面板");
    }

    /// <summary>
    /// 登录成功后由 LoginUIManager 调用，直接进入游戏
    /// </summary>
    public void EnterGame()
    {
        SetPanelActive(startPanel, false);
        SetPanelActive(loginPanel, false);
        SetPanelActive(gamePanel, true);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;

        // 登录成功后初始化对话（开场白在此时才输出）
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.InitChat();
        }

        Debug.Log("[UIManager] 登录成功，进入游戏");
    }

    /// <summary>
    /// 暂停游戏
    /// </summary>
    public void PauseGame()
    {
        if (isPaused) return;
        
        SetPanelActive(pausePanel, true);
        Time.timeScale = 0f;
        isPaused = true;
        Debug.Log("[UIManager] 暂停游戏");
    }

    /// <summary>
    /// 继续游戏
    /// </summary>
    public void ResumeGame()
    {
        if (!isPaused) return;
        
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] 继续游戏");
    }

    /// <summary>
    /// 返回开始界面
    /// </summary>
    public void BackToStart()
    {
        ShowStartPanel();
        
        // 清理游戏状态（如果需要）
        // 例如：重置对话历史、停止TTS等
        // 注意：由于项目引用限制，需要在游戏中手动处理
    }

    private void OnNewGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.RestartChat();
            ResumeGame();
            Debug.Log("[UIManager] 新建游戏，重置当前对话");
        }
    }

    private void OnSaveGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.SaveProgress("manual_save");
            Debug.Log("[UIManager] 游戏已保存到数据库");
        }
    }

    /// <summary>
    /// 切换用户：自动保存当前对话，清除状态，返回登录面板
    /// </summary>
    private void OnSwitchUserClicked()
    {
        // 自动保存当前对话进度
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.SaveProgress("autosave");
            DialogueManager.Instance.ResetSession();
        }

        // 清除当前用户
        PlayerPrefs.DeleteKey("current_user");
        PlayerPrefs.Save();

        // 显示登录面板
        SetPanelActive(startPanel, false);
        SetPanelActive(loginPanel, true);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;

        Debug.Log("[UIManager] 切换用户，返回登录面板");
    }

    /// <summary>
    /// 退出游戏
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[UIManager] 退出游戏");
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    /// <summary>
    /// 安全设置面板激活状态
    /// </summary>
    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    /// <summary>
    /// 获取暂停状态
    /// </summary>
    public bool IsPaused()
    {
        return isPaused;
    }
}
