using UnityEngine;

public class ResourcePickup : MonoBehaviour
{
    [Header("Recursos")]
    public float healthAmount = 0f;        // quanto cura
    public int ammoReserveAmount = 0;      // quantos "carregadores" dá (1 = 1 mag)
    public string targetTag = "Player";    // tag do player

    [Header("Efeitos")]
    public AudioClip pickupSound;
    public GameObject pickupVFX;

    // Proteção anti-múltiplas ativações no mesmo frame
    bool pickedUp = false;

    void OnTriggerEnter(Collider other)
    {
        // se já foi apanhado uma vez, ignora chamadas repetidas
        if (pickedUp) return;

        // verificar se quem entrou é o player (objeto com Tag Player
        // ou algum filho dele)
        if (!other.CompareTag(targetTag) && !other.transform.root.CompareTag(targetTag))
        {
            return;
        }

        // marcamos como apanhado para não repetir a lógica
        pickedUp = true;

        // tenta obter componentes do jogador
        Transform playerRoot = other.transform.root;

        // tentar encontrar Health e Weapon no collider atingido...
        Health playerHealth = other.GetComponent<Health>();
        Weapon playerWeapon = other.GetComponent<Weapon>();

        // ...se não estiverem no collider em si, tenta no root
        if (playerHealth == null) playerHealth = playerRoot.GetComponent<Health>();
        if (playerWeapon == null) playerWeapon = playerRoot.GetComponent<Weapon>();

        // ...e finalmente tenta em filhos do player (ex: WeaponHolder / arma na mão)
        if (playerWeapon == null) playerWeapon = playerRoot.GetComponentInChildren<Weapon>(true);

        // aplicar cura
        if (healthAmount > 0f)
        {
            if (playerHealth != null)
            {
                playerHealth.Heal(healthAmount);
            }
            else
            {
                Debug.LogWarning("Pickup: Falhou ao encontrar componente Health no Player.");
            }
        }

        // aplicar munição
        if (ammoReserveAmount > 0)
        {
            if (playerWeapon != null)
            {
                // AddReserveAmmo já trata de converter '1' em 1 carregador
                playerWeapon.AddReserveAmmo(ammoReserveAmount);
            }
            else
            {
                Debug.LogWarning("Pickup: Falhou ao encontrar componente Weapon no Player.");
            }
        }

        // se conseguimos interagir com pelo menos um dos dois (vida ou munição),
        // toca efeitos e elimina o pickup
        if (playerHealth != null || playerWeapon != null)
        {
            if (pickupSound)
            {
                AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            }

            if (pickupVFX)
            {
                Instantiate(pickupVFX, transform.position, Quaternion.identity);
            }

            // desativa o collider imediatamente para não voltar a disparar neste frame
            Collider col = GetComponent<Collider>();
            if (col) col.enabled = false;

            // destrói o pickup
            Destroy(gameObject);
        }
    }
}