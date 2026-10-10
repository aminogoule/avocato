using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

/// Меню паузы с настройками. Esc / Start открывает и закрывает его, в настройках — шаг назад.
/// Заодно отвечает за курсор: в игре он захвачен, в меню и на экране конца игры — свободен.
[RequireComponent(typeof(UIDocument))]
public class PauseMenu : MonoBehaviour
{
    [Tooltip("Ассет действий: карта Player отключается на паузе, из неё же берутся клавиши для переназначения")]
    public InputActionAsset controls;
    [Tooltip("Менеджер игры — чтобы отпустить курсор на экране победы или поражения. Пусто — найдётся сам")]
    public GameBehavior gameManager;

    public static bool IsPaused { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsPaused = false;

    //какие клавиши можно переназначить: подпись, действие, часть составной привязки
    private static readonly (string label, string action, string part)[] Rebindable =
    {
        ("Вперёд", "Move", "up"),
        ("Назад", "Move", "down"),
        ("Влево", "Move", "left"),
        ("Вправо", "Move", "right"),
        ("Прыжок", "Jump", null),
        ("Выстрел", "Fire", null),
        ("Шаг (удерживать)", "Walk", null),
    };

    private static readonly int[] FpsOptions = { 0, 30, 60, 120, 144 };

    private InputActionMap _playerMap;
    private InputAction _pauseAction;
    private float _timeScaleBeforePause = 1f;
    private float _lockUntil;       //до этого момента настойчиво захватываем курсор
    private InputActionRebindingExtensions.RebindingOperation _rebind;
    private int _rebindEndedFrame = -1;

    private VisualElement _overlay;
    private VisualElement _pauseScreen;
    private VisualElement _settingsScreen;
    private Button _resumeButton;
    private Slider _mouseSensitivity;
    private Slider _stickSensitivity;
    private Toggle _invertY;
    private Slider _fov;
    private Slider _cameraDistance;
    private Toggle _fullScreen;
    private DropdownField _resolution;
    private DropdownField _quality;
    private Toggle _vSync;
    private DropdownField _fpsLimit;
    private SliderInt _masterVolume;
    private SliderInt _musicVolume;
    private readonly List<(Button button, InputAction action, int index)> _bindingButtons = new();
    private readonly List<Vector2Int> _resolutions = new();

    private static SettingsData S => GameSettings.Current;

    private bool GameOver => gameManager != null && (gameManager.showWinScreen || gameManager.ShowLoseScreen);

    private void Awake()
    {
        if (gameManager == null) gameManager = FindAnyObjectByType<GameBehavior>();
        if (controls == null)
        {
            Debug.LogError("PauseMenu: не назначен ассет действий (controls)", this);
            return;
        }
        _playerMap = controls.FindActionMap("Player", false);
        _pauseAction = controls.FindAction("UI/Pause", false);
        if (_pauseAction == null) Debug.LogError("PauseMenu: в ассете нет действия UI/Pause", this);

        //переназначенные клавиши живут в настройках, а не в ассете
        controls.RemoveAllBindingOverrides();
        if (!string.IsNullOrEmpty(S.bindingOverrides)) controls.LoadBindingOverridesFromJson(S.bindingOverrides);
    }

    //дерево интерфейса UIDocument собирает в своём OnEnable, поэтому берём его в Start
    private void Start()
    {
        _pauseAction?.Enable();
        BuildUi();
        HideAll();
        GameSettings.Changed += RefreshSettings;
        //в редакторе окно Game получает фокус не сразу — первые полсекунды пробуем захватить курсор снова
        _lockUntil = Time.unscaledTime + 0.5f;
    }

    private void OnDestroy()
    {
        GameSettings.Changed -= RefreshSettings;
        CancelRebind();
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = _timeScaleBeforePause;
        }
    }

    private void Update()
    {
        if (_pauseAction == null || !_pauseAction.WasPressedThisFrame()) return;
        //тот же Esc только что отменил переназначение клавиши — меню не трогаем
        if (_rebind != null || _rebindEndedFrame == Time.frameCount) return;

        if (!IsPaused)
        {
            if (!GameOver) Pause();
        }
        else if (!_settingsScreen.ClassListContains("hidden"))
        {
            ShowPauseScreen();
        }
        else
        {
            Resume();
        }
    }

