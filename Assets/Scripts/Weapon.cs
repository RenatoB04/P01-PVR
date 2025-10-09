using UnityEngine;
using UnityEngine.InputSystem;

public class Weapon : MonoBehaviour
{
    [Header("Refs")]
    public Transform firePoint;
    [SerializeField] GameObject bulletPrefab;
    [SerializeField] Transform cam;
    [SerializeField] ParticleSystem muzzleFlash;   // prefab do efeito visual
    [SerializeField] AudioSource fireAudio;        // som do disparo

    [Header("Input")]
    [SerializeField] InputActionReference shootAction;

    [Header("Settings")]
    [SerializeField] float bulletSpeed = 40f;
    [SerializeField] float fireRate = 0.12f;
    [SerializeField] float maxAimDistance = 200f;

    float nextFire;
    CharacterController playerCC;

    void Awake()
    {
        if (!cam && Camera.main) cam = Camera.main.transform;
        playerCC = GetComponentInParent<CharacterController>();
    }

    void OnEnable()
    {
        if (shootAction) shootAction.action.Enable();
    }

    void OnDisable()
    {
        if (shootAction) shootAction.action.Disable();
    }

    void Update()
    {
        if (shootAction && shootAction.action.WasPressedThisFrame())
        {
            if (Time.time >= nextFire)
            {
                Shoot();
                nextFire = Time.time + fireRate;
            }
        }
    }

public void ShootExternally()
{
    // se usas "nextFire" + "fireRate" na tua arma, isto respeita a cadência
    if (Time.time >= nextFire)
    {
        Shoot();
        nextFire = Time.time + fireRate;
    }
}

    void Shoot()
    {
        if (!bulletPrefab || !firePoint) return;

        // direção do disparo (alinha ao ponto que a câmera mira)
        Vector3 dir;
        Ray ray = new Ray(cam ? cam.position : firePoint.position, cam ? cam.forward : firePoint.forward);
        if (Physics.Raycast(ray, out var hit, maxAimDistance, ~0, QueryTriggerInteraction.Ignore))
            dir = (hit.point - firePoint.position).normalized;
        else
            dir = (ray.GetPoint(maxAimDistance) - firePoint.position).normalized;

        // criar bala
        var bullet = Instantiate(bulletPrefab, firePoint.position, Quaternion.LookRotation(dir));

        // equipa e root do dono (para FF e evitar auto-dano)
        if (bullet.TryGetComponent<BulletProjectile>(out var bp))
        {
            var h = GetComponentInParent<Health>();
            if (h) bp.ownerTeam = h.team;          // ex.: 0 para player
            bp.ownerRoot = h ? h.transform.root : transform.root;
        }

        // afastar um pouco para não colidir logo com a própria arma
        bullet.transform.position += dir * 0.2f;

        // aplicar velocidade (usa velocity, não linearVelocity)
        if (bullet.TryGetComponent<Rigidbody>(out var rb))
            rb.linearVelocity = dir * bulletSpeed;

        // VFX
        if (muzzleFlash)
        {
            var flash = Instantiate(muzzleFlash, firePoint.position, firePoint.rotation, firePoint);
            flash.Play();
            Destroy(flash.gameObject, 0.2f);
        }

        // som
        if (fireAudio && fireAudio.clip) fireAudio.PlayOneShot(fireAudio.clip);
    }

}
