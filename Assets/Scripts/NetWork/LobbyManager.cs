using System;
using System.Text;
using System.Threading.Tasks;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LOBBY MANAGER
/// ------------------------------------------------------------------------
/// Este script implementa toda a camada de networking de alto nível (metajogo),
/// funcionando como ponto de ligação entre:
///
/// • Photon PUN (TCP)  → matchmaking, salas, lobby, sincronização fiável
/// • Netcode for GameObjects + Relay (UDP) → gameplay em tempo real
///
/// O Photon é utilizado exclusivamente como sistema de coordenação e sinalização,
/// enquanto o gameplay nunca passa pelo Photon, garantindo melhor desempenho
/// e menor latência durante a partida.
/// </summary>
public class LobbyManager : MonoBehaviourPunCallbacks
{
    // =====================================================================
    //                                UI
    // =====================================================================
    // Todos estes elementos apenas afetam o estado visual e interação do lobby,
    // não tendo impacto direto no tráfego de rede.

    [Header("UI (TMP)")]
    [SerializeField] TMP_InputField ifPlayerName;
    [SerializeField] Button btnConnect;
    [SerializeField] Button btnCreate;
    [SerializeField] TMP_Text txtCreatedCode;
    [SerializeField] TMP_InputField ifJoinCode;
    [SerializeField] Button btnJoin;
    [SerializeField] Button btnLeave;
    [SerializeField] TMP_Text txtStatus;
    [SerializeField] Button btnStartGame;
    [SerializeField] TMP_Text txtCountdown;
    [SerializeField] Button btnPlayBots;

    // =====================================================================
    //                           CONFIGURAÇÃO
    // =====================================================================
    // Parâmetros que definem o comportamento do lobby e da partida.

    [Header("Config")]
    [SerializeField] string gameSceneName = "Prototype";
    [SerializeField] int roomCodeLength = 6;
    [SerializeField] int maxPlayers = 2;
    [SerializeField] int countdownSeconds = 3;

    /// <summary>
    /// Chave usada nas Custom Room Properties do Photon.
    /// Aqui é guardado o Join Code do Unity Relay.
    ///
    /// Este campo é essencial porque permite que o Photon funcione como
    /// sistema de sinalização para iniciar o NGO.
    /// </summary>
    const string ROOM_PROP_RELAY = "relay";

    // Flags de estado locais para evitar múltiplos arranques
    bool _matchStarted = false;
    bool _isCountingDown = false;

    // =====================================================================
    //                               SETUP
    // =====================================================================

    void Awake()
    {
        // Desativa a sincronização automática de cenas do Photon.
        // A partir do momento em que o jogo começa, a autoridade
        // sobre cenas passa para o Netcode for GameObjects.
        PhotonNetwork.AutomaticallySyncScene = false;

        // Mantém o jogo ativo mesmo em segundo plano
        Application.runInBackground = true;

        // Estado inicial da UI
        SetUIConnected(false);
        SetUILobbyActions(false);

        if (btnStartGame) btnStartGame.gameObject.SetActive(false);
        if (txtCountdown) txtCountdown.gameObject.SetActive(false);

        Log("Pronto. Define nome e carrega Conectar.");

        // Registo dos eventos de UI
        btnConnect.onClick.AddListener(OnClickConnect);
        btnCreate.onClick.AddListener(OnClickCreate);
        btnJoin.onClick.AddListener(OnClickJoin);
        btnLeave.onClick.AddListener(OnClickLeave);
        if (btnStartGame) btnStartGame.onClick.AddListener(OnClickStartGame);
        if (btnPlayBots) btnPlayBots.onClick.AddListener(OnClickPlayWithBots);
    }

    void OnDestroy()
    {
        // Remoção explícita de listeners
        // Importante para evitar referências pendentes em reloads de cena
        btnConnect.onClick.RemoveAllListeners();
        btnCreate.onClick.RemoveAllListeners();
        btnJoin.onClick.RemoveAllListeners();
        btnLeave.onClick.RemoveAllListeners();
        if (btnStartGame) btnStartGame.onClick.RemoveAllListeners();
        if (btnPlayBots) btnPlayBots.onClick.RemoveAllListeners();
    }

    // =====================================================================
    //                         GESTÃO DE UI
    // =====================================================================

