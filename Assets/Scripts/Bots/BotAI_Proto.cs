using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;
using Unity.Netcode; // Importante para o IsServer (embora a IA deva correr só no servidor)

// O BotAI deve ser um NetworkBehaviour para poder ler NetworkVariables
// e garantir que a lógica só corre no servidor.
public class BotAI_Proto : NetworkBehaviour
{
    // === ESTADOS DA IA ===
    enum BotState { Patrol, Chase, Search, Attack, Retreat }

    [Header("Debug")]
    [SerializeField]
    private BotState currentState = BotState.Patrol;

    [Header("Patrulha")]
    public Transform[] patrolPoints; // PREENCHIDO PELO SPAWNER
    public float waypointTolerance = 1.5f;

    [Header("Visão")]
    public float viewRadius = 15f;
    [Range(0, 180)] public float viewAngle = 110f;
    public LayerMask targetMask;     // O que o bot considera um alvo (ex: Layer "Player")
    public LayerMask obstacleMask;   // O que bloqueia a visão (ex: Layer "Default", "Wall")
    [Tooltip("Tempo (s) que o bot continua a perseguir após perder visão (antes de ir para 'Search').")]
    public float loseSightChaseTime = 5f;
    [Tooltip("Tempo (s) que o bot espera no último local conhecido, procurando.")]
    public float searchWaitTime = 5.0f;

    [Header("Combate e Fuga")]
    [Tooltip("Distância para parar de perseguir e começar a atacar.")]
    public float attackDistance = 8f;
    [Tooltip("Distância para parar de atacar e volta a perseguir.")]
    public float chaseDistance = 10f;
    [Tooltip("Percentagem de vida (0.0 a 1.0) para começar a fugir.")]
    [Range(0f, 1f)] public float healthThreshold = 0.3f;
    [Tooltip("Multiplicador de velocidade ao fugir.")]
    public float retreatSpeedMultiplier = 1.5f;
    [Tooltip("Tempo (s) que o bot espera no ponto de fuga antes de reavaliar.")]
    public float retreatReassessTime = 2.0f;

    [Header("Animação")]
    public Animator animator;
    [Tooltip("Ajuste para sincronizar velocidade da animação com a do NavMeshAgent.")]
    public float animationSpeedMultiplier = 1.0f;

    // --- Componentes e Estado Interno ---
    private NavMeshAgent agent;
    private Health health;
    private Transform target;
    private int patrolIndex = -1;
    private float loseTargetTimer;
    private float searchTimer;
    private float retreatTimer;
    private Vector3 lastKnownTargetPosition;
    private float originalSpeed;
    private bool lostSightMessageShown = false;

    // --- Inicialização ---
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();

        if (!animator) animator = GetComponentInChildren<Animator>();
        if (animator) animator.applyRootMotion = false;

