using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NetworkUI_TestButtons
/// ---------------------------------------------------------------------------
/// Script de teste para iniciar Host, Client ou Server manualmente via UI.
/// Permite também spawnar o jogador quando um cliente se liga, caso ainda não tenha.
/// ---------------------------------------------------------------------------
/// Observações de networking:
/// - Integra-se diretamente com o NetworkManager.Singleton.
/// - Usa o evento OnClientConnectedCallback para spawnar jogadores no servidor.
/// - Suporta prefabs de jogadores com NetworkObject no root.
/// ---------------------------------------------------------------------------
/// </summary>
public class NetworkUI_TestButtons : MonoBehaviour
{
    [Header("Botões")]
    public Button hostButton;     // Botão para iniciar Host (Server + Client)
    public Button clientButton;   // Botão para iniciar Client
    public Button serverButton;   // Botão para iniciar Server puro

    [Header("Prefab do Jogador")]
    [Tooltip("Arrasta o teu Prefab do Jogador (com NetworkObject no root).")]
    public GameObject playerPrefabToSpawn;

    void Start()
    {
        // Configura listeners dos botões, garantindo que não há duplicações
        if (hostButton)
        {
            hostButton.onClick.RemoveListener(StartHost);
            hostButton.onClick.AddListener(StartHost);
        }
        if (clientButton)
        {
            clientButton.onClick.RemoveListener(StartClient);
            clientButton.onClick.AddListener(StartClient);
        }
        if (serverButton)
        {
            serverButton.onClick.RemoveListener(StartServer);
            serverButton.onClick.AddListener(StartServer);
        }

        // Associa callback para spawn de jogadores quando um cliente se liga
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        else
        {
            Debug.LogError("NetworkManager.Singleton é nulo na cena.");
        }
    }

    /// <summary>
    /// Inicia o Host (Server + Client local)
    /// </summary>
    private void StartHost()
    {
        if (NetworkManager.Singleton == null) { Debug.LogError("Sem NetworkManager."); return; }

        Debug.Log("Starting Host...");
        bool ok = NetworkManager.Singleton.StartHost();
        if (ok) HideButtons();
        else Debug.LogError("Falha ao iniciar Host (porta em uso?).");
    }

    /// <summary>
    /// Inicia o Client e liga ao servidor
    /// </summary>
    private void StartClient()
    {
        if (NetworkManager.Singleton == null) { Debug.LogError("Sem NetworkManager."); return; }

        Debug.Log("Starting Client...");
        bool ok = NetworkManager.Singleton.StartClient();
        if (ok) HideButtons();
        else Debug.LogError("Falha ao iniciar Client.");
    }

    /// <summary>
    /// Inicia apenas o Server
    /// </summary>
    private void StartServer()
    {
        if (NetworkManager.Singleton == null) { Debug.LogError("Sem NetworkManager."); return; }

        Debug.Log("Starting Server...");
        bool ok = NetworkManager.Singleton.StartServer();
        if (ok) HideButtons();
        else Debug.LogError("Falha ao iniciar Server.");
    }

    /// <summary>
    /// Esconde os botões depois de iniciar Host/Client/Server
    /// </summary>
    private void HideButtons()
    {
        if (hostButton) hostButton.gameObject.SetActive(false);
        if (clientButton) clientButton.gameObject.SetActive(false);
        if (serverButton) serverButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Callback chamado quando um cliente se liga ao servidor
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        // Evita spawn duplicado se o cliente já tiver PlayerObject
        if (nm.ConnectedClients.TryGetValue(clientId, out var client) &&
            client != null && client.PlayerObject != null)
        {
            Debug.Log($"[Spawn Skip] Client {clientId} já tem PlayerObject (auto-spawn ou outro script).");
            return;
        }

        // Spawn manual do jogador no servidor
        SpawnPlayer(clientId);
    }

    /// <summary>
    /// Instancia e spawna o prefab do jogador para o cliente especificado
    /// </summary>
    private void SpawnPlayer(ulong clientId)
    {
        if (playerPrefabToSpawn == null)
        {
            Debug.LogError("Player Prefab To Spawn não definido no Inspector!");
            return;
        }

        var instance = Instantiate(playerPrefabToSpawn);
        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("O prefab do jogador NÃO tem NetworkObject no root!");
            Destroy(instance);
            return;
        }

        // Spawn autorizado pelo servidor, associado ao clientId
        netObj.SpawnAsPlayerObject(clientId, true);
        Debug.Log($"Jogador spawnado para Client {clientId}");
    }

    /// <summary>
    /// Remove listener quando o objecto é destruído
    /// </summary>
    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }
}