    void SetUIConnected(bool connected)
    {
        // Quando ligado ao Photon, o jogador deixa de poder alterar o nome
        if (btnConnect) btnConnect.interactable = !connected;
        if (ifPlayerName) ifPlayerName.interactable = !connected;

        // Apenas jogadores ligados podem criar ou entrar em lobbies
        if (btnCreate) btnCreate.interactable = connected;
        if (btnJoin) btnJoin.interactable = connected && !string.IsNullOrEmpty(ifJoinCode?.text);
        if (ifJoinCode) ifJoinCode.interactable = connected;

        if (btnLeave) btnLeave.interactable = false;

        if (txtCreatedCode)
        {
            txtCreatedCode.gameObject.SetActive(false);
            txtCreatedCode.text = "";
        }

        if (btnStartGame) btnStartGame.gameObject.SetActive(false);
    }

    void SetUILobbyActions(bool inRoom)
    {
        // Interface dependente do estado dentro/fora de sala
        if (btnLeave) btnLeave.interactable = inRoom;
        if (btnCreate) btnCreate.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady;
        if (btnJoin) btnJoin.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady && !string.IsNullOrEmpty(ifJoinCode?.text);
        if (ifJoinCode) ifJoinCode.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady;
        if (txtCreatedCode) txtCreatedCode.gameObject.SetActive(inRoom);
    }

    void Log(string msg)
    {
        if (txtStatus) txtStatus.text = msg;
        Debug.Log("[Lobby] " + msg);
    }

    // =====================================================================
    //                       CONEXÃO AO PHOTON
    // =====================================================================

    /// <summary>
    /// Liga o jogador ao Photon Cloud.
    /// O Photon utiliza TCP, garantindo:
    /// • Entrega garantida
    /// • Ordem das mensagens
    /// • Fiabilidade para eventos críticos do lobby
    /// </summary>
    async void OnClickConnect()
    {
        var nick = string.IsNullOrWhiteSpace(ifPlayerName?.text)
            ? ("Player" + UnityEngine.Random.Range(1000, 9999))
            : ifPlayerName.text.Trim();

        PhotonNetwork.NickName = nick;
        Log($"A ligar ao Photon como {PhotonNetwork.NickName}...");

        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();

        // Inicialização dos Unity Services (Relay, Auth)
        await EnsureUnityServicesAsync();
    }

    /// <summary>
    /// Cria uma sala Photon privada, identificada por código.
    /// A sala serve apenas como ponto de encontro e sincronização inicial.
    /// </summary>
    void OnClickCreate()
    {
        string code = GenerateRoomCode(roomCodeLength);

        var options = new RoomOptions
        {
            MaxPlayers = (byte)Mathf.Clamp(maxPlayers, 2, 16),
            IsVisible = false,
            IsOpen = true,

            // Propriedades customizadas usadas como canal de sinalização
            CustomRoomProperties = new Hashtable { { ROOM_PROP_RELAY, "" } },
            CustomRoomPropertiesForLobby = new[] { ROOM_PROP_RELAY }
        };

        Log($"A criar lobby com código {code}...");
        PhotonNetwork.CreateRoom(code, options, TypedLobby.Default);
    }

    void OnClickJoin()
    {
        string code = ifJoinCode?.text?.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            Log("Escreve um código para entrar.");
            return;
        }

