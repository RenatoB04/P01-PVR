using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class BulletProjectile : MonoBehaviour
{
    [Header("Dano")]
    public float damage = 20f;

    [Header("Vida útil")]
    public float lifeTime = 5f;

    [HideInInspector] public int ownerTeam = -1;
    [HideInInspector] public Transform ownerRoot;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    void OnCollisionEnter(Collision c)
    {
        // Evita auto-hit (atingir quem disparou)
        if (ownerRoot && c.transform.root == ownerRoot)
            return;

        var h = c.collider.GetComponentInParent<Health>();
        if (h)
        {
            Vector3 hitPos = c.GetContact(0).point;

            // Chama a nova função que ativa Damage Indicator se for o jogador
            h.TakeDamageFrom(damage, ownerTeam, ownerRoot ? ownerRoot : transform, hitPos);

            // Hit marker (quando o jogador acerta num alvo)
            CrosshairUI.Instance?.ShowHit();
        }

        Destroy(gameObject);
    }
}
