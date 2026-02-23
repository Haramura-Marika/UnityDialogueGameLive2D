using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Security.Cryptography;
using System.Text;

public class LoginUIManager : MonoBehaviour
{
    public static LoginUIManager Instance { get; private set; }

    [Header("输入框")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;

    [Header("按钮")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button backButton;

    [Header("提示信息")]
    [SerializeField] private TMP_Text messageText;

    private bool _initialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        if (!_initialized)
        {
            if (loginButton != null)
                loginButton.onClick.AddListener(OnLoginClicked);

            if (registerButton != null)
                registerButton.onClick.AddListener(OnRegisterClicked);

            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);

            _initialized = true;
        }

        if (messageText != null)
            messageText.text = string.Empty;
    }

    private void OnLoginClicked()
    {
        string username = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
        string password = passwordInput != null ? passwordInput.text : string.Empty;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowMessage("用户名和密码不能为空", Color.red);
            return;
        }

        if (!DatabaseManager.Instance.UserExists(username))
        {
            ShowMessage("用户不存在，请先注册", Color.red);
            return;
        }

        string inputPasswordHash = HashPassword(password);

        if (!DatabaseManager.Instance.ValidateUser(username, inputPasswordHash))
        {
            ShowMessage("密码错误", Color.red);
            return;
        }

        ShowMessage("登录成功！", Color.green);
        Debug.Log($"[LoginUIManager] 用户 {username} 登录成功");

        PlayerPrefs.SetString("current_user", username);
        PlayerPrefs.Save();

        // 清空输入框
        if (usernameInput != null) usernameInput.text = string.Empty;
        if (passwordInput != null) passwordInput.text = string.Empty;

        // 延迟进入游戏，让用户看到提示
        Invoke(nameof(EnterGame), 1f);
    }

    private void OnRegisterClicked()
    {
        string username = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
        string password = passwordInput != null ? passwordInput.text : string.Empty;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowMessage("用户名和密码不能为空", Color.red);
            return;
        }

        string hashedPassword = HashPassword(password);

        if (!DatabaseManager.Instance.RegisterUser(username, hashedPassword))
        {
            ShowMessage("用户名已存在", Color.red);
            return;
        }

        ShowMessage("注册成功！请登录", Color.green);
        Debug.Log($"[LoginUIManager] 用户 {username} 注册成功");
    }

    private void OnBackClicked()
    {
        // 返回开始面板
        if (messageText != null) messageText.text = string.Empty;
        if (usernameInput != null) usernameInput.text = string.Empty;
        if (passwordInput != null) passwordInput.text = string.Empty;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowStartPanel();
        }
    }

    private void EnterGame()
    {
        if (messageText != null) messageText.text = string.Empty;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.EnterGame();
        }
    }

    private void ShowMessage(string message, Color color)
    {
        if (messageText != null)
        {
            messageText.text = message;
            messageText.color = color;
        }
    }

    private string HashPassword(string password)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}