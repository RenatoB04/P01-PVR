using UnityEngine;

public class BotSpawner_Proto : MonoBehaviour
{
    public GameObject botPrefab;
    public Transform[] spawnPoints;
    public int count = 3;

    void Start()
    {
        for (int i = 0; i < count; i++)
        {
            var p = spawnPoints[Random.Range(0, spawnPoints.Length)];
            Instantiate(botPrefab, p.position, p.rotation);
        }
    }
}
