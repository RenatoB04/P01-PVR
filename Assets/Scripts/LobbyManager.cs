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

public class LobbyManager : MonoBehaviourPunCallbacks
{
    [Header("UI (TMP)")]
    [SerializeField] TMP_InputField ifPlayerName;
    [SerializeField] Button         btnConnect;
    [SerializeField] Button         btnCreate;
    [SerializeField] TMP_Text       txtCreatedCode;
    [SerializeField] TMP_InputField ifJoinCode;
    [SerializeField] Button         btnJoin;
    [SerializeField] Button         btnLeave;
    [SerializeField] TMP_Text       txtStatus;

    [Header("Config")]
    [SerializeField] string gameSceneName = "GameScene";
    [SerializeField] int    roomCodeLength = 6;
    [SerializeField] int    maxPlayers = 2;

    const string ROOM_PROP_RELAY = "relay"; // joinCode do Unity Relay

    void Awake()
    {
        // NO HÍBRIDO: não deixes o Photon mexer na cena
        PhotonNetwork.AutomaticallySyncScene = false;
        Application.runInBackground = true;

        SetUIConnected(false);
        SetUILobbyActions(false);
        Log("Pronto. Define nome e carrega Conectar.");

        btnConnect.onClick.AddListener(OnClickConnect);
        btnCreate.onClick.AddListener(OnClickCreate);
        btnJoin.onClick.AddListener(OnClickJoin);
        btnLeave.onClick.AddListener(OnClickLeave);
    }

    void OnDestroy()
    {
        btnConnect.onClick.RemoveAllListeners();
        btnCreate.onClick.RemoveAllListeners();
        btnJoin.onClick.RemoveAllListeners();
        btnLeave.onClick.RemoveAllListeners();
    }

    void SetUIConnected(bool connected)
    {
        if (btnConnect) btnConnect.interactable = !connected;
        if (ifPlayerName) ifPlayerName.interactable = !connected;

        if (btnCreate) btnCreate.interactable = connected;
        if (btnJoin) btnJoin.interactable = connected && !string.IsNullOrEmpty(ifJoinCode?.text);
        if (ifJoinCode) ifJoinCode.interactable = connected;

        if (btnLeave) btnLeave.interactable = false;
        if (txtCreatedCode) { txtCreatedCode.gameObject.SetActive(false); txtCreatedCode.text = ""; }
    }

    void SetUILobbyActions(bool inRoom)
    {
        if (btnLeave) btnLeave.interactable = inRoom;
        if (btnCreate) btnCreate.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady;
        if (btnJoin) btnJoin.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady && !string.IsNullOrEmpty(ifJoinCode?.text);
        if (ifJoinCode) ifJoinCode.interactable = !inRoom && PhotonNetwork.IsConnectedAndReady;
        if (txtCreatedCode) txtCreatedCode.gameObject.SetActive(inRoom);
    }

    void Log(string msg) { if (txtStatus) txtStatus.text = msg; Debug.Log("[Lobby] " + msg); }

    async void OnClickConnect()
    {
        var nick = string.IsNullOrWhiteSpace(ifPlayerName?.text) ? ("Player" + UnityEngine.Random.Range(1000, 9999)) : ifPlayerName.text.Trim();
        PhotonNetwork.NickName = nick;
        Log($"A ligar ao Photon como {PhotonNetwork.NickName}...");

        if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
        else { Log("Já estás ligado."); SetUIConnected(true); }

        // Prepara Unity Services (Relay/Authentication) cedo
        await EnsureUnityServicesAsync();
    }

    void OnClickCreate()
    {
        string code = GenerateRoomCode(roomCodeLength);
        var options = new RoomOptions
        {
            MaxPlayers = (byte)Mathf.Clamp(maxPlayers, 2, 16),
            IsVisible = false,
            IsOpen = true,
            CustomRoomProperties = new Hashtable { { ROOM_PROP_RELAY, "" } },
            CustomRoomPropertiesForLobby = new[] { ROOM_PROP_RELAY }
        };
        Log($"A criar lobby com código {code}...");
        PhotonNetwork.CreateRoom(code, options, TypedLobby.Default);
    }

