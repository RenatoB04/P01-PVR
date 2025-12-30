using Unity.Netcode;
using UnityEngine;
using System.Collections;

/// <summary>
/// NetworkSpawnHandler
/// ---------------------------------------------------------------------------
/// Componente responsável por tratar o spawn seguro de jogadores em rede.
/// Integra-se com o PlayerDeathAndRespawn para garantir que o jogador 
/// inicia corretamente na cena após ser instanciado pelo Netcode for GameObjects.
/// ---------------------------------------------------------------------------
/// Observações de networking:
/// - Só executa respawn se o objeto for do cliente dono (IsOwner).
/// - Utiliza coroutine para garantir que o objeto já está totalmente spawnado
///   antes de chamar qualquer ServerRpc.
/// - Evita problemas de sincronização que poderiam ocorrer se tentasse 
///   respawnear antes do objeto estar completamente inicializado.
/// ---------------------------------------------------------------------------
/// Requisitos:
/// - Deve existir um PlayerDeathAndRespawn no mesmo GameObject.
/// ---------------------------------------------------------------------------
/// </summary>
[RequireComponent(typeof(PlayerDeathAndRespawn))]
public class NetworkSpawnHandler : NetworkBehaviour
{
    // Referência ao controlador de morte e respawn do jogador
    private PlayerDeathAndRespawn respawnController;

    void Awake()
    {
        // Obtém o componente PlayerDeathAndRespawn
        respawnController = GetComponent<PlayerDeathAndRespawn>();
        
        // Validação: se não existir, mostra erro no console
        if (respawnController == null)
        {
            Debug.LogError("NetworkSpawnHandler: Falha ao encontrar PlayerDeathAndRespawn. Verifique o Prefab.");
        }
    }

    /// <summary>
    /// Chamado quando o Netcode instancia este objeto em rede.
    /// </summary>
    public override void OnNetworkSpawn() 
    {
        base.OnNetworkSpawn();
        
        // Apenas o cliente dono do objeto deve iniciar o respawn
        if (IsOwner && respawnController != null)
        {
            // Coroutine garante que o respawn é chamado com segurança
            StartCoroutine(SafeRespawnCoroutine());
        }
    }
    
    /// <summary>
    /// Coroutine para chamar o RespawnServerRpc de forma segura
    /// após o objeto estar completamente spawnado.
    /// </summary>
    private IEnumerator SafeRespawnCoroutine()
    {
        // Espera pelo próximo frame para garantir que todos os sistemas de rede estão prontos
        yield return null; 
        
        // Verifica novamente se o objeto está spawnado e se o controller existe
        if (IsSpawned && respawnController != null)
        {
            Debug.Log("[SpawnHandler] A chamar RespawnServerRpc(ignoreAliveCheck: true) para spawn inicial...");
            
            // Chamada server-authoritative para spawn inicial
            respawnController.RespawnServerRpc(true);
        }
    }
}
