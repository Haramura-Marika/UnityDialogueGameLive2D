using System.Threading.Tasks;
using AI.Chat;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject pausePanel;

    [Header("Start Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    [Header("Pause Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button backToStartButton;
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button saveGameButton;
    [SerializeField] private Button switchUserButton;

    [Header("In-Game Buttons")]
    [SerializeField] private Button pauseButton;

    private bool isPaused = false;
    private bool isSwitchingCharacterCard = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        DatabaseManager.EnsureInitialized();

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
            switchUserButton.onClick.AddListener(OnSwitchCharacterCardClicked);

        ShowStartPanel();
    }

    private void Update()
    {
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

    public void ShowStartPanel()
    {
        SetPanelActive(startPanel, true);
        SetPanelActive(loginPanel, false);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] \u663e\u793a\u5f00\u59cb\u9762\u677f");
    }

    private void OnStartButtonClicked()
    {
        SetPanelActive(startPanel, false);
        SetPanelActive(loginPanel, true);
        SetPanelActive(gamePanel, false);
        SetPanelActive(pausePanel, false);
        Debug.Log("[UIManager] \u663e\u793a\u767b\u5f55\u9762\u677f");
    }

    public void EnterGame()
    {
        SetPanelActive(startPanel, false);
        SetPanelActive(loginPanel, false);
        SetPanelActive(gamePanel, true);
        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;

        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.InitChat();
        }

        Debug.Log("[UIManager] \u767b\u5f55\u6210\u529f\uff0c\u8fdb\u5165\u6e38\u620f");
    }

    public void PauseGame()
    {
        if (isPaused) return;

        SetPanelActive(pausePanel, true);
        Time.timeScale = 0f;
        isPaused = true;
        Debug.Log("[UIManager] \u6682\u505c\u6e38\u620f");
    }

    public void ResumeGame()
    {
        if (!isPaused) return;

        SetPanelActive(pausePanel, false);
        Time.timeScale = 1f;
        isPaused = false;
        Debug.Log("[UIManager] \u7ee7\u7eed\u6e38\u620f");
    }

    public void BackToStart()
    {
        ShowStartPanel();
    }

    private void OnNewGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.RestartChat();
            ResumeGame();
            Debug.Log("[UIManager] \u65b0\u5efa\u6e38\u620f\uff0c\u91cd\u7f6e\u5f53\u524d\u5bf9\u8bdd");
        }
    }

    private void OnSaveGameClicked()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.SaveProgress("manual_save");
            Debug.Log("[UIManager] \u6e38\u620f\u5df2\u4fdd\u5b58\u5230\u6570\u636e\u5e93");
        }
    }

    private async void OnSwitchCharacterCardClicked()
    {
        if (isSwitchingCharacterCard)
        {
            Debug.LogWarning("[UIManager] 角色卡切换流程正在进行，忽略重复点击");
            return;
        }

        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[UIManager] DialogueManager \u4e0d\u5b58\u5728\uff0c\u65e0\u6cd5\u5207\u6362\u89d2\u8272\u5361");
            return;
        }

        isSwitchingCharacterCard = true;
        try
        {
            bool confirmed = FileDialogHelper.ShowConfirmationDialog(
                "\u5207\u6362\u89d2\u8272\u5361",
                "\u6b64\u64cd\u4f5c\u5c06\u4e0d\u4f1a\u4fdd\u5b58\u73b0\u6709\u6570\u636e\uff01\n\u5207\u6362\u5b8c\u6210\u540e\uff0c\u5f53\u524d\u5bf9\u8bdd\u5c06\u4ece\u5934\u5f00\u59cb\u3002\n\n\u662f\u5426\u7ee7\u7eed\uff1f");
            if (!confirmed)
            {
                Debug.Log("[UIManager] \u7528\u6237\u53d6\u6d88\u5207\u6362\u89d2\u8272\u5361");
                return;
            }

            await Task.Yield();

            string initialDirectory = DialogueManager.Instance.GetCharacterCardSelectionDirectory();
            string cardPath;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            cardPath = await Task.Run(() => FileDialogHelper.ShowOpenFileDialog(
                "\u9009\u62e9\u89d2\u8272\u5361\u6587\u4ef6",
                "txt",
                initialDirectory));
#else
            cardPath = FileDialogHelper.ShowOpenFileDialog(
                "\u9009\u62e9\u89d2\u8272\u5361\u6587\u4ef6",
                "txt",
                initialDirectory);
#endif
            if (string.IsNullOrEmpty(cardPath))
            {
                Debug.Log("[UIManager] \u7528\u6237\u53d6\u6d88\u9009\u62e9\u89d2\u8272\u5361\u6587\u4ef6");
                return;
            }

            if (!DialogueManager.Instance.TrySwitchCharacterCardFromFile(cardPath))
            {
                FileDialogHelper.ShowMessageDialog(
                    "\u5207\u6362\u5931\u8d25",
                    "\u9009\u4e2d\u7684\u6587\u4ef6\u4e0d\u662f\u6709\u6548\u7684\u89d2\u8272\u5361\uff0c\u6216\u8bfb\u53d6\u5931\u8d25\u3002");
                return;
            }

            ResumeGame();
            Debug.Log($"[UIManager] \u5df2\u5207\u6362\u89d2\u8272\u5361\u5e76\u91cd\u7f6e\u5f53\u524d\u5bf9\u8bdd: {cardPath}");
        }
        finally
        {
            isSwitchingCharacterCard = false;
        }
    }

    public void QuitGame()
    {
        Debug.Log("[UIManager] \u9000\u51fa\u6e38\u620f");
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
    }

    public bool IsPaused()
    {
        return isPaused;
    }
}
