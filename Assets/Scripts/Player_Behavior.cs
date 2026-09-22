using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Behavior : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float rotationSpeed = 75f;
    private float vInput;
    private float hInput;
    private Rigidbody _rb;
    public float jumpVelocity = 5f;
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
    [Tooltip("Сглаживание ввода, единиц в секунду. Повторяет плавность старого GetAxis; 0 — мгновенно")]
    [Range(0f, 20f)] public float inputSmoothing = 3.5f;

    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _fireAction;
    private Vector2 _move;      // сглаженный ввод, используется и для движения, и для анимации
    private float _yaw;         // собственный угол поворота, физика на него не влияет

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
    private bool _jumpRequested;
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

        //клавиатура даёт сразу -1/0/1, поэтому доводим значение плавно — иначе персонаж
        //дёргается и сразу проскакивает из покоя в бег, минуя ходьбу
        Vector2 raw = _moveAction.ReadValue<Vector2>();
        _move = inputSmoothing > 0f
            ? Vector2.MoveTowards(_move, raw, inputSmoothing * Time.deltaTime)
            : raw;

        //Одновременно работать либо с RigidBody либо прямое управвление через трансформацию
        vInput = _move.y * moveSpeed;
        hInput = _move.x * rotationSpeed;
        //нажатия живут один кадр, поэтому читаем их здесь,
        //а применяем в FixedUpdate, иначе они теряются или дублируются
        if (_jumpAction.WasPressedThisFrame())
        {
            _jumpRequested = true;
        }
        if (_fireAction.WasPressedThisFrame())
        {
            _fireRequested = true;
        }
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        //поворот на месте тоже должен читаться как движение, иначе персонаж крутится в позе покоя
        float move = Mathf.Abs(_move.y);
        float turn = Mathf.Abs(_move.x) * 0.55f;
        float wanted = Mathf.Max(move, turn);
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
        //Поворот ведём собственным углом. Если брать за основу _rb.rotation, то любое касание
        //коллайдера закручивает тело вокруг Y (эта ось не заморожена), и разворот прилипает:
        //солвер повернул тело, а MoveRotation принял это за новое исходное положение.
        _yaw = Mathf.Repeat(_yaw + hInput * Time.fixedDeltaTime, 360f);
        _rb.angularVelocity = Vector3.zero;   //гасим закрутку, полученную от столкновений
        _rb.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));
        _rb.MovePosition(this.transform.position + this.transform.forward * vInput * Time.fixedDeltaTime);
        // add jump
        //in editor need chek, that ground layer is set
        if (_jumpRequested)
        {
            _jumpRequested = false;
            if (IsGrounded())
            {
                _rb.AddForce(Vector3.up * jumpVelocity, ForceMode.Impulse);
            }
        }
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
        //спавним перед игроком с учётом его поворота, иначе пуля рождается внутри коллайдера
        Vector3 spawnPoint = this.transform.position + this.transform.forward * bulletSpawnOffset;
        if (animator != null)
        {
            animator.SetTrigger(FireHash);
            if (_fireLayer >= 0)
            {
                _fireWeight = 1f;
                animator.SetLayerWeight(_fireLayer, 1f);
            }
        }
        GameObject newBullet = Instantiate(bullet, spawnPoint, this.transform.rotation);
        if (newBullet.TryGetComponent(out Rigidbody bulletRB))
        {
            bulletRB.linearVelocity = this.transform.forward * bulletSpeed;
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
