using UnityEngine;

public class BulletBehavior : MonoBehaviour
{
    public float OnScreenDelay = 5f; //bullet life time
    // Start is called before the first frame update
    void Start()
    {
        Destroy(this.gameObject, OnScreenDelay);
    }
}
