using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using System;
using System.Collections;
using TMPro;

public class PlayerDeathAndRespawn : NetworkBehaviour
{
    [Header("Refs Físicas")]
    [SerializeField] private NetworkTransform netTransform;
    [SerializeField] private CapsuleCollider capsuleCollider;
    [SerializeField] private Health health;
    [SerializeField] private Rigidbody rb;

    [Header("Refs Visuais (NOVO)")]
    [Tooltip("O modelo 3D do corpo do boneco.")]
    [SerializeField] private GameObject visualRoot; 
    [Tooltip("O objeto pai das ARMAS (na câmara).")]
    [SerializeField] private GameObject weaponRoot; 

    [Header("UI")]
    [SerializeField] private GameObject deathCanvasUI;
    private TextMeshProUGUI _respawnTimerTextInstance;
    private Coroutine _uiFinderCo;

    [Header("Respawn Config")]
    [SerializeField] private float respawnDelay = 3.0f;

    // Sincroniza a posição inicial do servidor para todos
    private NetworkVariable<Vector3> _networkSpawnPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Coroutine _respawnCoroutine;

    // Propriedade para outros scripts saberem se estamos vivos
    public bool IsPlayerControlled => IsOwner && health != null && !health.isDead.Value;

    [Header("Spawn Points")]
    [SerializeField] private Vector3 spawnPointA = new Vector3(87f, 1.5f, 115f);
    [SerializeField] private Vector3 spawnPointB = new Vector3(87f, 1.5f, 175f);
    [SerializeField] private float spawnUpOffset = 1.5f;
    [SerializeField] private bool groundSnap = true;
    [SerializeField] private float groundRaycastUp = 2f;
    [SerializeField] private float groundRaycastDown = 10f;
    [SerializeField] private Vector3 deadZonePosition = new Vector3(0, -50, 0);

    private struct Pose { public Vector3 pos; public Quaternion rot; public Pose(Vector3 p, Quaternion r) { pos = p; rot = r; } }

    private void Awake()
    {
        if (!netTransform) netTransform = GetComponentInChildren<NetworkTransform>();
        if (!capsuleCollider) capsuleCollider = GetComponentInChildren<CapsuleCollider>();
        if (!health) health = GetComponentInChildren<Health>();
        if (!rb) rb = GetComponent<Rigidbody>(); 

        // Auto-encontrar visual se falhar o arrasto
        if (!visualRoot) 
        {
            var renderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer) visualRoot = renderer.gameObject;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!netTransform) netTransform = GetComponentInChildren<NetworkTransform>();

        if (IsServer)
        {
            // Calcular e guardar posição inicial
            var spawn = ResolveSpawnForOwner(OwnerClientId);
            _networkSpawnPosition.Value = spawn.pos;

            // Forçar teleporte inicial
            ForceOwnerTeleportServer(spawn.pos, spawn.rot);
        }

        // Inicializar estado visual e controlo
        if (health != null)
        {
            // Aplica o estado inicial (se entrou já morto ou vivo)
            HandleControlState(health.isDead.Value, health.isDead.Value);
            // Subscreve a mudanças futuras
            health.isDead.OnValueChanged += HandleControlState;
        }

