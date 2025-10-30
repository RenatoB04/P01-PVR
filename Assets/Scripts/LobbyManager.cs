using System;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    const byte MAX_PLAYERS = 2;

    void Awake()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
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

    void OnClickConnect()
    {
        var nick = string.IsNullOrWhiteSpace(ifPlayerName?.text) ? ("Player" + UnityEngine.Random.Range(1000, 9999)) : ifPlayerName.text.Trim();
        PhotonNetwork.NickName = nick;
        Log($"A ligar ao Photon como {PhotonNetwork.NickName}...");
        if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
        else { Log("Já estás ligado."); SetUIConnected(true); }
    }

    void OnClickCreate()
    {
        string code = GenerateRoomCode(roomCodeLength);
        var options = new RoomOptions { MaxPlayers = MAX_PLAYERS, IsVisible = false, IsOpen = true };
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
        Log($"Entraste no lobby ({code}). À espera de jogadores ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        if (txtCreatedCode)
        {
            txtCreatedCode.gameObject.SetActive(true);
            txtCreatedCode.text = $"Código: {code}";
        }
        SetUILobbyActions(true);
        TryStartWhenReady();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Log($"Falhou criar lobby: {message}. A tentar outro código...");
        string newCode = GenerateRoomCode(roomCodeLength);
        var options = new RoomOptions { MaxPlayers = MAX_PLAYERS, IsVisible = false, IsOpen = true };
        PhotonNetwork.CreateRoom(newCode, options, TypedLobby.Default);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Log($"Falhou entrar: {message}. Confirma o código.");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Log($"Entrou: {newPlayer.NickName} ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        TryStartWhenReady();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Log($"{otherPlayer.NickName} saiu. ({PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
    }

    void TryStartWhenReady()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (PhotonNetwork.CurrentRoom.PlayerCount >= MAX_PLAYERS)
        {
            Log("Dois jogadores presentes. A iniciar partida...");
            PhotonNetwork.LoadLevel(gameSceneName);
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