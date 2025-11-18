using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components; // Necessário para NetworkTransform
using System;

// NOTA: Assumimos que existe uma classe 'Health' com uma NetworkVariable<bool> chamada 'isDead'.
// Assumimos também que existe uma classe estática 'GameplayCursor' com métodos Lock() e Unlock().

public class PlayerDeathAndRespawn : NetworkBehaviour
{
    // ==================================================================================
    // === NOVAS VARIÁVEIS PARA CONTROLO DE ESTADO E UI =================================
    // ==================================================================================

    [Header("Refs")]
    [SerializeField] private NetworkTransform netTransform;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Health health; // Componente Health do jogador

    [Header("Componentes de Controlo e UI")]
    [Tooltip("O GameObject que contém a UI de Morte/Respawn (Canvas).")]
    [SerializeField] private GameObject deathCanvasUI;

    /// <summary>
    /// Propriedade pública a ser verificada por scripts como PlayerMovement ou PlayerShooting.
    /// Se for TRUE, o jogador pode interagir (mexer/disparar). Se for FALSE (morto), deve ser ignorado.
    /// </summary>
    public bool IsPlayerControlled => IsOwner && health != null && !health.isDead.Value;

    // ==================================================================================
    // === REFS E LÓGICA EXISTENTE ======================================================
    // ==================================================================================

    [Header("Spawn Points Fixos (Mundiais)")]
    [Tooltip("SpawnPoint A (por exemplo lado esquerdo do mapa).")]
    [SerializeField] private Vector3 spawnPointA = new Vector3(87f, 1.5f, 115f);

    [Tooltip("SpawnPoint B (por exemplo lado direito do mapa).")]
    [SerializeField] private Vector3 spawnPointB = new Vector3(87f, 1.5f, 175f);

    [Header("Offset/Segurança")]
    [Tooltip("Offset vertical aplicado acima do ponto de spawn.")]
    [SerializeField] private float spawnUpOffset = 1.5f;
    [Tooltip("Raycast para ajustar o spawn ao chão (recomendado).")]
    [SerializeField] private bool groundSnap = true;
    [SerializeField] private float groundRaycastUp = 2f;
    [SerializeField] private float groundRaycastDown = 10f;

    private struct Pose
    {
        public Vector3 pos;
        public Quaternion rot;
        public Pose(Vector3 p, Quaternion r) { pos = p; rot = r; }
    }

    private void Awake()
    {
        if (!netTransform) netTransform = GetComponentInChildren<NetworkTransform>();
        if (!characterController) characterController = GetComponentInChildren<CharacterController>();
        if (!health) health = GetComponentInChildren<Health>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!netTransform)
            netTransform = GetComponentInChildren<NetworkTransform>();

        // Lógica de spawn inicial (apenas servidor)
        if (IsServer)
        {
            var spawn = ResolveSpawnForOwner(OwnerClientId);
            Debug.Log($"[Respawn] OnNetworkSpawn → Spawn inicial. Owner={OwnerClientId}, SpawnPos={spawn.pos}");
            ForceOwnerTeleportServer(spawn.pos, spawn.rot);
        }

        // Cliente Owner subscreve para reagir a mortes/respawns
        if (IsOwner && health != null)
        {
            // Set do estado inicial ao fazer o spawn
            // A NetworkVariable já terá o valor correto (false, por defeito)
            HandleControlState(health.isDead.Value, health.isDead.Value);

            // Subscrição para reagir a futuras mudanças de estado
            health.isDead.OnValueChanged += HandleControlState;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Limpar subscrição ao sair da rede
        if (IsOwner && health != null)
        {
            health.isDead.OnValueChanged -= HandleControlState;
        }
    }

    // ==================================================================================
    // === LÓGICA DE CONTROLO DE ESTADO (CLIENTE OWNER) ==================================
    // ==================================================================================

    /// <summary>
    /// Chamado no Client Owner quando o estado de morte muda (trigger via Health.isDead NetworkVariable).
    /// Controla a UI de Morte e o cursor.
    /// </summary>
    private void HandleControlState(bool previousDead, bool currentDead)
    {
        // Esta lógica SÓ deve ser executada pelo OWNER.
        if (!IsOwner) return;

        if (currentDead)
        {
            // O jogador morreu (isDead = true).
            Debug.Log("[DeathAndRespawn] Player morreu. Desabilitar Controlo, Mostrar UI, Desbloquear Cursor.");
            
            // 1. Mostrar o Canvas de Morte (Permite interação)
            if (deathCanvasUI != null)
            {
                deathCanvasUI.SetActive(true);
            }
            
            // 2. Desbloquear o cursor do rato para permitir cliques na UI
            GameplayCursor.Unlock(); 

            // 3. Outros scripts (Movimento/Disparo) devem parar de funcionar 
            //    automaticamente verificando a propriedade IsPlayerControlled.
        }
        else
        {
            // O jogador está vivo / renasceu (isDead = false).
            Debug.Log("[DeathAndRespawn] Player renasceu. Habilitar Controlo, Esconder UI, Bloquear Cursor.");
            
            // 1. Esconder o Canvas de Morte
            if (deathCanvasUI != null)
            {
                deathCanvasUI.SetActive(false);
            }
            
            // 2. Bloquear o cursor para o gameplay (após esconder a UI)
            GameplayCursor.Lock(); 
            
            // 3. Outros scripts voltam a funcionar automaticamente.
        }
    }


