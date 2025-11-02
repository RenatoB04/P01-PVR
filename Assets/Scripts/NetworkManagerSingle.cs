using UnityEngine;
using Unity.Netcode;

public class NetcodeBootstrap : MonoBehaviour
{
    void Awake()
    {
        // Garante Singleton
        var others = FindObjectsOfType<NetcodeBootstrap>();
        if (others.Length > 1) { Destroy(gameObject); return; }

        DontDestroyOnLoad(gameObject);

        // Se quiseres garantir que o NetworkManager também não duplica:
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager não está neste GameObject!");
        }
    }
}