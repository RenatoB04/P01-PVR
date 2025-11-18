using UnityEngine;
using UnityEngine.Events;
using TMPro;
using System;
using Unity.Netcode;
using System.Collections;

public class Health : NetworkBehaviour
{
    [Header("Config")]
    public float maxHealth = 100f;

    // Variáveis de Rede
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    
    public NetworkVariable<int> team = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Events")]
    public UnityEvent<float, float> OnHealthChanged;
    public UnityEvent OnDied;
    public event Action<float, Transform> OnTookDamage;

    [Header("UI (Opcional)")]
    [HideInInspector] public TextMeshProUGUI healthText;

    private PlayerShield playerShield;
    private ulong lastInstigatorClientId = ulong.MaxValue;
    private Coroutine uiFinderCo;

    void Awake()
    {
        playerShield = GetComponent<PlayerShield>();
        UpdateHealthUI(maxHealth);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;

            // --- LÓGICA DE EQUIPA (Bots vs Players) ---
            if (team.Value == -1) 
            {
                if (GetComponent<BotAI_Proto>() != null)
                    team.Value = -2; // Bot
                else
                    team.Value = (int)OwnerClientId; // Player
            }
        }

        currentHealth.OnValueChanged += OnHealthValueChanged;
        isDead.OnValueChanged += OnIsDeadChanged;

        UpdateHealthUI(currentHealth.Value);
        OnHealthChanged?.Invoke(currentHealth.Value, maxHealth);

        if (IsOwner)
            uiFinderCo = StartCoroutine(FindUIRefresh());
    }

    private IEnumerator FindUIRefresh()
    {
        const int safetyFrames = 600;
        int frames = 0;
        GameObject healthTextObj = null;

        while (healthTextObj == null && frames < safetyFrames)
        {
            yield return null;
            frames++;
            healthTextObj = GameObject.FindWithTag("HealthText");
            if (healthTextObj == null)
            {
                var byName = GameObject.Find("HealthText");
                if (byName && byName.GetComponent<TextMeshProUGUI>() != null)
                    healthTextObj = byName;
            }
        }

        if (healthTextObj != null)
        {
            healthText = healthTextObj.GetComponent<TextMeshProUGUI>();
            UpdateHealthUI(currentHealth.Value);
        }
        
        uiFinderCo = null;
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthValueChanged;
        isDead.OnValueChanged -= OnIsDeadChanged;
        if (uiFinderCo != null) { StopCoroutine(uiFinderCo); uiFinderCo = null; }
    }

    private void OnHealthValueChanged(float prev, float curr)
    {
        UpdateHealthUI(curr);
        OnHealthChanged?.Invoke(curr, maxHealth);
    }

    private void OnIsDeadChanged(bool prev, bool curr)
    {
        if (curr && !prev)
            OnDied?.Invoke();
    }

    // ---------------------------------------------------
    //                  SISTEMA DE DANO
    // ---------------------------------------------------

    // Função principal chamada pelo servidor (Bala -> Aqui)
    public void ApplyDamageServer(float amount, int instigatorTeam, ulong instigatorClientId, Vector3 hitWorldPos, bool showIndicator = true)
    {
        if (!IsServer) return;
        if (isDead.Value) return;

        amount = Mathf.Clamp(amount, 0f, maxHealth * 2f);
        if (amount <= 0f) return;

        // 1. Verificar Escudo
        if (playerShield != null && playerShield.IsShieldActive.Value)
        {
            amount = playerShield.AbsorbDamageServer(amount);
            if (amount <= 0.01f) return; // Escudo tankou tudo
        }

        // 2. Verificar Friendly Fire
        if (team.Value != -1 && instigatorTeam != -1 && team.Value == instigatorTeam)
        {
            return; // Ignora dano de amigos
        }

        lastInstigatorClientId = instigatorClientId;
        float oldHealth = currentHealth.Value;
        float newHealth = Mathf.Max(0f, oldHealth - amount);

        if (Mathf.Approximately(oldHealth, newHealth)) return;

        currentHealth.Value = newHealth;

        if (newHealth < oldHealth)
            OnTookDamage?.Invoke(amount, null);

        // 3. FEEDBACK VISUAL (CORRIGIDO)
        // Agora enviamos também o 'amount' para o cliente saber a intensidade do flash
        if (showIndicator)
        {
            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            // Chama a função no cliente com o valor do dano
            DamageIndicatorClientRpc(hitWorldPos, amount, clientParams);
        }

        // 4. Verificar Morte
        if (newHealth <= 0f && !isDead.Value)
        {
            isDead.Value = true;
            Debug.Log($"[Health] {name} morreu (Matador: {instigatorClientId}).");
            TryAwardKillToLastInstigator();
        }
    }

    // --- RPC: Executado no Cliente do Dono ---
    // ADICIONEI 'float damageAmount' aos argumentos
    [ClientRpc]
    private void DamageIndicatorClientRpc(Vector3 sourceWorldPos, float damageAmount, ClientRpcParams rpcParams = default)
    {
        // Só executa se formos o dono deste boneco
        if (!IsOwner) return;

        // Chama o Singleton da UI de Dano (DamageIndicatorUI)
        if (DamageIndicatorUI.Instance != null)
        {
            // Passa a posição de origem e a quantidade de dano
            DamageIndicatorUI.Instance.RegisterHit(sourceWorldPos, damageAmount);
        }
    }

    private void TryAwardKillToLastInstigator()
    {
        if (!IsServer) return;
        if (lastInstigatorClientId == ulong.MaxValue) return;
        if (lastInstigatorClientId == OwnerClientId) return; 

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(lastInstigatorClientId, out var client) &&
            client != null && client.PlayerObject != null)
        {
            // Aqui podes dar pontos:
            var ps = client.PlayerObject.GetComponent<PlayerScore>();
            if (ps != null) ps.AwardKillAndPoints();
        }
        lastInstigatorClientId = ulong.MaxValue;
    }

    // ---------------------------------------------------
    //                  CURA / RESET
    // ---------------------------------------------------

    public void ResetFullHealth() => ResetHealthServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void ResetHealthServerRpc()
    {
        isDead.Value = false;
        currentHealth.Value = maxHealth;
    }

    public void Heal(float amount)
    {
        if (isDead.Value) return;
        HealServerRpc(amount);
    }

    [ServerRpc(RequireOwnership = false)]
    private void HealServerRpc(float amount)
    {
        if (isDead.Value) return;
        amount = Mathf.Clamp(amount, 0f, maxHealth * 2f);
        if (amount <= 0f) return;
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
    }

    // Compatibilidade
    public void TakeDamage(float amount) => TakeDamageServerRpc(amount, -1, ulong.MaxValue, Vector3.zero, false);

    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(float amount, int instigatorTeam, ulong instigatorClientId, Vector3 hitWorldPos, bool showIndicator)
    {
        ApplyDamageServer(amount, instigatorTeam, instigatorClientId, hitWorldPos, showIndicator);
    }

    private void UpdateHealthUI(float v)
    {
        if (healthText != null)
            healthText.text = $"{v:0}";
    }
}