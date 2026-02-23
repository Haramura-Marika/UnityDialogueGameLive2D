using UnityEngine;
using UnityEngine.UI;
using AI.Chat;

/// <summary>
/// UI管理器 - 管理所有UI界面的显示和隐藏
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI 界面引用")]
    [SerializeField] private GameObject startPanel;      // 开始界面
    [SerializeField] private GameObject gamePanel;       // 游戏主界面
    [SerializeField] private GameObject pausePanel;      // 暂停界面

    [Header("开始界面按钮")]
    [SerializeField] private Button startButton;         // 开始游戏按钮
    [SerializeField] private Button quitButton;          // 退出按钮

    [Header("暂停菜单按钮")]
    [SerializeField] private Button resumeButton;        // 继续游戏按钮
    [SerializeField] private Button backToStartButton;   // 返回开始菜单按钮
    [SerializeField] private Button newGameButton;       // 新建游戏按钮
    [SerializeField] private Button saveGameButton;      // 保存游戏按钮
    [SerializeField] private Button loadGameButton;      // 读取游戏按钮

    [Header("游戏内按钮")]
    [SerializeField] private Button pauseButton;         // 暂停按钮（在游戏界面上）

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
        // 绑定按钮事件
        if (startButton != null)
            startButton.onClick.AddListener(StartGame);
        
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

        if (loadGameButton != null)
            loadGameButton.onClick.AddListener(OnLoadGameClicked);

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

    /// <summary>
    /// 显示开始界面
    /// </summary>
    public void ShowStartPanel()
    {
        SetPanelActive(startPanel, true);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] 显示开始界面");
    }

    /// <summary>
    /// 开始游戏
    /// </summary>
    public void StartGame()
    {
        SetPanelActive(startPanel, false);
        SetPanelActive(gamePanel, true);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] 开始游戏");
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

    private void OnNewGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.RestartChat();
            ResumeGame(); // 重置后继续游戏（取消暂停）
            Debug.Log("[UIManager] 新建游戏（重置当前对话）");
        }
    }

    private void OnSaveGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            string path = FileDialogHelper.ShowSaveFileDialog("选择保存位置", "json", Application.persistentDataPath, "chat_save");
            if (!string.IsNullOrEmpty(path))
            {
                DialogueManager.Instance.SaveProgressToPath(path);
                Debug.Log($"[UIManager] 游戏已保存: {path}");
            }
        }
    }

    private void OnLoadGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            string path = FileDialogHelper.ShowOpenFileDialog("选择存档文件", "json", Application.persistentDataPath);
            if (!string.IsNullOrEmpty(path))
            {
                DialogueManager.Instance.LoadProgressFromPath(path);
                ResumeGame(); // 读取后恢复游戏
                Debug.Log($"[UIManager] 游戏已读取: {path}");
            }
        }
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
