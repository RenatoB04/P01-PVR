using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class Health : MonoBehaviour
{
    [Header("Config")]
    public float maxHealth = 100f;
    public int team = -1; // -1 = neutro; 0 = jogador; 1 = bots

    [Header("Runtime")]
    public float currentHealth;
    public bool isDead;

    [Header("Events")]
    public UnityEvent<float, float> OnHealthChanged; 
    public UnityEvent OnDied;

    [Header("UI (Opcional)")]
    public TextMeshProUGUI healthText; // texto hp UI

    void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
        UpdateHealthUI();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // ------------------ DANO ------------------

    /// <summary>
    /// Método antigo — mantém compatibilidade.
    /// </summary>
    public void TakeDamage(float amount, int instigatorTeam = -1)
    {
        InternalApplyDamage(amount, instigatorTeam, null, Vector3.zero, hasSource: false);
    }

    /// <summary>
    /// Nova versão — recebe também o atacante e posição do impacto.
    /// </summary>
    public void TakeDamageFrom(float amount, int instigatorTeam, Transform attacker, Vector3 hitWorldPos)
    {
        InternalApplyDamage(amount, instigatorTeam, attacker, hitWorldPos, hasSource: true);
    }

    void InternalApplyDamage(float amount, int instigatorTeam, Transform attacker, Vector3 hitWorldPos, bool hasSource)
    {
        if (isDead) return;

        // Evita friendly fire
        if (team != -1 && instigatorTeam != -1 && team == instigatorTeam)
            return;

        float oldHealth = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);

        UpdateHealthUI();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        // Mostra Damage Indicator apenas no jogador local (team == 1)
        if (team == 1 && DamageIndicatorUI.Instance && hasSource && currentHealth < oldHealth)
        {
            Vector3 source = attacker ? attacker.position : hitWorldPos;
            DamageIndicatorUI.Instance.RegisterHit(source, amount);
        }

        if (currentHealth <= 0 && !isDead)
        {
            isDead = true;
            OnDied?.Invoke();
        }
    }

    // ------------------ CURA / RESET ------------------

    public void Heal(float amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHealthUI();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void ResetFullHealth()
    {
        isDead = false;
        currentHealth = maxHealth;
        UpdateHealthUI();
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // ------------------ UI ------------------

    void UpdateHealthUI()
    {
        if (healthText != null)
            healthText.text = $"HP: {currentHealth}/{maxHealth}";
    }
}