        if (IsOwner)
        {
            _uiFinderCo = StartCoroutine(FindDeathUIRefs());
        }
    }

    private IEnumerator FindDeathUIRefs()
    {
        const int safetyFrames = 600;
        int frames = 0;
        GameObject timerTextObj = null;

        while (timerTextObj == null && frames < safetyFrames)
        {
            yield return null;
            frames++;
            timerTextObj = GameObject.FindWithTag("RespawnTimerTag");
        }

        if (timerTextObj != null)
        {
            _respawnTimerTextInstance = timerTextObj.GetComponent<TextMeshProUGUI>();
            if (deathCanvasUI == null)
            {
                deathCanvasUI = timerTextObj.GetComponentInParent<Canvas>(true)?.gameObject;
                if (deathCanvasUI != null) deathCanvasUI.SetActive(false);
            }
        }
        _uiFinderCo = null;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (health != null) health.isDead.OnValueChanged -= HandleControlState;
        if (_respawnCoroutine != null) StopCoroutine(_respawnCoroutine);
        if (_uiFinderCo != null) StopCoroutine(_uiFinderCo);
    }

    // --- LÓGICA CENTRAL DE ESTADO (VISUAL + UI + CONTROLO) ---
    private void HandleControlState(bool previousDead, bool currentDead)
    {
        // 1. Lógica Visual (Para TODOS verem se o boneco existe ou não)
        ToggleVisuals(!currentDead); // Se dead=true, visuals=false

        // 2. Lógica Local (UI e Input só para o Dono)
        if (IsOwner)
        {
            if (currentDead)
            {
                if (deathCanvasUI != null) deathCanvasUI.SetActive(true);
                if (_respawnTimerTextInstance != null) _respawnTimerTextInstance.gameObject.SetActive(false);
                GameplayCursor.Unlock();
                
                // Move para longe para não atrapalhar a câmara
                // (Nota: O Collider já é desligado no ToggleVisuals)
                //transform.position = deadZonePosition; 
            }
            else
            {
                if (deathCanvasUI != null) deathCanvasUI.SetActive(false);
                if (_respawnTimerTextInstance != null) _respawnTimerTextInstance.gameObject.SetActive(false);
                GameplayCursor.Lock();
            }
        }
    }

    private void ToggleVisuals(bool isActive)
    {
        // Esconde/Mostra Corpo
        if (visualRoot) visualRoot.SetActive(isActive);
        // Esconde/Mostra Arma
        if (weaponRoot) weaponRoot.SetActive(isActive);
        // Liga/Desliga Colisão (para não bloquear balas enquanto morto)
        if (capsuleCollider) capsuleCollider.enabled = isActive;
    }

    // --- LÓGICA DO SERVIDOR ---

    [ServerRpc(RequireOwnership = false)]
    public void RespawnServerRpc(bool ignoreAliveCheck = false, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (health == null || _respawnCoroutine != null) return;
        if (!ignoreAliveCheck && !health.isDead.Value) return;

        _respawnCoroutine = StartCoroutine(RespawnSequenceCoroutine(OwnerClientId));
    }

    private IEnumerator RespawnSequenceCoroutine(ulong clientID)
    {
        float timer = respawnDelay;
        UpdateRespawnTimerClientRpc(timer, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientID } } });

        while (timer > 0)
        {
            yield return new WaitForSeconds(1.0f);
            timer -= 1.0f;
            UpdateRespawnTimerClientRpc(timer, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientID } } });
        }

        // 1. Determinar Posição
        var spawnPos = _networkSpawnPosition.Value;
        if (spawnPos == Vector3.zero)
        {
            var newSpawn = ResolveSpawnForOwner(clientID);
            spawnPos = newSpawn.pos;
            _networkSpawnPosition.Value = spawnPos;
        }

        // 2. TELEPORTE
        ForceOwnerTeleportServer(spawnPos, Quaternion.identity);

        // 3. Restaurar Vida (Isto vai disparar o HandleControlState via OnValueChanged e reativar os visuais)
        health.ResetFullHealth();

        UpdateRespawnTimerClientRpc(0f, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientID } } });
        _respawnCoroutine = null;
    }

    [ClientRpc]
    private void UpdateRespawnTimerClientRpc(float timeRemaining, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        if (_respawnTimerTextInstance != null)
        {
            if (timeRemaining > 0)
            {
                if (!_respawnTimerTextInstance.gameObject.activeSelf) _respawnTimerTextInstance.gameObject.SetActive(true);
                _respawnTimerTextInstance.text = $"Respawning in: {Mathf.CeilToInt(timeRemaining)}";
            }
            else
            {
                _respawnTimerTextInstance.gameObject.SetActive(false);
            }
        }
    }

    private void ForceOwnerTeleportServer(Vector3 spawnPos, Quaternion spawnRot)
    {
        if (netTransform != null && netTransform.CanCommitToTransform)
        {
            netTransform.Teleport(spawnPos, spawnRot, transform.localScale);
        }
        var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
        OwnerTeleportClientRpc(spawnPos, spawnRot, transform.localScale, target);
    }

    [ClientRpc]
    private void OwnerTeleportClientRpc(Vector3 pos, Quaternion rot, Vector3 scale, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;
        StartCoroutine(TeleportSequence(pos, rot, scale));
    }

    private IEnumerator TeleportSequence(Vector3 targetPos, Quaternion targetRot, Vector3 targetScale)
    {
        // Congela Física
        if (capsuleCollider) capsuleCollider.enabled = false;
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true; 
        }

        // Move
        if (netTransform != null) netTransform.Teleport(targetPos, targetRot, targetScale);
        transform.position = targetPos;
        transform.rotation = targetRot;

        // Espera Frame
        yield return new WaitForEndOfFrame();
        yield return new WaitForFixedUpdate(); 

        // Reativa (SE estiver vivo, a lógica HandleControlState trata do resto, 
        // mas aqui garantimos que o collider fica pronto para a física se o boneco estiver visível)
        if (netTransform != null) netTransform.Teleport(targetPos, targetRot, targetScale);
        
        if (rb)
        {
            rb.isKinematic = false; 
            rb.linearVelocity = Vector3.zero; 
        }
        
        // Só reativa o collider se o boneco não estiver morto
        if (health && !health.isDead.Value && capsuleCollider) 
            capsuleCollider.enabled = true;
    }

    // --- Lógica de Spawn ---
    private Pose ResolveSpawnForOwner(ulong ownerClientId)
    {
        if (spawnPointA == Vector3.zero && spawnPointB == Vector3.zero)
        {
            spawnPointA = new Vector3(-5f, spawnUpOffset, 0f);
            spawnPointB = new Vector3(5f, spawnUpOffset, 0f);
        }
        bool useA = (ownerClientId % 2UL == 0UL);
        var basePos = useA ? spawnPointA : spawnPointB;
        return FinalizePose(basePos, Quaternion.identity);
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
        if (Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Max(groundRaycastDown, spawnUpOffset + 2f), ~0, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + Vector3.up * Mathf.Max(0.1f, spawnUpOffset);
        }
    }
}