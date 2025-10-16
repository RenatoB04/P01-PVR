using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class BotAI_Proto : MonoBehaviour
{
    // === ESTADOS DA IA ===
    enum BotState { Patrol, Chase, Attack, Retreat }
    [SerializeField]
    BotState currentState = BotState.Patrol;

    [Header("Patrulha")]
    public Transform[] patrolPoints; // PREENCHIDO PELO SPAWNER
    public float waypointTolerance = 0.6f;

    [Header("Visão")]
    public float viewRadius = 15f;
    [Range(0, 180)] public float viewAngle = 110f;
    public LayerMask targetMask;
    public LayerMask obstacleMask;
    public float loseSightTime = 2f;

    [Header("Combate / Retreat")]
    [Tooltip("Vida percentual (0.0 a 1.0) para iniciar o Retreat.")]
    public float healthThreshold = 0.3f;
    public float retreatSpeedMultiplier = 1.5f;

    [Header("Animação")]
    public Animator animator;
    [Tooltip("Multiplicador de velocidade para sincronizar a animação com o movimento real.")]
    public float speedMultiplier = 1.0f;

    // Dependências e Estado Interno
    NavMeshAgent agent;
    Health health;
    Transform target;
    int idx;
    float loseTimer;
    Vector3 lastTargetPosition; 
    float originalSpeed; 

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();

        if (!animator)
            animator = GetComponentInChildren<Animator>();

        if (animator)
            animator.applyRootMotion = false;

        originalSpeed = agent.speed;
        agent.stoppingDistance = 0.0f; // Controlado pelo BotCombat
    }

    void OnEnable()
    {
        if (health)
        {
            health.OnDied.RemoveListener(OnDeath); // Prevenção
            health.OnDied.AddListener(OnDeath);

            // Subscreve ao evento de dano (Action) do Health
            health.OnTookDamage -= HandleTookDamage; // Prevenção
            health.OnTookDamage += HandleTookDamage;
        }
        // Garante que o bot está no estado inicial correto
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            SetInitialDestination(patrolPoints[idx].position);
        }
    }

    void OnDisable()
    {
        if (health)
        {
            health.OnDied.RemoveListener(OnDeath);
            health.OnTookDamage -= HandleTookDamage;
        }
    }

    // --------------------------------------------------------
    // MÉTODOS PÚBLICOS
    // --------------------------------------------------------

    // Chamado pelo Spawner para forçar o destino inicial.
    public void SetInitialDestination(Vector3 pos)
    {
        if (agent.isActiveAndEnabled)
        {
            agent.SetDestination(pos); 
            currentState = BotState.Patrol; 
        }
    }

    // --------------------------------------------------------
    // LÓGICA DE DANO/RETREAT
    // --------------------------------------------------------

    // NOVO: Chamado pelo evento OnTookDamage (Action<float, Transform>)
    void HandleTookDamage(float damageAmount, Transform attacker)
    {
        float healthRatio = health.currentHealth / health.maxHealth;
        
        // Se a vida está abaixo do limiar E o bot não está já a fugir, OU o dano foi muito alto (dano de choque)
        if (healthRatio <= healthThreshold && currentState != BotState.Retreat)
        {
            target = attacker; // Define o atacante como alvo
            lastTargetPosition = attacker.position;
            TransitionToRetreat();
        }
        // Mesmo que não fuja, se estiver a patrulhar, deve reagir
        else if (currentState == BotState.Patrol)
        {
             // Opcional: faz o bot virar-se para o atacante para procurar
             // target = attacker; 
             // TransitionToChase(); 
        }
    }

    // --------------------------------------------------------
    // FSM (Lógica Principal)
    // --------------------------------------------------------

    void Update()
    {
        if (health == null || health.isDead)
        {
            if (agent.enabled) agent.isStopped = true;
            return;
        }
        
        UpdateState();
        UpdateAnimation();
    }

    void UpdateState()
    {
        DetectPlayer(); // Tenta sempre encontrar/manter o alvo

        float healthRatio = health.currentHealth / health.maxHealth;
        bool isLowHealth = healthRatio <= healthThreshold;

        // 1. Prioridade Máxima: Fuga (se ferido e a fugir)
        if (currentState == BotState.Retreat)
        {
            if (!isLowHealth) // Já recuperou vida (ou está longe o suficiente)
            {
                TransitionToPatrol();
            }
            DoRetreat();
            return;
        }

        // 2. Transição para Fuga (Se Low Health)
        if (isLowHealth)
        {
            TransitionToRetreat();
            return;
        }

        // 3. Prioridade Média: Perseguição/Ataque
        if (target != null)
        {
            // Se o alvo foi perdido, o DoChase/DoAttack cuida da transição
            TransitionToChase(); 
        }
        
        // 4. Prioridade Baixa: Patrulha
        else
        {
            TransitionToPatrol();
        }

        // Lógica de Estado
        switch (currentState)
        {
            case BotState.Patrol: DoPatrol(); break;
            case BotState.Chase: DoChase(); break;
            case BotState.Attack: DoAttack(); break;
            // Retreat é tratado acima
        }
    }

    // --------------------------------------------------------
    // ESTADOS E TRANSIÇÕES
    // --------------------------------------------------------

    void TransitionToPatrol()
    {
        if (currentState == BotState.Patrol) return;
        currentState = BotState.Patrol;
        agent.isStopped = false;
        agent.speed = originalSpeed; 
        
        SetNextPatrolPoint();
    }

    void DoPatrol()
    {
        if (agent.isActiveAndEnabled && agent.remainingDistance < waypointTolerance && !agent.pathPending)
        {
            SetNextPatrolPoint();
        }
    }

    void SetNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;

        idx = (idx + 1) % patrolPoints.Length;
        agent.SetDestination(patrolPoints[idx].position);
    }
    
    void TransitionToChase()
    {
        if (currentState == BotState.Chase) return;
        currentState = BotState.Chase;
        agent.isStopped = false;
        agent.speed = originalSpeed; 
    }

    void DoChase()
    {
        if (target == null) return;
        agent.isStopped = false;
        agent.SetDestination(target.position);
        lastTargetPosition = target.position; 

        float distance = Vector3.Distance(transform.position, target.position);
        
        if (distance <= 8f) // Distância de ataque (exemplo)
        {
            agent.isStopped = true;
            currentState = BotState.Attack;
        }

        // Se perder a linha de visão, perde o alvo
        if (!HasLineOfSight(target))
        {
            target = null; 
        }
    }

    void DoAttack()
    {
        // Se o alvo se afastar
        float distance = target ? Vector3.Distance(transform.position, target.position) : float.MaxValue;
        if (distance > 10f)
        {
            TransitionToChase();
        }
        else
        {
             agent.isStopped = true; // Permite ao BotCombat virar/atacar
        }
    }

    void OnDeath()
    {
        if(agent.enabled) agent.enabled = false;
    }

    // --------------------------------------------------------
    // RETREAT (FUGA)
    // --------------------------------------------------------

   void TransitionToRetreat()
    {
        if (currentState == BotState.Retreat) return;
        
        // Devemos ter o atacante/lastTargetPosition para saber para onde fugir
        if (lastTargetPosition == Vector3.zero) return; 

        currentState = BotState.Retreat;
        agent.isStopped = false;
        agent.speed = originalSpeed * retreatSpeedMultiplier;

        // Determina a direção oposta
        Vector3 targetDirection = (target != null) ? target.position : lastTargetPosition;
        Vector3 runDirection = transform.position - targetDirection;
        
        // Calcula um ponto seguro para correr (longe do Player)
        Vector3 runPoint = transform.position + runDirection.normalized * viewRadius * 2f;
        
        // Encontra o ponto mais próximo no NavMesh para onde fugir
        if (NavMesh.SamplePosition(runPoint, out NavMeshHit hit, viewRadius * 2.5f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            TransitionToPatrol(); // Falhou a encontrar rota de fuga, volta à patrulha
        }
    }

    void DoRetreat()
    {
        // Se a vida já não estiver crítica, pode sair de Retreat
        float healthRatio = health.currentHealth / health.maxHealth;
        if (healthRatio > healthThreshold + 0.1f) // +0.1f para dar margem de segurança
        {
            TransitionToPatrol();
            return;
        }

        // Se chegou ao ponto de fuga E a vida AINDA está baixa
        if (agent.remainingDistance < waypointTolerance && !agent.pathPending)
        {
            // Fica parado e espera. Não volta a Patrulha.
            agent.isStopped = true; 
            
            // Sugestão: Podia aqui tocar uma animação de "esconder-se" ou "curar-se"
            // Por agora, o bot fica parado até a vida subir (o que não acontecerá se não tivermos Pickups de Saúde)
        }
        else
        {
            agent.isStopped = false; // Continua a correr para o ponto de fuga
        }
        
        // Se o alvo aparecer no FOV, não persegue, continua a fugir.
    }

    // --------------------------------------------------------
    // UTILITÁRIOS
    // --------------------------------------------------------

    void DetectPlayer()
    {
        if (target && HasLineOfSight(target)) return;
        if (currentState == BotState.Retreat) return; 

        var hits = Physics.OverlapSphere(transform.position, viewRadius, targetMask);
        foreach (var h in hits)
        {
            var cand = h.transform;
            Vector3 dir = (cand.position - transform.position).normalized;
            if (Vector3.Angle(transform.forward, dir) <= viewAngle * 0.5f && HasLineOfSight(cand))
            {
                target = cand;
                lastTargetPosition = target.position; 
                TransitionToChase(); 
                break;
            }
        }
    }

    bool HasLineOfSight(Transform t)
    {
        Vector3 eyes = transform.position + Vector3.up * 1.2f;
        Vector3 dest = t.position + Vector3.up * 1.2f;
        Vector3 dir = dest - eyes;

        if (Physics.Raycast(eyes, dir, out RaycastHit hit, dir.magnitude, obstacleMask))
        {
            return false;
        }

        return true;
    }

    void UpdateAnimation()
    {
        if (!animator || !agent) return;

        Vector3 vel = agent.velocity;
        vel.y = 0f;

        float speed = agent.enabled ? vel.magnitude * speedMultiplier : 0f; 
        
        // CORREÇÃO: Usa o parâmetro "Speed" (funcional)
        animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);

        // ERRO RESOLVIDO: Removida a linha que causava a exceção.
        // animator.SetBool("IsChasing", target != null && currentState != BotState.Retreat);
    }
}