using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components; // necessário para NetworkTransform
using System;

public class PlayerDeathAndRespawn : NetworkBehaviour
{
    [Header("Refs")]
    [SerializeField] private NetworkTransform netTransform;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Health health;

    [Header("Spawns")]
    [Tooltip("Opcional. Se vazio, é encontrado automaticamente (SpawnsManager.I ou FindObjectOfType).")]
    [SerializeField] private SpawnsManager spawnsManager;
    [Tooltip("Modo de seleção do ponto de spawn.")]
    [SerializeField] private SelectionMode selectionMode = SelectionMode.Random;
    [Tooltip("Usar rotação do ponto de spawn (se falso, usa Quaternion.identity).")]
    [SerializeField] private bool useSpawnRotation = true;

    [Header("Offset/Segurança")]
    [Tooltip("Offset vertical aplicado acima do ponto de spawn.")]
    [SerializeField] private float spawnUpOffset = 1.5f;
    [Tooltip("Raycast para ajustar o spawn ao chão (recomendado).")]
    [SerializeField] private bool groundSnap = true;
    [SerializeField] private float groundRaycastUp = 2f;
    [SerializeField] private float groundRaycastDown = 10f;

    private static int s_roundRobinIndex = 0;

    public enum SelectionMode
    {
        Random,
        RoundRobin, // usa SpawnsManager.GetNext()
        ByClientId  // determinístico: OwnerClientId % count
    }

    void Awake()
    {
        if (!netTransform) netTransform = GetComponentInChildren<NetworkTransform>();
        if (!characterController) characterController = GetComponentInChildren<CharacterController>();
        if (!health) health = GetComponentInChildren<Health>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!netTransform) netTransform = GetComponentInChildren<NetworkTransform>();
        if (!spawnsManager)
            spawnsManager = SpawnsManager.I ? SpawnsManager.I : FindObjectOfType<SpawnsManager>();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RespawnServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (health == null)
        {
            Debug.LogError("[Respawn] Health nulo no servidor.");
            return;
        }

        if (!health.isDead.Value)
        {
            Debug.LogWarning("[Respawn] Ignorado: jogador não está morto.");
            return;
        }

        GetSpawnPose(out Vector3 spawnPos, out Quaternion spawnRot);

        Debug.Log($"[Respawn] Iniciando respawn no servidor. SpawnPos={spawnPos}");

        health.ResetFullHealth(); // server -> direto
        ServerTeleport(spawnPos, spawnRot);
    }

    public void ServerTeleport(Vector3 spawnPos, Quaternion spawnRot)
    {
        if (!IsServer) return;

        bool ccWasEnabled = characterController && characterController.enabled;
        if (ccWasEnabled) characterController.enabled = false;

        Vector3 scale = transform.localScale;

        if (netTransform != null)
        {
            try
            {
                if (netTransform.CanCommitToTransform)
                {
                    netTransform.Teleport(spawnPos, spawnRot, scale);
                    // Dica: Owners também podem relockar localmente depois do teleport via rede
                    GameplayCursor.Lock();
                }
                else
                {
                    var target = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                    };
                    OwnerTeleportClientRpc(spawnPos, spawnRot, scale, target);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Respawn] Exceção no teleport server: {ex.Message}. Fallback: pedir ao dono.");
                var target = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                };
                OwnerTeleportClientRpc(spawnPos, spawnRot, scale, target);
            }
        }
        else
        {
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            GameplayCursor.Lock();
        }

        if (ccWasEnabled) characterController.enabled = true;
    }

    [ClientRpc]
    private void OwnerTeleportClientRpc(Vector3 pos, Quaternion rot, Vector3 scale, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        bool ccWasEnabled = characterController && characterController.enabled;
        if (ccWasEnabled) characterController.enabled = false;

        if (netTransform != null)
        {
            try
            {
                netTransform.Teleport(pos, rot, scale);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Respawn] Client(owner) Teleport falhou: {ex.Message}. Usando SetPositionAndRotation.");
                transform.SetPositionAndRotation(pos, rot);
                transform.localScale = scale;
            }
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = scale;
        }

        // GARANTIA: no fim do respawn, bloqueia e oculta o cursor (volta à mira FPS)
        GameplayCursor.Lock();

        if (ccWasEnabled) characterController.enabled = true;
    }

    private void GetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        rot = useSpawnRotation ? Quaternion.identity : Quaternion.identity;

        var sm = spawnsManager ? spawnsManager : (SpawnsManager.I ? SpawnsManager.I : FindObjectOfType<SpawnsManager>());
        if (sm == null || sm.points == null || sm.points.Length == 0)
        {
            SafeSnapToGround(ref pos);
            return;
        }

        if (selectionMode == SelectionMode.RoundRobin)
        {
            sm.GetNext(out pos, out rot);
            float extraUp = Mathf.Max(0f, spawnUpOffset - 0.1f);
            pos += Vector3.up * extraUp;
            if (!useSpawnRotation) rot = Quaternion.identity;
            SafeSnapToGround(ref pos);
            return;
        }

        int count = sm.points.Length;
        int idx = 0;
        switch (selectionMode)
        {
            case SelectionMode.Random:
                idx = UnityEngine.Random.Range(0, count);
                break;
            case SelectionMode.ByClientId:
                idx = (int)(OwnerClientId % (ulong)count);
                break;
        }

        var t = sm.points[idx];
        if (t == null)
        {
            SafeSnapToGround(ref pos);
            return;
        }

        pos = t.position + Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        rot = useSpawnRotation ? t.rotation : Quaternion.identity;

        SafeSnapToGround(ref pos);
    }

    private void SafeSnapToGround(ref Vector3 pos)
    {
        if (!groundSnap) return;

        Vector3 origin = pos + Vector3.up * Mathf.Max(0.01f, groundRaycastUp);
        if (Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Max(groundRaycastDown, spawnUpOffset + 2f), ~0, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        }
    }
}