    void OnClickJoin()
    {
        string code = ifJoinCode?.text?.Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) { Log("Escreve um código para entrar."); return; }
        Log($"A entrar no lobby {code}...");
        PhotonNetwork.JoinRoom(code);
    }

    void OnClickLeave()
    {
        if (PhotonNetwork.InRoom) { Log("A sair do lobby..."); PhotonNetwork.LeaveRoom(); }
    }

    // ---------------- Photon Callbacks ----------------

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

    public override async void OnJoinedRoom()
    {
        string code = PhotonNetwork.CurrentRoom.Name;
        Log($"Entraste no lobby ({code}). À espera de jogadores ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        if (txtCreatedCode)
        {
            txtCreatedCode.gameObject.SetActive(true);
            txtCreatedCode.text = $"Código: {code}";
        }
        SetUILobbyActions(true);

        // Se fores o Master e a sala estiver cheia, arranca já.
        await TryStartWhenReadyAsync();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Log($"Falhou criar lobby: {message}. A tentar outro código...");
        string newCode = GenerateRoomCode(roomCodeLength);
        var options = new RoomOptions
        {
            MaxPlayers = (byte)Mathf.Clamp(maxPlayers, 2, 16),
            IsVisible = false,
            IsOpen = true,
            CustomRoomProperties = new Hashtable { { ROOM_PROP_RELAY, "" } },
            CustomRoomPropertiesForLobby = new[] { ROOM_PROP_RELAY }
        };
        PhotonNetwork.CreateRoom(newCode, options, TypedLobby.Default);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Log($"Falhou entrar: {message}. Confirma o código.");
    }

    public override async void OnPlayerEnteredRoom(Player newPlayer)
    {
        Log($"Entrou: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        await TryStartWhenReadyAsync();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Log($"{otherPlayer.NickName} saiu. ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    public override async void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        // Clientes recebem o joinCode e ligam-se ao NGO
        if (propertiesThatChanged != null && propertiesThatChanged.ContainsKey(ROOM_PROP_RELAY))
        {
            string joinCode = propertiesThatChanged[ROOM_PROP_RELAY] as string;
            if (!string.IsNullOrEmpty(joinCode) && !IsNgoConnected())
            {
                Log($"Código Relay recebido: {joinCode}. A ligar ao jogo...");
                await StartClientWithRelayAsync(joinCode);
            }
        }
    }

    // ---------------- Fluxo Híbrido ----------------

    async Task TryStartWhenReadyAsync()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (PhotonNetwork.CurrentRoom.PlayerCount >= PhotonNetwork.CurrentRoom.MaxPlayers)
        {
            Log("Jogadores prontos. A iniciar partida (Relay + NGO)...");
            await StartHostWithRelayAndLoadAsync();
        }
    }

    // HOST: cria Relay, publica joinCode no Photon e arranca Host + troca de cena via NGO
    async Task StartHostWithRelayAndLoadAsync()
    {
        await EnsureUnityServicesAsync();

        int maxConnections = Mathf.Max(1, maxPlayers - 1);
        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
        Log($"Relay criado. JoinCode: {joinCode}");

        // Publica joinCode para os clientes apanharem
        var props = new ExitGames.Client.Photon.Hashtable { { ROOM_PROP_RELAY, joinCode } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) { Debug.LogError("UnityTransport não encontrado no NetworkManager."); return; }

        // >>> MUDANÇA AQUI: usar AllocationUtils.ToRelayServerData (SDK unificado)
        var serverData = AllocationUtils.ToRelayServerData(alloc, "dtls"); // ou RelayProtocol.DTLS
        transport.SetRelayServerData(serverData);

        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            if (!NetworkManager.Singleton.StartHost()) { Debug.LogError("Falha ao iniciar Host NGO."); return; }
        }

        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    async Task StartClientWithRelayAsync(string joinCode)
    {
        await EnsureUnityServicesAsync();

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) { Debug.LogError("UnityTransport não encontrado no NetworkManager."); return; }

        JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);

        // >>> PODES usar AllocationUtils também no Join (consistente com o host):
        var serverData = AllocationUtils.ToRelayServerData(joinAlloc, "dtls"); // ou RelayProtocol.DTLS
        transport.SetRelayServerData(serverData);

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.StartClient()) { Debug.LogError("Falha ao iniciar Client NGO."); return; }
        }
    }

    bool IsNgoConnected()
    {
        var nm = NetworkManager.Singleton;
        return nm && (nm.IsClient || nm.IsServer);
    }

    // ---------------- Util ----------------

    async Task EnsureUnityServicesAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    string GenerateRoomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new System.Random();
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(chars[rnd.Next(chars.Length)]);
        return sb.ToString();
    }
}
