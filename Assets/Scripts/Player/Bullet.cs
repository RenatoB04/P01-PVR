using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class BulletProjectile : NetworkBehaviour
{
    [Header("Dano")]
    public float damage = 20f;

    [Header("Vida útil")]
    public float lifeTime = 5f;

    [Header("Filtro (opcional)")]
    public LayerMask hittableLayers = ~0;

    [HideInInspector] public int   ownerTeam     = -1;
    [HideInInspector] public Transform ownerRoot = null;
    [HideInInspector] public ulong ownerClientId = ulong.MaxValue; // para scoreboard e ignorar self

    // Velocidade inicial para clientes aplicarem localmente
    public NetworkVariable<Vector3> initialVelocity = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool hasHit = false;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Mantém interpolation à tua escolha; a simulação será local também
            // rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
        var col = GetComponent<Collider>();
        if (col) col.enabled = true;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Aplica a velocity inicial no lado do cliente para evitar projéctil parado
        if (!IsServer && rb != null)
        {
            if (initialVelocity.Value != Vector3.zero)
                rb.linearVelocity = initialVelocity.Value;
        }

        if (IsServer)
            Invoke(nameof(ServerLifetimeEnd), lifeTime);
    }

    void ServerLifetimeEnd()
    {
        if (!IsServer) return;
        var no = GetComponent<NetworkObject>();
        if (no && no.IsSpawned) no.Despawn();
        else Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        if (!IsServer) return;
        if (hasHit) return;

        // Ignora colisão com o próprio atirador
        if (ownerRoot && c.transform.root == ownerRoot) return;
        var otherRootNO = c.transform.root.GetComponent<NetworkObject>();
        if (ownerClientId != ulong.MaxValue && otherRootNO && otherRootNO.OwnerClientId == ownerClientId) return;

        if (((1 << c.gameObject.layer) & hittableLayers) == 0) { ServerCleanup(); return; }

        Vector3 hitPos = transform.position;
        if (c.contactCount > 0) hitPos = c.GetContact(0).point;
        ProcessHitServer(c.collider, hitPos);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (hasHit) return;

        // Ignora colisão com o próprio atirador
        if (ownerRoot && other.transform.root == ownerRoot) return;
        var otherRootNO = other.transform.root.GetComponent<NetworkObject>();
        if (ownerClientId != ulong.MaxValue && otherRootNO && otherRootNO.OwnerClientId == ownerClientId) return;

        if (((1 << other.gameObject.layer) & hittableLayers) == 0) { ServerCleanup(); return; }

        ProcessHitServer(other, transform.position);
    }

    private void ProcessHitServer(Collider col, Vector3 hitPos)
    {
        if (hasHit) return;
        hasHit = true;

        var h = col.GetComponentInParent<Health>();
        if (h != null)
        {
            // Aplica dano directamente no servidor
            h.ApplyDamageServer(damage, ownerTeam, ownerClientId, hitPos, true);

            // Hitmarker apenas para o atirador (ClientRpc dirigido)
            if (ownerClientId != ulong.MaxValue)
            {
                var target = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { ownerClientId } }
                };
                HitmarkerClientRpc((float)damage, h.name, target);
            }
        }

        ServerCleanup();
    }

    [ClientRpc]
    void HitmarkerClientRpc(float dealt, string victimName, ClientRpcParams rpcParams = default)
    {
        if (DamageFeedUI.Instance)
            DamageFeedUI.Instance.Push(dealt, false, victimName);
        CrosshairUI.Instance?.ShowHit();
    }

    private void ServerCleanup()
    {
        if (!IsServer) return;
        var no = GetComponent<NetworkObject>();
        if (no && no.IsSpawned) no.Despawn();
        else Destroy(gameObject);
    }
}