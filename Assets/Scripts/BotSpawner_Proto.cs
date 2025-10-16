using UnityEngine;
using System.Collections;

public class BotSpawner_Proto : MonoBehaviour
{
    [Header("Configuração Inicial")]
    public GameObject botPrefab;
    public Transform[] spawnPoints;
    public Transform[] patrolWaypoints;
    [Tooltip("Quantos bots devem existir em simultâneo.")]
    public int count = 3;

    [Header("Respawn")]
    [Tooltip("Segundos a aguardar após a morte para nascer um novo bot.")]
    public float respawnDelay = 5f;

    int nextSpawnIndex = 0;
    int spawnedTotal = 0;

    void Start()
    {
        if (botPrefab == null || spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("Configura botPrefab e spawnPoints.");
            return;
        }

        SpawnBots();
    }

    void SpawnBots()
    {
        for (int i = 0; i < count; i++)
            SpawnOne();

        Debug.Log($"Spawned {count} bots.");
    }

    // ------------------------------------------------------------
    // Spawn unitário (CRÍTICO: Injeção de Waypoints e Link de Eventos)
    // ------------------------------------------------------------
    void SpawnOne()
    {
        var spawnPoint = spawnPoints[nextSpawnIndex % spawnPoints.Length];
        nextSpawnIndex++;

        // 1. Instanciar
        var bot = Instantiate(botPrefab, spawnPoint.position, spawnPoint.rotation);
        spawnedTotal++;
        bot.name = $"Bot_{spawnedTotal}";

        // 2. ATRIBUIR WAYPOINTS AO BotAI_Proto E INICIAR PATRULHA
        var ai = bot.GetComponent<BotAI_Proto>();
        if (ai != null)
        {
            ai.patrolPoints = patrolWaypoints;
            
            // CRÍTICO: Inicia a patrulha AGORA que a lista está completa
            if (ai.patrolPoints != null && ai.patrolPoints.Length > 0)
            {
                ai.SetInitialDestination(ai.patrolPoints[0].position); 
            }
        }

        // 3. Ligar o script BotRespawnLink e Eventos
        var link = bot.GetComponent<BotRespawnLink>();
        if (!link) link = bot.AddComponent<BotRespawnLink>();
        link.spawner = this;
        link.patrolWaypoints = patrolWaypoints;

        var death = bot.GetComponent<BOTDeath>();
        if (death != null)
        {
            death.OnDied -= HandleBotDied;
            death.OnDied += HandleBotDied;
        }
        else
        {
            Debug.LogWarning($"[BotSpawner_Proto] Bot '{bot.name}' não tem BOTDeath. Sem respawn automático.");
        }
    }

    // ------------------------------------------------------------
    // Evento chamado quando um bot morre
    // ------------------------------------------------------------
    void HandleBotDied(BOTDeath death)
    {
        death.OnDied -= HandleBotDied;
        StartCoroutine(RespawnRoutine());
    }

    // ------------------------------------------------------------
    // Coroutine interna usada para respawn
    // ------------------------------------------------------------
    IEnumerator RespawnRoutine()
    {
        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        SpawnOne();
    }

    // ------------------------------------------------------------
    // Método público chamado pelo BotRespawnLink (para compatibilidade)
    // ------------------------------------------------------------
    public void ScheduleRespawn(Transform[] waypointsFromDead)
    {
        // Usa o fluxo de respawn já existente
        StartCoroutine(RespawnRoutine());
    }
}