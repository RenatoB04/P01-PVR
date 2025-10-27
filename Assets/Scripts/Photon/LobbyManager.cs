using UnityEngine;
using Photon.Pun; // Precisamos disto para usar as funções do Photon
using Photon.Realtime; // E disto também para callbacks

// MonoBehaviourPunCallbacks dá-nos acesso a funções especiais do Photon (callbacks)
public class LobbyManager : MonoBehaviourPunCallbacks
{
    // Chamado automaticamente pelo Unity quando o script é carregado
    void Start()
    {
        Debug.Log("LobbyManager a iniciar... Tentando conectar ao Photon...");
        // Esta linha diz ao Photon para usar as configurações que definiste no Wizard (App ID, Região)
        // e para tentar conectar-se ao servidor "Master" do Photon.
        PhotonNetwork.ConnectUsingSettings();
    }

    // --- Callbacks do Photon ---
    // Estas funções são chamadas AUTOMATICAMENTE pelo Photon quando certos eventos acontecem.

    // Chamado quando a ligação ao servidor Master do Photon é bem sucedida
    public override void OnConnectedToMaster()
    {
        // Debug.Log para sabermos que funcionou!
        Debug.Log("Conectado com sucesso ao Servidor Master do Photon!");

        // Opcional, mas importante para o matchmaking depois:
        // Assim que nos conectamos ao Master, dizemos que queremos poder
        // entrar no "Lobby" principal. É no Lobby que vemos as salas disponíveis.
        Debug.Log("A entrar no Lobby Principal...");
        PhotonNetwork.JoinLobby();
    }

    // Chamado quando entramos com sucesso no Lobby Principal
    public override void OnJoinedLobby()
    {
        Debug.Log("Entrei com sucesso no Lobby Principal!");
        // A partir daqui, estarias pronto para criar ou listar salas (tarefa seguinte)
    }

    // Chamado se a ligação falhar ou for desconectada
    public override void OnDisconnected(DisconnectCause cause)
    {
        // Informa-nos porque é que fomos desconectados
        Debug.LogWarningFormat("Desconectado do Photon. Causa: {0}", cause);
    }
}