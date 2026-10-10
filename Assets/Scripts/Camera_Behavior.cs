using UnityEngine;
using UnityEngine.InputSystem;

/// Камера от третьего лица. По умолчанию держится за спиной игрока и поворачивается вместе с ним,
/// мышью или правым стиком её можно временно отвести — потом она сама вернётся за спину.
/// В настройках игрок может включить свободную камеру: тогда она крутится только мышью.
[ExecuteAlways]
public class Camera_Behavior : MonoBehaviour
{
    [Header("Цель")]
    [Tooltip("За кем следует камера. Если пусто — берётся объект с тегом Player.")]
    [SerializeField] private Transform target;

    [Header("Положение камеры")]
    [Tooltip("Расстояние от камеры до точки опоры над игроком")]
    [Range(0.5f, 15f)] public float distance = 3.2f;
    [Tooltip("Наклон камеры при старте, градусов: 0 — на уровне точки опоры, больше — сверху")]
    [Range(-80f, 85f)] public float pitch = 25f;
    [Tooltip("Сдвиг вбок: минус — влево от игрока, плюс — вправо. Ноль — строго по центру")]
    [Range(-5f, 5f)] public float sideOffset = 0f;

    [Header("Точка взгляда")]
    [Tooltip("Высота точки опоры, вокруг которой вращается камера. У модели пивот на уровне ног, поэтому ноль смотрит в ступни")]
    [Range(0f, 3f)] public float lookAtHeight = 1f;
    [Tooltip("Сдвиг точки взгляда вперёд по направлению камеры — даёт больше обзора впереди")]
    [Range(-5f, 5f)] public float lookAtForward = 0f;

    [Header("Управление камерой")]
    [Tooltip("Ассет действий с действием Player/Look. Пусто — камеру не покрутить")]
    public InputActionAsset controls;
    [Tooltip("Базовая чувствительность мыши, градусов на пиксель. Игрок умножает её в меню настроек")]
    [Range(0.01f, 1f)] public float mouseSensitivity = 0.12f;
    [Tooltip("Базовая скорость поворота стиком, градусов в секунду. Игрок умножает её в меню настроек")]
    [Range(30f, 720f)] public float stickSpeed = 180f;
    [Tooltip("Нижний и верхний предел наклона, градусов")]
    public Vector2 pitchLimits = new(-20f, 70f);

    [Header("Камера за спиной")]
    [Tooltip("Через сколько секунд после последнего движения мышью камера начинает возвращаться за спину")]
    [Range(0f, 5f)] public float recenterDelay = 1f;
    [Tooltip("Плавность возврата за спину, секунд: меньше — быстрее")]
    [Range(0.05f, 2f)] public float recenterSmoothing = 0.35f;

    [Header("Поведение")]
    [Tooltip("Сглаживание в секундах: 0 — камера приклеена жёстко, больше — догоняет плавно. Поворот не сглаживается")]
    [Range(0f, 1f)] public float followSmoothing = 0f;
    [Tooltip("Не давать камере уходить в стены")]
    public bool avoidObstacles = true;
    [Tooltip("Слои, которые загораживают камеру")]
    public LayerMask obstacleLayers = ~0;
    [Tooltip("Радиус камеры при проверке стен")]
    [Range(0.05f, 1f)] public float collisionRadius = 0.25f;
    [Tooltip("Двигать камеру прямо в редакторе, без запуска игры — удобно подбирать значения")]
    public bool previewInEditMode = true;

    private InputAction _lookAction;
    private float _yaw;
    private float _yawOffset;       //насколько камера отведена от спины игрока (режим «за спиной»)
    private float _pitch;
    private float _lastLookTime = -999f;
    private bool _wasFree;
    private Vector3 _pivot;
    private float _currentDistance;
    private bool _initialized;
    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private Camera _camera;

