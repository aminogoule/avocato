using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyBehavior : MonoBehaviour
{
    public Transform patrolRoute; //ref to object whom linked in inspector
    [SerializeField] private List<Transform> locations = new(); // nav points, filled from patrolRoute
    private NavMeshAgent agent;
    private int locationIdex = 0;
    public Transform player;
    private bool _isChasing = false;
    private int _lives = 3; //set enemy lives
    public int EnemyLives
    {
        get { return _lives; }
        private set
        {
            _lives = value;
            if (_lives <= 0)
            {
                Die();
            }
        }
    }
    void InitializePatrolRoute()
    {
        //чистим список, иначе точки продублируются, если он уже заполнен в инспекторе
        locations.Clear();
        if (patrolRoute == null)
        {
            Debug.LogWarning("Patrol route is not assigned", this);
            return;
        }
        foreach (Transform child in patrolRoute)
        {
            //adding to list
            locations.Add(child);
        }
    }
    void MoveToNextPoint()
    {
        if (locations.Count == 0) //if locatoin equal 0 then return
            return;

        locationIdex = (locationIdex + 1) % locations.Count; //infinite location points
        GoToCurrentPoint();
    }
    void GoToCurrentPoint()
    {
        if (locations.Count == 0)
            return;

        agent.destination = locations[locationIdex].position;
    }
    public void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        InitializePatrolRoute();
        GoToCurrentPoint();
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player"); //get player by tag
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }
        if (player == null)
        {
            Debug.LogError("Object with tag Player not found on the scene", this);
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && player != null)
        {
            _isChasing = true;
            agent.destination = player.position;
            Debug.Log("Player has enter to vision");
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _isChasing = false;
            GoToCurrentPoint(); // возвращаемся к той же точке, а не через одну
            Debug.Log("Player has leave trigger site");
        }
    }
    void Update()
    {
        if (_isChasing && player != null)
        {
            agent.destination = player.position; //держим цель, пока игрок в зоне видимости
            return;
        }
        if (agent.remainingDistance < 0.2f && !agent.pathPending)
        {
            MoveToNextPoint();
        }
    }
    void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Bullet"))
        {
            Destroy(collision.gameObject); //одна пуля наносит ровно один урон
            EnemyLives -= 1;
            Debug.Log("Got a hit");
        }
    }
    private void Die()
    {
        Debug.Log("Enemy destroyed");
        //отключаем логику сразу, Destroy сработает только в конце кадра
        enabled = false;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }
        Destroy(this.gameObject);
    }
}
