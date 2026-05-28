using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-10000)]
public sealed class AspectRatioLetterboxController : MonoBehaviour
{
    private const float TargetAspect = 16f / 9f;
    private const float BackgroundCameraDepth = -1000f;
    private const float CanvasPlaneDistance = 100f;

    private static AspectRatioLetterboxController _instance;

    private Camera _backgroundCamera;
    private Camera _mainSceneCamera;
    private int _lastScreenWidth = -1;
    private int _lastScreenHeight = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null || Object.FindFirstObjectByType<AspectRatioLetterboxController>() != null)
        {
            return;
        }

        var go = new GameObject(nameof(AspectRatioLetterboxController));
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<AspectRatioLetterboxController>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshAspectTargets();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    private void LateUpdate()
    {
        if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height || _mainSceneCamera != GetMainSceneCamera())
        {
            RefreshAspectTargets();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshAspectTargets();
    }

    private void RefreshAspectTargets()
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;

        EnsureBackgroundCamera();
        _mainSceneCamera = GetMainSceneCamera();

        ApplyViewportRectToSceneCameras();
        ConfigureRootCanvases();
    }

    private void EnsureBackgroundCamera()
    {
        if (_backgroundCamera != null)
        {
            return;
        }

        var go = new GameObject("AspectRatioBackgroundCamera");
        go.transform.SetParent(transform, false);

        _backgroundCamera = go.AddComponent<Camera>();
        _backgroundCamera.depth = BackgroundCameraDepth;
        _backgroundCamera.cullingMask = 0;
        _backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
        _backgroundCamera.backgroundColor = Color.black;
        _backgroundCamera.orthographic = true;
        _backgroundCamera.rect = new Rect(0f, 0f, 1f, 1f);
    }

    private Camera GetMainSceneCamera()
    {
        var taggedMainCamera = Camera.main;
        if (taggedMainCamera != null && taggedMainCamera != _backgroundCamera)
        {
            return taggedMainCamera;
        }

        var allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var camera in allCameras)
        {
            if (camera == null || camera == _backgroundCamera)
            {
                continue;
            }

            if (!camera.enabled || camera.targetTexture != null || camera.targetDisplay != 0)
            {
                continue;
            }

            return camera;
        }

        return null;
    }

    private void ApplyViewportRectToSceneCameras()
    {
        Rect targetRect = CalculateViewportRect();
        var allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var camera in allCameras)
        {
            if (camera == null || camera == _backgroundCamera)
            {
                continue;
            }

            if (!camera.enabled || camera.targetTexture != null || camera.targetDisplay != 0)
            {
                continue;
            }

            camera.rect = targetRect;
        }
    }

    private void ConfigureRootCanvases()
    {
        if (_mainSceneCamera == null)
        {
            return;
        }

        var allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var canvas in allCanvases)
        {
            if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace || canvas.targetDisplay != 0)
            {
                continue;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _mainSceneCamera;
            canvas.planeDistance = CanvasPlaneDistance;
        }
    }

    private static Rect CalculateViewportRect()
    {
        if (Screen.height <= 0 || Screen.width <= 0)
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        float windowAspect = (float)Screen.width / Screen.height;
        if (windowAspect > TargetAspect)
        {
            float width = TargetAspect / windowAspect;
            float x = (1f - width) * 0.5f;
            return new Rect(x, 0f, width, 1f);
        }

        float height = windowAspect / TargetAspect;
        float y = (1f - height) * 0.5f;
        return new Rect(0f, y, 1f, height);
    }
}