    //расстояние с учётом настройки игрока; в редакторе — как в инспекторе
    private float Distance => Application.isPlaying ? distance * GameSettings.Current.cameraDistance : distance;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        _camera = GetComponent<Camera>();
        GameSettings.Changed += ApplySettings;
        ApplySettings();
        if (controls == null) return;
        _lookAction = controls.FindAction("Player/Look", false);
        _lookAction?.Enable();
    }

    private void OnDisable()
    {
        if (Application.isPlaying) GameSettings.Changed -= ApplySettings;
    }

    private void ApplySettings()
    {
        if (_camera != null) _camera.fieldOfView = GameSettings.Current.fieldOfView;
    }

    private void Start()
    {
        ResolveTarget();
        if (Application.isPlaying && target == null)
        {
            Debug.LogError("Camera_Behavior: цель не задана и объект с тегом Player не найден", this);
        }
        if (Application.isPlaying && _lookAction == null)
        {
            Debug.LogWarning("Camera_Behavior: нет действия Player/Look — камера не будет вращаться", this);
        }
    }

    private void ResolveTarget()
    {
        if (target != null) return;
        //ищем по тегу, а не по имени объекта
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.transform;
    }

    private static bool FreeCamera => Application.isPlaying && GameSettings.Current.freeCamera;

    //стартуем за спиной игрока
    private void Init()
    {
        _yaw = target.eulerAngles.y;
        _yawOffset = 0f;
        _wasFree = FreeCamera;
        _pitch = pitch;
        _pivot = PivotTarget();
        _currentDistance = Distance;
        _initialized = true;
    }

    //не путать с LastUpdate
    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            if (!previewInEditMode) return;
            ResolveTarget();
            if (target == null) return;
            Init();   //в редакторе всегда показываем стартовый вид
            Apply(false);
            return;
        }

        ResolveTarget();
        if (target == null) return;
        if (!_initialized) Init();
        ReadLook();
        UpdateYaw();
        Apply(followSmoothing > 0f);
    }

    private void ReadLook()
    {
        if (_lookAction == null) return;
        Vector2 look = _lookAction.ReadValue<Vector2>();
        if (look == Vector2.zero) return;

        //мышь отдаёт смещение за кадр в пикселях, стик — наклон; второе умножаем на время
        bool pointer = _lookAction.activeControl != null && _lookAction.activeControl.device is Pointer;
        if (pointer)
        {
            //пока курсор свободен, мышь крутит не камеру, а курсор
            if (Cursor.lockState != CursorLockMode.Locked) return;
            look *= mouseSensitivity * GameSettings.Current.mouseSensitivity;
        }
        else
        {
            look *= stickSpeed * GameSettings.Current.stickSensitivity * Time.deltaTime;
        }

        if (FreeCamera) _yaw = Mathf.Repeat(_yaw + look.x, 360f);
        else _yawOffset = Mathf.Clamp(_yawOffset + look.x, -180f, 180f);
        bool invertY = GameSettings.Current.invertY;
        _pitch = Mathf.Clamp(_pitch + (invertY ? look.y : -look.y), pitchLimits.x, pitchLimits.y);
        _lastLookTime = Time.unscaledTime;
    }

    private void UpdateYaw()
    {
        bool free = FreeCamera;
        //при переключении режима камера остаётся там, где была, без рывка
        if (free != _wasFree)
        {
            if (!free) _yawOffset = Mathf.DeltaAngle(target.eulerAngles.y, _yaw);
            _wasFree = free;
        }
        if (free) return;

        //за спиной: камера поворачивается вместе с игроком, а отведённая мышью — со временем возвращается
        if (Time.unscaledTime - _lastLookTime > recenterDelay)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / recenterSmoothing);
            _yawOffset = Mathf.Lerp(_yawOffset, 0f, k);
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y), k);
        }
        _yaw = Mathf.Repeat(target.eulerAngles.y + _yawOffset, 360f);
    }

    private Vector3 PivotTarget() => target.position + Vector3.up * lookAtHeight;

    private void Apply(bool smooth)
    {
        //сглаживаем только точку опоры: поворот от мыши должен откликаться сразу
        Vector3 pivotTarget = PivotTarget();
        if (smooth)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / followSmoothing);
            _pivot = Vector3.Lerp(_pivot, pivotTarget, k);
        }
        else
        {
            _pivot = pivotTarget;
        }

        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 side = rotation * Vector3.right * sideOffset;
        Vector3 back = rotation * Vector3.back;

        //в стену камера заходит сразу, а обратно отъезжает плавно, чтобы не дёргалась
        float wanted = Distance;
        if (avoidObstacles && Application.isPlaying) wanted = Mathf.Min(wanted, FreeDistance(_pivot + side, back, wanted));
        _currentDistance = wanted < _currentDistance || !Application.isPlaying
            ? wanted
            : Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-Time.deltaTime / 0.2f));

        transform.position = _pivot + side + back * _currentDistance;
        transform.LookAt(LookPoint());
    }

    //насколько далеко можно отъехать от точки опоры, не упершись в препятствие
    private float FreeDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, _hits, maxDistance,
            obstacleLayers, QueryTriggerInteraction.Ignore);
        float free = maxDistance;
        for (int i = 0; i < count; i++)
        {
            //сам игрок камеру не загораживает
            if (_hits[i].collider.transform.IsChildOf(target)) continue;
            //сфера стартовала внутри коллайдера — точки удара нет, прижимаемся к опоре
            float d = _hits[i].distance > 0f ? _hits[i].distance : 0f;
            free = Mathf.Min(free, d);
        }
        return Mathf.Max(free, 0.1f);
    }

    private Vector3 LookPoint()
    {
        Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        return _pivot + forward * lookAtForward;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, LookPoint());
        Gizmos.DrawWireSphere(LookPoint(), 0.12f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(PivotTarget(), 0.15f);
    }

    [ContextMenu("Сбросить положение камеры")]
    private void ResetCamera()
    {
        distance = 3.2f;
        pitch = 25f;
        sideOffset = 0f;
        lookAtHeight = 1f;
        lookAtForward = 0f;
        followSmoothing = 0f;
    }
}