    //курсор меняем после всех Update: тогда клик, который захватил курсор, игрок не примет за выстрел
    private void LateUpdate()
    {
        bool wantLocked = !IsPaused && !GameOver;
        if (!wantLocked)
        {
            if (Cursor.lockState != CursorLockMode.None) SetCursorLocked(false);
            return;
        }
        if (Cursor.lockState == CursorLockMode.Locked) return;
        bool clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (clicked || Time.unscaledTime < _lockUntil) SetCursorLocked(true);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused && !IsPaused) _lockUntil = Time.unscaledTime + 0.3f;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ---------- пауза ----------

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        _timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        //управление героем и камерой выключаем целиком: клик по кнопке меню не должен стрелять
        _playerMap?.Disable();
        SetCursorLocked(false);
        ShowPauseScreen();
    }

    public void Resume()
    {
        if (!IsPaused) return;
        CancelRebind();
        GameSettings.Save();
        HideAll();
        IsPaused = false;
        Time.timeScale = _timeScaleBeforePause;
        _playerMap?.Enable();
        _lockUntil = Time.unscaledTime + 0.3f;
    }

    private void Restart()
    {
        CancelRebind();
        GameSettings.Save();
        IsPaused = false;
        _playerMap?.Enable();
        Utilities.RestartLevel();
    }

    private void Quit()
    {
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void HideAll()
    {
        _overlay?.AddToClassList("hidden");
    }

    private void ShowPauseScreen()
    {
        CancelRebind();
        GameSettings.Save();
        _overlay.RemoveFromClassList("hidden");
        _pauseScreen.RemoveFromClassList("hidden");
        _settingsScreen.AddToClassList("hidden");
        _resumeButton.Focus();
    }

    private void ShowSettingsScreen()
    {
        RefreshSettings();
        _pauseScreen.AddToClassList("hidden");
        _settingsScreen.RemoveFromClassList("hidden");
        _settingsScreen.Q<Button>("back").Focus();
    }

    // ---------- интерфейс ----------

    private void BuildUi()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        _overlay = root.Q("overlay");
        _pauseScreen = root.Q("pause-screen");
        _settingsScreen = root.Q("settings-screen");

        _resumeButton = root.Q<Button>("resume");
        _resumeButton.clicked += Resume;
        root.Q<Button>("open-settings").clicked += ShowSettingsScreen;
        root.Q<Button>("restart").clicked += Restart;
        root.Q<Button>("quit").clicked += Quit;
        root.Q<Button>("back").clicked += ShowPauseScreen;
        root.Q<Button>("reset-all").clicked += GameSettings.ResetToDefaults;
        root.Q<Button>("reset-bindings").clicked += ResetBindings;

        _mouseSensitivity = BindSlider(root, "mouse-sensitivity", v => S.mouseSensitivity = v);
        _stickSensitivity = BindSlider(root, "stick-sensitivity", v => S.stickSensitivity = v);
        _invertY = BindToggle(root, "invert-y", v => S.invertY = v);
        _fov = BindSlider(root, "fov", v => S.fieldOfView = Mathf.Round(v));
        _cameraDistance = BindSlider(root, "camera-distance", v => S.cameraDistance = v);

        _fullScreen = BindToggle(root, "fullscreen", v => S.fullScreen = v ? 1 : 0);
        _resolution = root.Q<DropdownField>("resolution");
        _resolution.RegisterValueChangedCallback(_ =>
        {
            int i = _resolution.index;
            if (i < 0 || i >= _resolutions.Count) return;
            S.resolutionWidth = _resolutions[i].x;
            S.resolutionHeight = _resolutions[i].y;
            GameSettings.NotifyChanged();
        });
        //в редакторе разрешение и режим экрана задаёт окно Game
        bool editor = Application.isEditor;
        _fullScreen.SetEnabled(!editor);
        _resolution.SetEnabled(!editor);
        root.Q("editor-hint").style.display = editor ? DisplayStyle.Flex : DisplayStyle.None;

        _quality = root.Q<DropdownField>("quality");
        _quality.choices = QualitySettings.names.ToList();
        _quality.RegisterValueChangedCallback(_ =>
        {
            if (_quality.index < 0) return;
            S.qualityLevel = _quality.index;
            GameSettings.NotifyChanged();
        });
        _vSync = BindToggle(root, "vsync", v => S.vSync = v);
        _fpsLimit = root.Q<DropdownField>("fps-limit");
        _fpsLimit.choices = FpsOptions.Select(f => f == 0 ? "Без ограничения" : $"{f} кадров/с").ToList();
        _fpsLimit.RegisterValueChangedCallback(_ =>
        {
            if (_fpsLimit.index < 0) return;
            S.frameRateLimit = FpsOptions[_fpsLimit.index];
            GameSettings.NotifyChanged();
        });

        _masterVolume = BindSliderInt(root, "master-volume", v => S.masterVolume = v / 100f);
        _musicVolume = BindSliderInt(root, "music-volume", v => S.musicVolume = v / 100f);

        BuildBindingRows(root.Q("bindings"));
        RefreshSettings();
    }

    private static Slider BindSlider(VisualElement root, string name, System.Action<float> set)
    {
        var slider = root.Q<Slider>(name);
        slider.RegisterValueChangedCallback(e =>
        {
            set(e.newValue);
            GameSettings.NotifyChanged();
        });
        return slider;
    }

    private static SliderInt BindSliderInt(VisualElement root, string name, System.Action<int> set)
    {
        var slider = root.Q<SliderInt>(name);
        slider.RegisterValueChangedCallback(e =>
        {
            set(e.newValue);
            GameSettings.NotifyChanged();
        });
        return slider;
    }

    private static Toggle BindToggle(VisualElement root, string name, System.Action<bool> set)
    {
        var toggle = root.Q<Toggle>(name);
        toggle.RegisterValueChangedCallback(e =>
        {
            set(e.newValue);
            GameSettings.NotifyChanged();
        });
        return toggle;
    }

    /// Выставляет элементы по текущим настройкам, не вызывая их обработчики.
    private void RefreshSettings()
    {
        if (_mouseSensitivity == null) return;
        _mouseSensitivity.SetValueWithoutNotify(S.mouseSensitivity);
        _stickSensitivity.SetValueWithoutNotify(S.stickSensitivity);
        _invertY.SetValueWithoutNotify(S.invertY);
        _fov.SetValueWithoutNotify(S.fieldOfView);
        _cameraDistance.SetValueWithoutNotify(S.cameraDistance);

        _fullScreen.SetValueWithoutNotify(S.fullScreen < 0 ? Screen.fullScreen : S.fullScreen > 0);
        FillResolutions();

        _quality.SetValueWithoutNotify(QualitySettings.names[QualitySettings.GetQualityLevel()]);
        _vSync.SetValueWithoutNotify(S.vSync);
        int fps = System.Array.IndexOf(FpsOptions, S.frameRateLimit);
        _fpsLimit.SetValueWithoutNotify(_fpsLimit.choices[Mathf.Max(fps, 0)]);
        //при вертикальной синхронизации ограничение кадров не действует
        _fpsLimit.SetEnabled(!S.vSync);

        _masterVolume.SetValueWithoutNotify(Mathf.RoundToInt(S.masterVolume * 100f));
        _musicVolume.SetValueWithoutNotify(Mathf.RoundToInt(S.musicVolume * 100f));

        RefreshBindingLabels();
    }

    private void FillResolutions()
    {
        _resolutions.Clear();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!_resolutions.Contains(size)) _resolutions.Add(size);
        }
        var current = new Vector2Int(
            S.resolutionWidth > 0 ? S.resolutionWidth : Screen.width,
            S.resolutionHeight > 0 ? S.resolutionHeight : Screen.height);
        if (!_resolutions.Contains(current)) _resolutions.Add(current);
        _resolutions.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));

        _resolution.choices = _resolutions.Select(r => $"{r.x} × {r.y}").ToList();
        _resolution.SetValueWithoutNotify(_resolution.choices[_resolutions.IndexOf(current)]);
    }

    // ---------- переназначение клавиш ----------

    private void BuildBindingRows(VisualElement container)
    {
        _bindingButtons.Clear();
        container.Clear();
        if (_playerMap == null) return;

        foreach (var (label, actionName, part) in Rebindable)
        {
            InputAction action = _playerMap.FindAction(actionName, false);
            if (action == null) continue;
            int index = FindKeyboardBinding(action, part);
            if (index < 0) continue;

            var row = new VisualElement();
            row.AddToClassList("binding-row");
            var text = new Label(label);
            text.AddToClassList("binding-row__label");
            var button = new Button();
            button.AddToClassList("binding-row__button");
            button.clicked += () => StartRebind(button, action, index);
            row.Add(text);
            row.Add(button);
            container.Add(row);
            _bindingButtons.Add((button, action, index));
        }
        RefreshBindingLabels();
    }

    //первая привязка к клавиатуре или мыши; для движения — нужная сторона первого набора клавиш (WASD)
    private static int FindKeyboardBinding(InputAction action, string part)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            if (part == null)
            {
                if (bindings[i].isComposite || bindings[i].isPartOfComposite) continue;
                string path = bindings[i].path;
                if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>")) return i;
            }
            else if (bindings[i].isComposite)
            {
                for (int j = i + 1; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                {
                    if (bindings[j].name == part) return j;
                }
                return -1;
            }
        }
        return -1;
    }

    private void RefreshBindingLabels()
    {
        foreach (var (button, action, index) in _bindingButtons)
        {
            button.text = action.GetBindingDisplayString(index);
            button.RemoveFromClassList("binding-row__button--waiting");
        }
    }

    private void StartRebind(Button button, InputAction action, int index)
    {
        CancelRebind();
        button.text = "Нажмите клавишу…";
        button.AddToClassList("binding-row__button--waiting");

        //действие на паузе выключено — переназначать можно только выключенное
        _rebind = action.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.1f)
            .OnComplete(_ => FinishRebind(true))
            .OnCancel(_ => FinishRebind(false))
            .Start();
    }

    private void FinishRebind(bool changed)
    {
        _rebind?.Dispose();
        _rebind = null;
        _rebindEndedFrame = Time.frameCount;
        if (changed)
        {
            S.bindingOverrides = controls.SaveBindingOverridesAsJson();
            GameSettings.Save();
        }
        RefreshBindingLabels();
    }

    private void CancelRebind()
    {
        _rebind?.Cancel();
    }

    private void ResetBindings()
    {
        CancelRebind();
        controls.RemoveAllBindingOverrides();
        S.bindingOverrides = "";
        GameSettings.Save();
        RefreshBindingLabels();
    }
}
