using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Behavior : MonoBehaviour
{
    [Header("Движение")]
    [Tooltip("Скорость бега, м/с")]
    public float moveSpeed = 10f;
    [Tooltip("Доля скорости бега при зажатом шаге (Left Shift)")]
    [Range(0.1f, 1f)] public float walkSpeedFactor = 0.45f;
    [Tooltip("Скорость поворота на A/D, градусов в секунду")]
    public float rotationSpeed = 75f;
    [Tooltip("Сглаживание ввода, единиц в секунду: чем меньше, тем плавнее и медленнее разгон и остановка; 0 — мгновенно")]
    [Range(0f, 20f)] public float inputSmoothing = 3.5f;
    private Rigidbody _rb;

    [Header("Прыжок")]
    [Tooltip("Начальная скорость прыжка вверх, м/с")]
    public float jumpVelocity = 5f;
    [Tooltip("Во сколько раз сильнее гравитация на спуске — прыжок перестаёт быть «лунным»")]
    [Range(1f, 4f)] public float fallGravityMultiplier = 2f;
    [Tooltip("Гравитация на подъёме, если кнопку прыжка отпустили: короткое нажатие — низкий прыжок")]
    [Range(1f, 4f)] public float lowJumpGravityMultiplier = 2f;
    [Tooltip("Сколько секунд после схода с края ещё можно прыгнуть")]
    public float coyoteTime = 0.1f;
    [Tooltip("Сколько секунд помнится нажатие прыжка до приземления")]
    public float jumpBufferTime = 0.12f;
    public float distanceToGround = 0.1f;
    public LayerMask groundLayer;
    private CapsuleCollider _col;
    //add bullet object
    public GameObject bullet;
    public float bulletSpeed = 100f;    //bullet start speed ignore gravity force
    public float bulletSpawnOffset = 1f; //spawn distance in front of the player
    public float damageCooldown = 1f; //invulnerability window after a hit
    public GameBehavior _gameManager; //ref to game manager

    [Header("Ввод")]
    [Tooltip("Ассет действий. Пусто — управление работать не будет")]
    public InputActionAsset controls;

    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _fireAction;
    private InputAction _walkAction;
    private Vector2 _move;          // сглаженный ввод, используется и для движения, и для анимации
    private float vInput;           // скорость вперёд/назад, м/с
    private float hInput;           // скорость поворота, градусов в секунду
    private bool _jumpHeld;
    private float _yaw;             // собственный угол поворота, физика на него не влияет
    private float _lastGroundedTime = -999f;
    private float _jumpPressedTime = -999f;
    private float _jumpLockUntil;   // сразу после отрыва земля ещё рядом — не считаем себя стоящим

    [Header("Анимация")]
    [Tooltip("Аниматор модели. Пусто — будет найден в дочерних объектах")]
    public Animator animator;
    [Tooltip("Ниже этого значения параметра Speed персонаж стоит")]
    [Range(0f, 1f)] public float idleThreshold = 0.1f;
    [Tooltip("Насколько быстро параметр Speed догоняет ввод")]
    [Range(1f, 30f)] public float animSpeedDamping = 12f;
    [Tooltip("Сколько держится слой стрельбы после выстрела")]
    public float fireLayerHold = 0.55f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int GroundedHash = Animator.StringToHash("Grounded");
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private int _fireLayer = -1;
    private float _fireWeight;

    private float _animSpeed;

    //input is read in Update, applied in FixedUpdate
    private bool _fireRequested;
    private float _lastDamageTime = -999f;
    // в редакторе незабыть заморозить вращение по всем осям

    void Awake()
    {
        if (controls == null)
        {
            Debug.LogError("Player_Behavior: не назначен ассет действий (controls)", this);
            return;
        }
        var map = controls.FindActionMap("Player", true);
        _moveAction = map.FindAction("Move", true);
        _jumpAction = map.FindAction("Jump", true);
        _fireAction = map.FindAction("Fire", true);
        _walkAction = map.FindAction("Walk", false);
    }

    void OnEnable()
    {
        if (controls != null) controls.FindActionMap("Player", true).Enable();
    }

    void OnDisable()
    {
        if (controls != null) controls.FindActionMap("Player", true).Disable();
    }

    void Start()
    {
        //инициализация физики
        _rb = GetComponent<Rigidbody>();
        _yaw = transform.eulerAngles.y;   // стартуем с того, как персонаж поставлен в сцене
        //get player capsule collider
        _col = GetComponent<CapsuleCollider>();
        //скорость задаём сами каждый шаг физики, и трение о пол и стены только мешает:
        //тормозит бег и заставляет «прилипать» к стене в прыжке
        if (_col.sharedMaterial == null)
        {
            _col.material = new PhysicsMaterial("PlayerNoFriction")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
        }
        if (_gameManager == null)
        {
            //get ref to game manager
            _gameManager = FindAnyObjectByType<GameBehavior>();
        }
        if (_gameManager == null)
        {
            Debug.LogError("GameBehavior not found on the scene", this);
        }
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        if (animator != null)
        {
            _fireLayer = animator.GetLayerIndex("Fire");
            if (_fireLayer >= 0) animator.SetLayerWeight(_fireLayer, 0f);
        }
        else
        {
            Debug.LogWarning("Animator не найден — анимации работать не будут", this);
        }
    }
    void Update()
    {
        if (_moveAction == null) return;

        //танковая схема: W/S — вперёд и назад по направлению героя, A/D — поворот на месте.
        //Клавиатура даёт сразу -1/0/1, поэтому доводим значение плавно
        Vector2 raw = _moveAction.ReadValue<Vector2>();
        _move = inputSmoothing > 0f
            ? Vector2.MoveTowards(_move, raw, inputSmoothing * Time.deltaTime)
            : raw;
        float speed = moveSpeed;
        if (_walkAction != null && _walkAction.IsPressed()) speed *= walkSpeedFactor;
        vInput = _move.y * speed;
        hInput = _move.x * rotationSpeed;

        //нажатия живут один кадр, поэтому читаем их здесь,
        //а применяем в FixedUpdate, иначе они теряются или дублируются
        if (_jumpAction.WasPressedThisFrame())
        {
            _jumpPressedTime = Time.time;
        }
        _jumpHeld = _jumpAction.IsPressed();
        if (_fireAction.WasPressedThisFrame() && !ClickOnlyGrabsCursor())
        {
            _fireRequested = true;
        }
        UpdateAnimator();
    }

    //пока курсор не захвачен, клик мышью уходит на захват курсора, а не на выстрел
    private bool ClickOnlyGrabsCursor()
    {
        return _fireAction.activeControl != null
            && _fireAction.activeControl.device is Mouse
            && Cursor.lockState != CursorLockMode.Locked;
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        //анимацию ведём по настоящей скорости, а не по кнопкам: упёрся в стену — стоит.
        //Поворот на месте тоже читается как движение, иначе персонаж крутится в позе покоя
        Vector3 v = _rb.linearVelocity;
        float move = new Vector2(v.x, v.z).magnitude / Mathf.Max(moveSpeed, 0.01f);
        float wanted = Mathf.Max(move, Mathf.Abs(_move.x) * 0.55f);
        if (wanted < idleThreshold) wanted = 0f;
        _animSpeed = Mathf.MoveTowards(_animSpeed, wanted, animSpeedDamping * Time.deltaTime);
        animator.SetFloat(SpeedHash, _animSpeed);
        animator.SetBool(GroundedHash, IsGrounded());

        //слой стрельбы поднимается на время выстрела и плавно гаснет, чтобы можно было стрелять на бегу
        if (_fireLayer >= 0)
        {
            _fireWeight = Mathf.MoveTowards(_fireWeight, 0f, Time.deltaTime / Mathf.Max(fireLayerHold, 0.01f));
            animator.SetLayerWeight(_fireLayer, _fireWeight);
        }
    }
    void FixedUpdate() //use this for Physics purpose only
    {
        bool grounded = Time.time >= _jumpLockUntil && IsGrounded();
        if (grounded) _lastGroundedTime = Time.time;

        //Поворот ведём собственным углом. Если брать за основу _rb.rotation, то любое касание
        //коллайдера закручивает тело вокруг Y (эта ось не заморожена), и разворот прилипает
        _yaw = Mathf.Repeat(_yaw + hInput * Time.fixedDeltaTime, 360f);
        _rb.angularVelocity = Vector3.zero;   //гасим закрутку, полученную от столкновений
        Quaternion facing = Quaternion.Euler(0f, _yaw, 0f);
        _rb.MoveRotation(facing);

        //скорость по земле задаём напрямую, плавность уже в сглаженном вводе; вертикаль оставляем физике.
        //Двигаем через скорость, а не MovePosition, — тогда солвер честно скользит вдоль стен
        Vector3 v = _rb.linearVelocity;
        Vector3 planar = facing * Vector3.forward * vInput;

        //прыжок с запасом по времени в обе стороны: нажал чуть раньше приземления
        //или чуть позже схода с края — всё равно прыгает
        bool buffered = Time.time - _jumpPressedTime <= jumpBufferTime;
        bool coyote = Time.time - _lastGroundedTime <= coyoteTime;
        if (buffered && coyote)
        {
            v.y = jumpVelocity;
            _jumpPressedTime = -999f;
            _lastGroundedTime = -999f;
            _jumpLockUntil = Time.time + 0.15f;
        }
        else if (!grounded)
        {
            //дополнительная гравитация: быстрее падаем и ниже прыгаем при коротком нажатии
            float extra = 0f;
            if (v.y < 0f) extra = fallGravityMultiplier - 1f;
            else if (!_jumpHeld) extra = lowJumpGravityMultiplier - 1f;
            v.y += Physics.gravity.y * extra * Time.fixedDeltaTime;
        }
        _rb.linearVelocity = new Vector3(planar.x, v.y, planar.z);

        if (_fireRequested)
        {
            _fireRequested = false;
            Shoot();
        }
    }
    private void Shoot()
    {
        if (bullet == null)
        {
            Debug.LogWarning("Bullet prefab is not assigned", this);
            return;
        }
        //спавним перед игроком с учётом его поворота, иначе пуля рождается внутри коллайдера.
        //Позу берём из физики: при интерполяции transform отстаёт от тела
        Quaternion facing = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 forward = facing * Vector3.forward;
        Vector3 spawnPoint = _rb.position + forward * bulletSpawnOffset;
        if (animator != null)
        {
            animator.SetTrigger(FireHash);
            if (_fireLayer >= 0)
            {
                _fireWeight = 1f;
                animator.SetLayerWeight(_fireLayer, 1f);
            }
        }
        GameObject newBullet = Instantiate(bullet, spawnPoint, facing);
        if (newBullet.TryGetComponent(out Rigidbody bulletRB))
        {
            bulletRB.linearVelocity = forward * bulletSpeed;
        }
    }
    //Get info about is player grounded or not
    private bool IsGrounded()
    {
        //get center botton of capsule
        Vector3 capsuleBottom = new Vector3(_col.bounds.center.x, _col.bounds.min.y, _col.bounds.center.z);
        //check interaction with ground layer
        bool grounded = Physics.CheckCapsule(_col.bounds.center, capsuleBottom, distanceToGround, groundLayer, QueryTriggerInteraction.Ignore);

        return grounded;
    }
    void OnCollisionEnter(Collision _collision)
    {
        //check if collide with enemy
        if (!_collision.gameObject.CompareTag("Enemy"))
        {
            return;
        }
        //контакт может повторяться несколько раз подряд, поэтому держим паузу между уронами
        if (Time.time - _lastDamageTime < damageCooldown)
        {
            return;
        }
        _lastDamageTime = Time.time;
        if (_gameManager != null)
        {
            _gameManager.HP -= 1; //reduce HP
        }
    }
}
