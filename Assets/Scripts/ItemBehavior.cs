using UnityEngine;

public class ItemBehavior : MonoBehaviour
{
    public GameBehavior gameManager;
    private bool _collected = false;
    void Start()
    {
        if (gameManager == null)
        {
            gameManager = FindAnyObjectByType<GameBehavior>();
        }
        if (gameManager == null)
        {
            Debug.LogError("GameBehavior not found on the scene", this);
        }
    }
    void OnCollisionEnter(Collision collision)
    {
        //несколько контактов подряд не должны засчитываться как несколько предметов
        if (_collected || !collision.gameObject.CompareTag("Player"))
        {
            return;
        }
        _collected = true;
        Debug.Log("Item has collected");
        if (gameManager != null)
        {
            gameManager.Items += 1;
        }
        //корень предмета, если скрипт висит на дочернем объекте
        Transform root = this.transform.parent != null ? this.transform.parent : this.transform;
        Destroy(root.gameObject);
    }
}