    // ==================================================================================
    // === LÓGICA DE RESPAWN (SERVER) ====================================================
    // ==================================================================================

    /// <summary>
    /// RPC de respawn, usado quando o jogador morre.
    /// Para spawn inicial forçado, usar ignoreAliveCheck = true.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RespawnServerRpc(bool ignoreAliveCheck = false, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (health == null)
        {
            Debug.LogError("[Respawn] Health nulo no servidor.");
            return;
        }

        if (!ignoreAliveCheck && !health.isDead.Value)
        {
            Debug.LogWarning("[Respawn] Ignorado: jogador não está morto.");
            return;
        }

        var spawn = ResolveSpawnForOwner(OwnerClientId);
        Debug.Log($"[Respawn] Respawn/Spawn no servidor. Owner={OwnerClientId} SpawnPos={spawn.pos}");

        // CRUCIAL: Isto deve alterar health.isDead.Value para FALSE, 
        // o que por sua vez despoleta o HandleControlState no Owner.
        health.ResetFullHealth(); 
        
        ForceOwnerTeleportServer(spawn.pos, spawn.rot);
    }

    /// <summary>
    /// Força o dono a teletransportar-se. 
    /// </summary>
    private void ForceOwnerTeleportServer(Vector3 spawnPos, Quaternion spawnRot)
    {
        // 1) Se o servidor puder “commit” (server authority), teleporta também no servidor.
        try
        {
            if (netTransform != null && netTransform.CanCommitToTransform)
            {
                Vector3 scale = transform.localScale;
                netTransform.Teleport(spawnPos, spawnRot, scale);
            }
            else
            {
                // Mesmo sem autoridade, o RPC ao Owner irá resolver.
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Respawn] Server-side Teleport falhou/sem autoridade: {ex.Message}. A prosseguir com RPC ao Owner.");
        }

        // 2) **Sempre** enviar RPC dirigido ao Owner.
        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
        OwnerTeleportClientRpc(spawnPos, spawnRot, transform.localScale, target);
    }

    [ClientRpc]
    private void OwnerTeleportClientRpc(Vector3 pos, Quaternion rot, Vector3 scale, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        // Desativar temporariamente o CharacterController para evitar "jittering" no Teleport.
        bool ccWasEnabled = characterController && characterController.enabled;
        if (ccWasEnabled) characterController.enabled = false;

        try
        {
            if (netTransform != null)
            {
                // No Owner, a autoridade é local — este Teleport *vai* aplicar.
                netTransform.Teleport(pos, rot, scale);
            }
            else
            {
                transform.SetPositionAndRotation(pos, rot);
                transform.localScale = scale;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Respawn] Owner Teleport falhou: {ex.Message}. Fallback transform.SetPositionAndRotation.");
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = scale;
        }

        // O controlo do cursor (Lock/Unlock) é agora feito em HandleControlState.
        
        if (ccWasEnabled) characterController.enabled = true;
    }

    // ===================== LÓGICA DE RESOLUÇÃO DE SPAWN =====================

    private Pose ResolveSpawnForOwner(ulong ownerClientId)
    {
        // Fallback simples e determinístico A/B (sem dependências externas).
        if (spawnPointA == Vector3.zero && spawnPointB == Vector3.zero)
        {
            spawnPointA = new Vector3(-5f, spawnUpOffset, 0f);
            spawnPointB = new Vector3(5f, spawnUpOffset, 0f);
        }

        bool useA = (ownerClientId % 2UL == 0UL);
        var basePos = useA ? spawnPointA : spawnPointB;
        var rot = Quaternion.identity;

        var final = FinalizePose(basePos, rot);
        Debug.Log($"[Respawn] ResolveSpawn → Owner={ownerClientId}, useA={useA}, basePos={basePos}, finalPos={final.pos}");
        return final;
    }

    private Pose FinalizePose(Vector3 basePos, Quaternion rot)
    {
        var pos = basePos + Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        SafeSnapToGround(ref pos);
        return new Pose(pos, rot);
    }

    private void SafeSnapToGround(ref Vector3 pos)
    {
        if (!groundSnap) return;

        Vector3 origin = pos + Vector3.up * Mathf.Max(0.01f, groundRaycastUp);
        if (Physics.Raycast(origin, Vector3.down, out var hit,
                Mathf.Max(groundRaycastDown, spawnUpOffset + 2f),
                ~0, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        }
    }
}