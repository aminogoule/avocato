using UnityEngine;

[ExecuteAlways]
public class Camera_Behavior : MonoBehaviour
{
    [Header("Цель")]
    [Tooltip("За кем следует камера. Если пусто — берётся объект с тегом Player.")]
    [SerializeField] private Transform target;

    [Header("Положение камеры")]
    [Tooltip("Насколько камера отстаёт от игрока")]
    [Range(0.5f, 15f)] public float distance = 2.6f;
    [Tooltip("Высота камеры над точкой опоры игрока")]
    [Range(0f, 10f)] public float height = 1.3f;
    [Tooltip("Сдвиг вбок: минус — влево от игрока, плюс — вправо. Ноль — строго по центру")]
    [Range(-5f, 5f)] public float sideOffset = 0f;

    [Header("Точка взгляда")]
    [Tooltip("На какой высоте камера держит прицел. У модели пивот на уровне ног, поэтому ноль смотрит в ступни")]
    [Range(0f, 3f)] public float lookAtHeight = 1f;
    [Tooltip("Сдвиг точки взгляда вперёд по направлению игрока — даёт больше обзора по ходу движения")]
    [Range(-5f, 5f)] public float lookAtForward = 0f;

    [Header("Поведение")]
    [Tooltip("Включено — камера разворачивается вместе с игроком. Выключено — висит с одной стороны мира")]
    public bool rotateWithTarget = true;
    [Tooltip("Сглаживание в секундах: 0 — камера приклеена жёстко, больше — догоняет плавно")]
    [Range(0f, 1f)] public float followSmoothing = 0f;
    [Tooltip("Двигать камеру прямо в редакторе, без запуска игры — удобно подбирать значения")]
    public bool previewInEditMode = true;

    // то, чем это было раньше: одно смещение в локальных осях игрока
    public Vector3 CamOffset => new(sideOffset, height, -distance);

    private void Start()
    {
        ResolveTarget();
        if (Application.isPlaying && target == null)
        {
            Debug.LogError("Camera_Behavior: цель не задана и объект с тегом Player не найден", this);
        }
    }

    private void ResolveTarget()
    {
        if (target != null) return;
        //ищем по тегу, а не по имени объекта
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.transform;
    }

    //не путать с LastUpdate
    private void LateUpdate()
    {
        if (!Application.isPlaying && !previewInEditMode) return;
        Follow(Application.isPlaying && followSmoothing > 0f);
    }

    /// Ставит камеру в рассчитанную позицию. smooth=false — мгновенно.
    public void Follow(bool smooth)
    {
        ResolveTarget();
        if (target == null) return;

        Vector3 desired = rotateWithTarget
            ? target.TransformPoint(CamOffset)
            : target.position + CamOffset;

        //сглаживание имеет смысл только в игре: в редакторе deltaTime не настоящий
        if (smooth)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / followSmoothing);
            transform.position = Vector3.Lerp(transform.position, desired, k);
        }
        else
        {
            transform.position = desired;
        }
        transform.LookAt(LookPoint());
    }

    private Vector3 LookPoint()
    {
        Vector3 forward = rotateWithTarget ? target.forward : Vector3.forward;
        return target.position + Vector3.up * lookAtHeight + forward * lookAtForward;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Vector3 look = LookPoint();
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, look);
        Gizmos.DrawWireSphere(look, 0.12f);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(rotateWithTarget ? target.TransformPoint(CamOffset) : target.position + CamOffset, 0.15f);
    }

    [ContextMenu("Сбросить положение камеры")]
    private void ResetCamera()
    {
        distance = 2.6f;
        height = 1.3f;
        sideOffset = 0f;
        lookAtHeight = 1f;
        lookAtForward = 0f;
        rotateWithTarget = true;
        followSmoothing = 0f;
    }
}
