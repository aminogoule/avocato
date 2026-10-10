using System;
using UnityEngine;

/// Всё, что игрок настраивает в меню. Значения по умолчанию — здесь.
[Serializable]
public class SettingsData
{
    [Header("Управление")]
    public float mouseSensitivity = 1f;     // множитель к базовой чувствительности камеры
    public float stickSensitivity = 1f;
    public bool invertY = false;
    public string bindingOverrides = "";    // переназначенные клавиши, формат Input System

    [Header("Камера")]
    public bool freeCamera = false;         // false — камера за спиной героя, true — крутится только мышью
    public float fieldOfView = 60f;
    public float cameraDistance = 1f;       // множитель к расстоянию из инспектора камеры

    [Header("Графика")]
    public int qualityLevel = -1;           // -1 — как в проекте
    public bool vSync = true;
    public int frameRateLimit = 0;          // 0 — без ограничения
    public int fullScreen = -1;             // -1 — как при запуске, 0 — в окне, 1 — во весь экран
    public int resolutionWidth = 0;         // 0 — как при запуске
    public int resolutionHeight = 0;

    [Header("Звук")]
    public float masterVolume = 1f;
    public float musicVolume = 0.8f;
}

/// Хранит настройки в PlayerPrefs и применяет то, что относится ко всей игре:
/// громкость, качество, частоту кадров, экран. Остальное подхватывают компоненты
/// через событие Changed.
public static class GameSettings
{
    private const string PrefsKey = "avocato.settings";

    private static SettingsData _current;
    private static int _projectQualityLevel = -1;

    public static event Action Changed;

    public static SettingsData Current
    {
        get
        {
            if (_current == null) Load();
            return _current;
        }
    }

    //статические поля переживают перезапуск Play Mode, если перезагрузка домена выключена
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _current = null;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        _projectQualityLevel = QualitySettings.GetQualityLevel();
        Load();
        ApplyGlobal();
    }

    public static void Load()
    {
        _current = null;
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _current = JsonUtility.FromJson<SettingsData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GameSettings: сохранённые настройки не читаются, беру значения по умолчанию ({e.Message})");
            }
        }
        _current ??= new SettingsData();
    }

    public static void Save()
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(Current));
        PlayerPrefs.Save();
    }

    /// Вернуть значения по умолчанию. Переназначенные клавиши сбрасываются отдельно.
    public static void ResetToDefaults()
    {
        string bindings = Current.bindingOverrides;
        _current = new SettingsData { bindingOverrides = bindings };
        NotifyChanged();
    }

    /// Вызывать после любого изменения Current.
    public static void NotifyChanged()
    {
        ApplyGlobal();
        Changed?.Invoke();
    }

    private static void ApplyGlobal()
    {
        SettingsData s = Current;
        AudioListener.volume = Mathf.Clamp01(s.masterVolume);

        //уровень качества меняет и vSync, поэтому vSync ставим после
        int quality = s.qualityLevel >= 0 ? s.qualityLevel : _projectQualityLevel;
        if (quality >= 0 && quality < QualitySettings.names.Length && quality != QualitySettings.GetQualityLevel())
        {
            QualitySettings.SetQualityLevel(quality, true);
        }
        QualitySettings.vSyncCount = s.vSync ? 1 : 0;
        Application.targetFrameRate = s.frameRateLimit > 0 ? s.frameRateLimit : -1;

        //в редакторе размер задаёт окно Game, менять его отсюда бессмысленно
        if (Application.isEditor) return;
        FullScreenMode mode = s.fullScreen < 0 ? Screen.fullScreenMode
            : s.fullScreen > 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        int width = s.resolutionWidth > 0 ? s.resolutionWidth : Screen.width;
        int height = s.resolutionHeight > 0 ? s.resolutionHeight : Screen.height;
        if (width != Screen.width || height != Screen.height || mode != Screen.fullScreenMode)
        {
            Screen.SetResolution(width, height, mode);
        }
    }
}