        if (agent) originalSpeed = agent.speed;
        agent.stoppingDistance = 0.0f;
    }

    public override void OnNetworkSpawn()
    {
        // --- CONTROLO DA IA ---
        // A IA (cérebro) SÓ DEVE CORRER NO SERVIDOR (HOST).
        // Clientes não precisam de pensar pelos bots, eles só recebem a posição.
        // (Vamos assumir que o prefab do Bot tem um NetworkTransform para sincronizar a posição,
        // mas a lógica da IA só corre aqui).
        if (!IsServer)
        {
            // Se eu não sou o servidor, desativo a IA e o NavMeshAgent.
            // Eu sou só um "fantoche" à espera de ordens da rede.
            agent.enabled = false;
            this.enabled = false; // Desativa este script (Update, etc.)
            return;
        }

        // Se sou o Servidor, continuo a inicialização
        if (health)
        {
            health.OnDied.RemoveListener(OnDeath); health.OnDied.AddListener(OnDeath);
            health.OnTookDamage -= HandleTookDamage; health.OnTookDamage += HandleTookDamage;
        }

        target = null;
        patrolIndex = -1;
        currentState = BotState.Patrol;
        if (agent && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = originalSpeed;
            SetNextPatrolPoint();
        }
    }


    public override void OnNetworkDespawn()
    {
        // Limpa subscrições (mesmo no servidor)
        if (health)
        {
            health.OnDied.RemoveListener(OnDeath);
            health.OnTookDamage -= HandleTookDamage;
        }
        if (agent && agent.isActiveAndEnabled)
        {
            agent.isStopped = true;
            if(agent.isOnNavMesh) agent.ResetPath();
        }
    }

    // --- Ciclo Principal ---
    void Update()
    {
        // Se este script está a correr, já sabemos que somos o Servidor (devido ao OnNetworkSpawn)
        
        if (health == null || health.isDead.Value || !agent || !agent.isOnNavMesh) // <<< USA .Value
        {
            if (agent && agent.isActiveAndEnabled) agent.isStopped = true;
            return;
        }

        if (currentState != BotState.Retreat)
        {
            DetectTarget();
        }

        UpdateStateMachine();
        UpdateAnimation();
    }

    // --- Máquina de Estados ---
    private void UpdateStateMachine()
    {
        switch (currentState)
        {
            case BotState.Patrol: DoPatrol(); break;
            case BotState.Chase: DoChase(); break;
            case BotState.Search: DoSearch(); break;
            case BotState.Attack: DoAttack(); break;
            case BotState.Retreat: DoRetreat(); break;
        }
    }

    // --- Lógica de Cada Estado ---

    private void DoPatrol()
    {
        agent.isStopped = false;
        agent.speed = originalSpeed;

        if ((!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + waypointTolerance) || !agent.hasPath)
        {
            SetNextPatrolPoint();
        }
    }


    private void DoChase()
    {
        agent.isStopped = false;
        agent.speed = originalSpeed;

        if (target && HasLineOfSight(target))
        {
            lastKnownTargetPosition = target.position;
            loseTargetTimer = loseSightChaseTime;
            lostSightMessageShown = false;
        }
        else
        {
            if (!lostSightMessageShown)
            {
                // Debug.Log(gameObject.name + " LOST Line of Sight. Starting lose timer...");
                lostSightMessageShown = true;
            }

            loseTargetTimer -= Time.deltaTime;
            if (loseTargetTimer <= 0)
            {
                TransitionToSearch();
                return;
            }
        }

        if (agent.destination != lastKnownTargetPosition)
        {
            agent.SetDestination(lastKnownTargetPosition);
        }

        float distanceSqr = (transform.position - lastKnownTargetPosition).sqrMagnitude;
        if (distanceSqr <= attackDistance * attackDistance)
        {
            TransitionToAttack();
        }
    }

    private void DoSearch()
    {
        agent.isStopped = false;
        agent.speed = originalSpeed * 0.75f;

        if (agent.destination != lastKnownTargetPosition && (!agent.hasPath || agent.remainingDistance > waypointTolerance))
        {
             agent.SetDestination(lastKnownTargetPosition);
        }

        if (!agent.pathPending && agent.remainingDistance <= waypointTolerance)
        {
            agent.isStopped = true;
            searchTimer -= Time.deltaTime;
            if (searchTimer <= 0)
            {
                TransitionToPatrol();
            }
        }
    }

    private void DoAttack()
    {
        agent.isStopped = true;

        if (target == null)
        {
            TransitionToSearch();
            return;
        }

        Vector3 direction = (target.position - transform.position);
        direction.y = 0;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * agent.angularSpeed * 1.5f);
        }

        float distanceSqr = (transform.position - target.position).sqrMagnitude;
        if (distanceSqr > chaseDistance * chaseDistance)
        {
            TransitionToChase();
        }
        else if (!HasLineOfSight(target))
        {
             TransitionToSearch();
        }
    }

    private void DoRetreat()
    {
        agent.isStopped = false;

        if (!agent.pathPending && agent.remainingDistance < 1.0f)
        {
            agent.isStopped = true;
            retreatTimer -= Time.deltaTime;

            if (retreatTimer <= 0)
            {
                 // CORREÇÃO (CS0019 - Linha 399): Usa .Value para ler a vida
                 bool stillLowHealth = (health.currentHealth.Value / health.maxHealth) <= healthThreshold;
                 bool stillSeeTarget = target && HasLineOfSight(target);

                if (stillLowHealth && stillSeeTarget)
                {
                    FindAndSetFleePoint();
                }
                else
                {
                    TransitionToPatrol();
                }
            }
        }
    }

    // --- Métodos de Transição de Estado ---

    private void TransitionToPatrol()
    {
        currentState = BotState.Patrol;
        agent.speed = originalSpeed;
        agent.isStopped = false;
        target = null;
        lostSightMessageShown = false;
        SetNextPatrolPoint();
    }

    private void TransitionToChase()
    {
        currentState = BotState.Chase;
        agent.speed = originalSpeed;
        agent.isStopped = false;
        loseTargetTimer = loseSightChaseTime;
        lostSightMessageShown = false;
    }

    private void TransitionToSearch()
    {
        currentState = BotState.Search;
        agent.speed = originalSpeed * 0.75f;
        agent.isStopped = false;
        searchTimer = searchWaitTime;
        lostSightMessageShown = false;
    }

    private void TransitionToAttack()
    {
        currentState = BotState.Attack;
        agent.isStopped = true;
        lostSightMessageShown = false;
    }

    private void TransitionToRetreat()
    {
         if (target == null && lastKnownTargetPosition == Vector3.zero)
         {
            TransitionToPatrol();
            return;
         }

        currentState = BotState.Retreat;
        agent.speed = originalSpeed * retreatSpeedMultiplier;
        agent.isStopped = false;
        lostSightMessageShown = false;

        FindAndSetFleePoint();
    }

    // --- Lógica de Eventos e Funções Auxiliares ---

    private void HandleTookDamage(float damageAmount, Transform attacker)
    {
        if (health.isDead.Value) return; // <<< USA .Value

        target = attacker;
        lastKnownTargetPosition = attacker ? attacker.position : transform.position;
        
        // CORREÇÃO (CS0019 - Linha 292): Usa .Value para ler a vida
        float healthRatio = health.currentHealth.Value / health.maxHealth;
        
        if (healthRatio <= healthThreshold && currentState != BotState.Retreat)
        {
            TransitionToRetreat();
        }
        else if (currentState != BotState.Retreat)
        {
             TransitionToChase();
        }
    }

    private void OnDeath()
    {
        if(agent && agent.isActiveAndEnabled) agent.enabled = false;
        if (animator) animator.SetFloat("Speed", 0f);
        currentState = BotState.Patrol;
        target = null;
    }

    private void SetNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            if (agent && agent.isOnNavMesh) agent.isStopped = true;
            return;
        }

        patrolIndex = (patrolIndex + 1) % patrolPoints.Length;

        if (patrolPoints[patrolIndex] == null)
        {
             if (agent && agent.isOnNavMesh) agent.isStopped = true;
             return;
        }

        if (agent && agent.isOnNavMesh)
        {
            agent.SetDestination(patrolPoints[patrolIndex].position);
            agent.isStopped = false;
        }
    }

    private void FindAndSetFleePoint()
    {
        Vector3 fleeFromPos = (target != null) ? target.position : lastKnownTargetPosition;
        if (fleeFromPos == Vector3.zero) fleeFromPos = transform.position + transform.forward * -10f;

        Vector3 runDirection = transform.position - fleeFromPos;
        runDirection.y = 0;
        Vector3 runPoint = transform.position + runDirection.normalized * (viewRadius * 1.2f);

        if (NavMesh.SamplePosition(runPoint, out NavMeshHit hit, viewRadius * 1.5f, NavMesh.AllAreas))
        {
             if(agent.isOnNavMesh) agent.SetDestination(hit.position);
             retreatTimer = retreatReassessTime;
             agent.isStopped = false;
        }
        else
        {
            TransitionToPatrol();
        }
    }

    private void DetectTarget()
    {
        if (currentState == BotState.Attack || currentState == BotState.Retreat) return;

        Collider[] hitsInViewRadius = Physics.OverlapSphere(transform.position, viewRadius, targetMask, QueryTriggerInteraction.Ignore);
        Transform closestVisibleTarget = null;
        float minDistanceSqr = viewRadius * viewRadius + 1f;

        foreach (var hitCollider in hitsInViewRadius)
        {
            if (hitCollider.transform.root == transform.root) continue;

             Health targetHealth = hitCollider.GetComponentInParent<Health>();
             if (targetHealth == null || targetHealth.isDead.Value) continue; // <<< USA .Value

            Transform potentialTarget = hitCollider.transform;
            Vector3 directionToTarget = potentialTarget.position - transform.position;
            float distanceSqr = directionToTarget.sqrMagnitude;

            if (distanceSqr > minDistanceSqr) continue;

            float angleToTarget = Vector3.Angle(transform.forward, directionToTarget.normalized);
            if (angleToTarget <= viewAngle * 0.5f)
            {
                if (HasLineOfSight(potentialTarget))
                {
                    minDistanceSqr = distanceSqr;
                    closestVisibleTarget = potentialTarget;
                }
            }
        }

        if (closestVisibleTarget != null)
        {
            if (target != closestVisibleTarget)
            {
                target = closestVisibleTarget;
                TransitionToChase();
            }
            else if (currentState == BotState.Search)
            {
                 TransitionToChase();
            }
        }
    }


    private bool HasLineOfSight(Transform t)
    {
        if (t == null) return false;

        Vector3 eyesPosition = transform.position + Vector3.up * 1.6f + transform.forward * 0.3f;
        Vector3 targetCenter = t.position + Vector3.up * 1.0f;
        Collider targetCollider = t.GetComponentInChildren<Collider>();
        if (targetCollider) targetCenter = targetCollider.bounds.center;

        Vector3 direction = targetCenter - eyesPosition;
        float distance = direction.magnitude;

        if (Physics.Raycast(eyesPosition, direction.normalized, out RaycastHit hit, distance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.root != t.root)
            {
                return false;
            }
        }
        return true;
    }

    private void UpdateAnimation()
    {
        if (!animator || !agent || !agent.isActiveAndEnabled)
        {
            if (animator) animator.SetFloat("Speed", 0f);
            return;
        }

        Vector3 desiredVelocity = agent.desiredVelocity;
        Vector3 localDesiredVel = transform.InverseTransformDirection(desiredVelocity);
        float speed = localDesiredVel.z / agent.speed;
        speed = Mathf.Clamp01(speed);
        animator.SetFloat("Speed", speed * animationSpeedMultiplier, 0.1f, Time.deltaTime);
    }
}