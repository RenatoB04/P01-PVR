using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BotAI_Proto : MonoBehaviour
{
    public enum State { Idle, Patrol, Chase, Attack }

    [Header("Percepção")]
    public float viewRadius = 15f;
    [Range(0, 180)] public float viewAngle = 110f;
    public LayerMask targetMask;  // Player
    public LayerMask obstacleMask;  // Environment

    [Header("Patrulha")]
    public Transform[] patrolPoints;
    public float waypointTolerance = 0.6f;

    [Header("Chase")]
    public float stoppingDistance = 2f;
    public float loseSightTime = 2f;

    NavMeshAgent agent;
    State state = State.Idle;
    int patrolIndex = -1;
    Transform target;
    float losTimer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (patrolPoints != null && patrolPoints.Length > 0)
            state = State.Patrol;
    }

    void Update()
    {
        DetectPlayer();

        switch (state)
        {
            case State.Idle:
                if (target) state = State.Chase;
                break;

            case State.Patrol:
                DoPatrol();
                if (target) state = State.Chase;
                break;

            case State.Chase:
                DoChase();
                break;

            case State.Attack:
                if (!target) state = State.Patrol;
                break;
        }
    }

    void DoPatrol()
    {
        if (!agent.hasPath || agent.remainingDistance < waypointTolerance)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            agent.stoppingDistance = 0f;
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }
    }

    void DoChase()
    {
        if (!target) { state = State.Patrol; return; }

        agent.stoppingDistance = stoppingDistance;
        agent.SetDestination(target.position);

        bool canSee = HasLineOfSight(target);
        float dist = Vector3.Distance(transform.position, target.position);

        if (canSee)
        {
            losTimer = 0f;
            if (dist <= stoppingDistance + 0.5f) state = State.Attack;
        }
        else
        {
            losTimer += Time.deltaTime;
            if (losTimer > loseSightTime)
            {
                losTimer = 0f;
                target = null;
                state = (patrolPoints.Length > 0) ? State.Patrol : State.Idle;
            }
        }
    }

    void DetectPlayer()
    {
        if (target) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, viewRadius, targetMask);
        foreach (var h in hits)
        {
            Transform cand = h.transform;
            Vector3 dir = (cand.position - transform.position).normalized;
            if (Vector3.Angle(transform.forward, dir) < viewAngle * 0.5f && HasLineOfSight(cand))
            {
                target = cand;
                state = State.Chase;
                break;
            }
        }
    }

    bool HasLineOfSight(Transform t)
    {
        Vector3 eyes = transform.position + Vector3.up * 1.2f;
        Vector3 dest = t.position + Vector3.up * 1.2f;
        Vector3 dir = dest - eyes;
        if (Physics.Raycast(eyes, dir.normalized, out var hit, dir.magnitude, ~0))
        {
            if (((1 << hit.collider.gameObject.layer) & obstacleMask) != 0) return false;
        }
        return true;
    }
}