        Log($"A entrar no lobby {code}...");
        PhotonNetwork.JoinRoom(code);
    }

    void OnClickLeave()
    {
        if (PhotonNetwork.InRoom)
        {
            Log("A sair do lobby...");
            PhotonNetwork.LeaveRoom();
        }
    }

    /// <summary>
    /// Apenas o Master Client pode iniciar o jogo.
    /// O início é comunicado a todos através de propriedades da sala.
    /// </summary>
    void OnClickStartGame()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (_isCountingDown || _matchStarted) return;

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { "startCountdown", true } }
        );
    }

    // =====================================================================
    //                      CALLBACKS DO PHOTON
    // =====================================================================

    public override void OnConnectedToMaster()
    {
        Log("Ligado ao Master. A entrar no lobby...");
        PhotonNetwork.JoinLobby(TypedLobby.Default);
    }

    public override void OnJoinedLobby()
    {
        Log("Estás no lobby. Podes criar ou entrar por código.");
        SetUIConnected(true);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Log($"Desligado: {cause}");
        SetUIConnected(false);
        SetUILobbyActions(false);
    }

    public override void OnCreatedRoom()
    {
        Log($"Lobby criado. Código: {PhotonNetwork.CurrentRoom?.Name}");
    }

    public override void OnJoinedRoom()
    {
        string code = PhotonNetwork.CurrentRoom.Name;

        Log($"Entraste no lobby ({code}). Espera pelo início do jogo ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");

        if (txtCreatedCode)
        {
            txtCreatedCode.gameObject.SetActive(true);
            txtCreatedCode.text = $"Código: {code}";
        }

        SetUILobbyActions(true);

        // Apenas o Master Client vê o botão de iniciar jogo
        if (PhotonNetwork.IsMasterClient && btnStartGame)
            btnStartGame.gameObject.SetActive(true);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Log($"Entrou: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Log($"{otherPlayer.NickName} saiu. ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    /// <summary>
    /// Este callback é o principal ponto de sincronização do lobby.
    /// Permite reagir a eventos sem RPCs explícitos.
    /// </summary>
    public override async void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        // Início sincronizado do jogo
        if (propertiesThatChanged.ContainsKey("startCountdown"))
        {
            await StartCountdownAndLaunch();
            return;
        }

        // Receção do Join Code do Relay
        if (propertiesThatChanged.ContainsKey(ROOM_PROP_RELAY))
        {
            string joinCode = propertiesThatChanged[ROOM_PROP_RELAY] as string;

            if (!string.IsNullOrEmpty(joinCode) && !IsNgoConnected())
            {
                Log($"Código Relay recebido: {joinCode}. A ligar ao jogo...");
                await StartClientWithRelayAsync(joinCode);
            }
        }
    }

    // =====================================================================
    //               TRANSIÇÃO PHOTON → NGO + RELAY
    // =====================================================================

    async Task StartCountdownAndLaunch()
    {
        if (_isCountingDown) return;
        _isCountingDown = true;

        if (btnStartGame) btnStartGame.interactable = false;
        if (txtCountdown) txtCountdown.gameObject.SetActive(true);

        for (int i = countdownSeconds; i > 0; i--)
        {
            if (txtCountdown) txtCountdown.text = $"Começa em {i}...";
            await Task.Delay(1000);
        }

        if (txtCountdown) txtCountdown.text = "A começar!";

        // Apenas o Master Client cria o servidor
        if (PhotonNetwork.IsMasterClient)
        {
            _matchStarted = true;
            await StartHostWithRelayAndLoadAsync();
        }
    }

    /// <summary>
    /// Criação do Relay + arranque do Host NGO.
    /// A partir daqui, toda a comunicação passa a ser feita via UDP.
    /// </summary>
    async Task StartHostWithRelayAndLoadAsync()
    {
        await EnsureUnityServicesAsync();

        int maxConnections = Mathf.Max(1, maxPlayers - 1);

        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

        Log($"Relay criado. JoinCode: {joinCode}");

        // Envia o join code aos clientes via Photon
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { ROOM_PROP_RELAY, joinCode } }
        );

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        var serverData = AllocationUtils.ToRelayServerData(alloc, "dtls");
        transport.SetRelayServerData(serverData);

        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    async Task StartClientWithRelayAsync(string joinCode)
    {
        await EnsureUnityServicesAsync();

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
        var serverData = AllocationUtils.ToRelayServerData(joinAlloc, "dtls");

        transport.SetRelayServerData(serverData);
        NetworkManager.Singleton.StartClient();
    }

    bool IsNgoConnected()
    {
        var nm = NetworkManager.Singleton;
        return nm && (nm.IsClient || nm.IsServer);
    }

    async Task EnsureUnityServicesAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    string GenerateRoomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new System.Random();
        var sb = new StringBuilder(length);

        for (int i = 0; i < length; i++)
            sb.Append(chars[rnd.Next(chars.Length)]);

        return sb.ToString();
    }

    /// <summary>
    /// Modo offline com bots.
    /// Inicia um Host local sem Photon nem Relay.
    /// </summary>
    public void OnClickPlayWithBots()
    {
        Debug.Log("[Lobby] Modo offline com bots (host local).");

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        NetworkManager.Singleton.StartHost();
        PlayerPrefs.SetInt("OfflineMode", 1);

        NetworkManager.Singleton.SceneManager.LoadScene("Prototype", LoadSceneMode.Single);
    }
}
