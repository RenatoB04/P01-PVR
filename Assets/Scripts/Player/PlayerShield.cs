using Unity.Netcode;
using UnityEngine;
using TMPro;
using System.Collections;

public class PlayerShield : NetworkBehaviour
{
    public enum ShieldMode { Capacity, Duration }

    [Header("Referências Visuais")]
    [SerializeField] private GameObject shieldVisual;
    [Tooltip("Arrasta aqui o prefab 'VFX_Pulse' que criaste")]
    [SerializeField] private GameObject pulseVfxPrefab; // <-- NOVO

    [Header("UI (Automático via Tag 'ShieldText')")]
    private TextMeshProUGUI shieldTextUI;

    [Header("Modo Escudo")]
    [SerializeField] private ShieldMode shieldMode = ShieldMode.Capacity;

    [Header("Configurações Escudo")]
    [SerializeField] private float shieldCapacity = 100f;
    [SerializeField] private float shieldDuration = 2.0f;
    [SerializeField] private float shieldCooldown = 30.0f;

    [Header("Configurações Pulso")]
    [SerializeField] private float pulseDamage = 40f;
    [SerializeField] private float pulseRadius = 4.0f;
    [SerializeField] private float pulseCastTime = 1.5f;
    [SerializeField] private float pulseCooldown = 45.0f;

    // --- NetworkVariables ---
    public NetworkVariable<bool> IsShieldActive = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<double> NextShieldReadyTime = new NetworkVariable<double>(0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> ShieldHealth = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<double> NextPulseReadyTime = new NetworkVariable<double>(0.0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> IsPulseCasting = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Health health;
    private Coroutine pulseCastCoroutine;

    void Awake()
    {
        health = GetComponent<Health>();
        if (shieldVisual != null) shieldVisual.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (shieldVisual != null) shieldVisual.SetActive(IsShieldActive.Value);
        if (IsOwner) StartCoroutine(FindShieldUI());
    }

    private IEnumerator FindShieldUI()
    {
        while (shieldTextUI == null)
        {
            GameObject uiObj = GameObject.FindGameObjectWithTag("ShieldText");
            if (uiObj != null) shieldTextUI = uiObj.GetComponent<TextMeshProUGUI>();
            yield return new WaitForSeconds(0.5f);
        }
        shieldTextUI.text = "";
    }

    void Update()
    {
        if (shieldVisual != null && shieldVisual.activeSelf != IsShieldActive.Value)
        {
            shieldVisual.SetActive(IsShieldActive.Value);
        }

        if (IsOwner && shieldTextUI != null)
        {
            if (IsShieldActive.Value)
                shieldTextUI.text = (shieldMode == ShieldMode.Capacity) ? $"SHIELD: {ShieldHealth.Value:0}" : "SHIELD: ACTIVE";
            else if (IsPulseCasting.Value) // <-- NOVO: Mostra na UI que estamos a carregar o pulso
                shieldTextUI.text = "CHARGING PULSE...";
            else
                shieldTextUI.text = "";
        }
    }

    public override void OnNetworkDespawn()
    {
        if (shieldVisual && shieldVisual.activeSelf) shieldVisual.SetActive(false);
        if (IsOwner && shieldTextUI != null) shieldTextUI.text = "";
        base.OnNetworkDespawn();
    }

    // ==================== LÓGICA SERVER ====================

    [ServerRpc]
    public void RequestShieldServerRpc()
    {
        double now = NetworkManager.LocalTime.Time;
        if (now < NextShieldReadyTime.Value || IsShieldActive.Value) return;
        if (health != null && health.isDead.Value) return;

        NextShieldReadyTime.Value = now + shieldCooldown;
        IsShieldActive.Value = true;

        if (shieldMode == ShieldMode.Capacity) ShieldHealth.Value = shieldCapacity;
        else { ShieldHealth.Value = 0; StartCoroutine(ShieldActiveTimerServer()); }
    }

    private IEnumerator ShieldActiveTimerServer()
    {
        yield return new WaitForSeconds(shieldDuration);
        if (IsShieldActive.Value) DeactivateShieldServer();
    }

    private void DeactivateShieldServer()
    {
        IsShieldActive.Value = false;
        ShieldHealth.Value = 0;
    }

    public float AbsorbDamageServer(float incomingDamage)
    {
        if (!IsServer || !IsShieldActive.Value) return incomingDamage;
        if (shieldMode == ShieldMode.Duration) return 0f;
        if (shieldMode == ShieldMode.Capacity)
        {
            float absorbed = Mathf.Min(ShieldHealth.Value, incomingDamage);
            ShieldHealth.Value -= absorbed;
            if (ShieldHealth.Value <= 0) DeactivateShieldServer();
            return incomingDamage - absorbed;
        }
        return incomingDamage;
    }

    [ServerRpc]
    public void RequestPulseServerRpc()
    {
        if (health == null || health.isDead.Value) return;
        double now = NetworkManager.LocalTime.Time;
        if (now < NextPulseReadyTime.Value || IsPulseCasting.Value) return;
        pulseCastCoroutine = StartCoroutine(PulseCastAndExecuteServer());
    }

    private IEnumerator PulseCastAndExecuteServer()
    {
        IsPulseCasting.Value = true;
        yield return new WaitForSeconds(pulseCastTime);
        if (health == null || health.isDead.Value) { IsPulseCasting.Value = false; yield break; }
        
        ExecutePulseServer();
        
        // --- NOVO: Manda o servidor avisar todos os clientes para tocarem o VFX ---
        PlayPulseVfxClientRpc(transform.position);

        IsPulseCasting.Value = false;
        NextPulseReadyTime.Value = NetworkManager.LocalTime.Time + pulseCooldown;
    }

    private void ExecutePulseServer()
    {
        Vector3 center = transform.position;
        Collider[] hits = Physics.OverlapSphere(center, pulseRadius);
        foreach (var col in hits)
        {
            if (!col) continue;
            Health tHealth = col.GetComponentInParent<Health>();
            if (tHealth && tHealth.transform.root != transform.root)
            {
                tHealth.ApplyDamageServer(pulseDamage, health.team.Value, OwnerClientId, center, true);
            }
        }
    }

    // --- NOVO: ClientRpc para visuais ---
    [ClientRpc]
    private void PlayPulseVfxClientRpc(Vector3 position)
    {
        // Cria o efeito visual na posição onde o pulso ocorreu
        if (pulseVfxPrefab != null)
        {
            Instantiate(pulseVfxPrefab, position, Quaternion.identity);
        }
    }
}