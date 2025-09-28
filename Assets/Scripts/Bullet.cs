using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float lifeTime = 2f; 

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        Destroy(gameObject);
    }
}
