using UnityEngine;

public class PulseEffect : MonoBehaviour
{
    public float maxRadius = 4f;
    public float expansionSpeed = 20f; // Velocidade de crescimento

    private float currentRadius = 0.1f;

    void Update()
    {
        // 1. Aumenta o raio
        currentRadius += expansionSpeed * Time.deltaTime;

        // 2. Atualiza o tamanho do objeto (multiplicamos por 2 porque a escala 1 = 0.5 raio)
        transform.localScale = Vector3.one * currentRadius * 2f;

        // 3. Se já chegou ao tamanho máximo, destroi-se
        if (currentRadius >= maxRadius)
        {
            Destroy(gameObject);
        }
    }
}