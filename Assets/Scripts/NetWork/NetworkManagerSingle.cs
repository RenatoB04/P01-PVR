using UnityEngine;
using Unity.Netcode;

/// <summary>
/// NetcodeBootstrap
/// ---------------------------------------------------------------------------
/// Componente responsável por garantir que exista apenas uma instância do 
/// NetworkManager e configuração relacionada ao Netcode for GameObjects.
/// Mantém-se persistente entre cenas, evitando re-instanciação acidental.
/// ---------------------------------------------------------------------------
/// Observações de networking:
/// - Garante que a inicialização do Netcode ocorre apenas uma vez.
/// - Evita múltiplos objetos de bootstrap que poderiam conflitar com o NetworkManager.
/// - Utiliza DontDestroyOnLoad para persistir entre mudanças de cena.
/// ---------------------------------------------------------------------------
/// </summary>
public class NetcodeBootstrap : MonoBehaviour
{
    void Awake()
    {
        // Procura outras instâncias deste mesmo script na cena
        var others = FindObjectsOfType<NetcodeBootstrap>();

        // Se existir mais de uma instância, destrói esta (previne duplicação)
        if (others.Length > 1)
        {
            Destroy(gameObject);
            return;
        }

        // Mantém este objeto vivo entre cenas
        DontDestroyOnLoad(gameObject);
    }
}