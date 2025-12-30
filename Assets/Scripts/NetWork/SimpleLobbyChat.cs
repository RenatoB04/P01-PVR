using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Chat;
using ExitGames.Client.Photon;
using Photon.Pun;

/// <summary>
/// SimpleLobbyChat
/// ---------------------------------------------------------------------------
/// Implementa um chat de lobby simples usando Photon Chat.
/// Permite enviar e receber mensagens no canal de lobby, com UI baseada em TMP.
/// ---------------------------------------------------------------------------
/// Funcionalidades:
/// - Conexão a Photon Chat com AppId específico ou fallback do PhotonServerSettings.
/// - Subscrição automática de canal de lobby.
/// - Envios de mensagens via botão ou Enter.
/// - Visualização das mensagens num ScrollRect com TextMeshPro.
/// - Filtragem de mensagens do próprio jogador.
/// ---------------------------------------------------------------------------
/// </summary>
public class SimpleLobbyChat : MonoBehaviour, IChatClientListener
{
    [Header("Photon Chat")]
    [Tooltip("Se vazio, tentamos usar PhotonServerSettings.AppSettings.AppIdChat como fallback.")]
    [SerializeField] private string appIdChat = "";      // AppId do Photon Chat
    [SerializeField] private string chatVersion = "1.0"; // Versão do Chat (necessário pelo Photon)
    [SerializeField] private string fixedRegion = "eu";  // Região do servidor
    [SerializeField] private string lobbyChannel = "global-lobby"; // Canal de chat

    [Header("Identidade")]
    [SerializeField] private TMP_InputField playerNameInput;  // InputField para nome do jogador
    [SerializeField] private string invalidSampleName = "Nome Jogador"; // Nome inválido por defeito

    [Header("UI (mensagens)")]
    [SerializeField] private TMP_InputField inputField;  // InputField de mensagens
    [SerializeField] private Button sendButton;          // Botão de envio
    [SerializeField] private ScrollRect scrollRect;      // Scroll para mensagens
    [SerializeField] private TMP_Text messagesText;      // Texto que mostra mensagens

    [Header("Controlo de fluxo")]
    [SerializeField] private Button connectButton;      // Botão para conectar ao chat
    [SerializeField] private bool sendOnEnter = true;   // Envia mensagem ao pressionar Enter
    [SerializeField] private int maxVisibleMessages = 100; // Máximo de mensagens visíveis

    [Header("Debug / Test")]
    [Tooltip("Se true, tenta auto-conectar 1s após Start() (útil para testes no Editor).")]
    public bool autoConnectForTesting = false;

    private ChatClient _chat;                            // Cliente de chat Photon
    private readonly Queue<string> _lines = new Queue<string>(128); // Mensagens guardadas
    private bool isSubscribed = false;                  // Se está subscrito ao canal
    private bool isConnectingOrConnected = false;      // Se já está conectado ou tentando
    private string displayName = "";                    // Nome do jogador atual

    #region Unity Lifecycle

    void Awake()
    {
        // Inicialmente desativa UI de chat
        SetChatInteractable(false);

        // Adiciona listeners aos botões e input
        if (sendButton) sendButton.onClick.AddListener(OnClickSend);
        if (inputField && sendOnEnter) inputField.onSubmit.AddListener(_ => OnClickSend());
        if (connectButton) connectButton.onClick.AddListener(OnConnectButtonPressed);

        // Tenta obter referências de UI automaticamente
        TryAutoWireUI();
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;

        // Se AppIdChat não estiver definido no Inspector, tenta usar fallback do PhotonServerSettings
        if (string.IsNullOrWhiteSpace(appIdChat))
        {
            var settings = PhotonNetwork.PhotonServerSettings;
            if (settings != null && settings.AppSettings != null && !string.IsNullOrWhiteSpace(settings.AppSettings.AppIdChat))
            {
                appIdChat = settings.AppSettings.AppIdChat;
                Debug.Log("[SimpleLobbyChat] AppIdChat vazio no componente; a usar AppId do PhotonServerSettings.");
            }
        }

        // Se ainda não estiver definido, desativa o chat
        if (string.IsNullOrWhiteSpace(appIdChat))
        {
            Debug.LogError("[SimpleLobbyChat] AppIdChat vazio. Define no componente ou em PhotonServerSettings.");
            AppendSystem("[chat] AppIdChat vazio. Preenche AppIdChat no componente ou em PhotonServerSettings.");
            enabled = false;
            yield break;
        }

        // Mensagem inicial de instruções
        AppendSystem("Define o teu nome e carrega <b>Conectar</b> para ativar o chat.");

        // Auto-conexão para testes
        if (autoConnectForTesting)
        {
            yield return new WaitForSeconds(1f);
            AppendSystem("[chat] autoConnectForTesting activo -> a tentar conectar...");
            OnConnectButtonPressed();
        }
    }

    void Update()
    {
        // Necessário para o Photon Chat processar mensagens recebidas
        try
        {
            _chat?.Service();
        }
        catch (Exception ex)
        {
            Debug.LogError("[SimpleLobbyChat] Exception no ChatClient.Service(): " + ex);
        }
    }

    void OnDestroy()
    {
        // Limpa cliente de chat e listeners
        if (_chat != null) { _chat.Disconnect(); _chat = null; }
        if (connectButton) connectButton.onClick.RemoveListener(OnConnectButtonPressed);
        if (sendButton) sendButton.onClick.RemoveListener(OnClickSend);
        if (inputField && sendOnEnter) inputField.onSubmit.RemoveAllListeners();
    }

    #endregion

    #region Conexão / UI

    /// <summary>
    /// Tenta conectar ao chat com o nome definido
    /// </summary>
    public void OnConnectButtonPressed()
    {
        if (isConnectingOrConnected)
        {
            AppendSystem("[chat] Já ligado ou a tentar ligar.");
            Debug.Log("[SimpleLobbyChat] OnConnectButtonPressed: já ligado/ouligando.");
            return;
        }

        string proposed = GetProposedName();
        if (!IsNameValid(proposed))
        {
            AppendSystem("⚠ Define um <b>nome válido</b> antes de ligar o chat.");
            SetChatInteractable(false);
            Debug.Log("[SimpleLobbyChat] Nome inválido para chat: '" + proposed + "'");
            return;
        }

        displayName = proposed.Trim();
        AppendSystem($"[chat] A tentar conectar como <b>{displayName}</b>...");

        try
        {
            // Cria ChatClient se ainda não existir
            if (_chat == null)
            {
                _chat = new ChatClient(this);
                _chat.ChatRegion = fixedRegion;
                Debug.Log("[SimpleLobbyChat] Criado ChatClient, region=" + fixedRegion);
            }

            // Conecta ao Photon Chat
            bool ok = _chat.Connect(appIdChat, chatVersion, new AuthenticationValues(displayName));
            isConnectingOrConnected = true;
            Debug.Log($"[SimpleLobbyChat] Connect chamada -> returned {ok}. appIdChat length={(appIdChat?.Length ?? 0)}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[SimpleLobbyChat] Exceção ao chamar Connect(): " + ex);
            AppendSystem("[chat] Erro ao iniciar ligação: " + ex.Message);
            isConnectingOrConnected = false;
        }
    }

    /// <summary>
    /// Obtém nome proposto do jogador
    /// </summary>
    string GetProposedName()
    {
        if (playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text))
            return playerNameInput.text;

        if (!string.IsNullOrWhiteSpace(PhotonNetwork.NickName))
            return PhotonNetwork.NickName;

        return string.Empty;
    }

    /// <summary>
    /// Valida se o nome é adequado para chat
    /// </summary>
    bool IsNameValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string n = name.Trim();
        if (n.Length < 2) return false;
        if (!string.IsNullOrWhiteSpace(invalidSampleName) &&
            n.Equals(invalidSampleName, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Habilita ou desabilita UI de chat
    /// </summary>
    void SetChatInteractable(bool enabledUI)
    {
        if (sendButton) sendButton.interactable = enabledUI;
        if (inputField) inputField.readOnly = !enabledUI;
    }

    /// <summary>
    /// Envia mensagem pelo chat
    /// </summary>
    public void OnClickSend()
    {
        if (inputField == null) { AppendSystem("⚠ InputField da mensagem não está atribuído."); return; }

        var currentName = GetProposedName();
        if (!IsNameValid(displayName) || !IsNameValid(currentName))
        {
            AppendSystem("⚠ Define o teu <b>nome</b> e carrega <b>Conectar</b>.");
            SetChatInteractable(false);
            return;
        }
        displayName = currentName.Trim();

        if (_chat == null || !_chat.CanChat || !isSubscribed)
        {
            AppendSystem("⚠ O chat ainda não está pronto. Tenta novamente.");
            SetChatInteractable(false);
            return;
        }

        string msg = inputField.text;
        if (string.IsNullOrWhiteSpace(msg)) return;

        string trimmed = msg.Trim();
        bool sent = _chat.PublishMessage(lobbyChannel, trimmed);
        Debug.Log($"[SimpleLobbyChat] PublishMessage returned: {sent}");

        AppendLine($"<b>{displayName}</b>: {trimmed}");

        inputField.text = string.Empty;
        inputField.ActivateInputField();
    }

    #endregion

    #region Mensagens / UI

    /// <summary>
    /// Adiciona linha de mensagem no chat e atualiza ScrollRect
    /// </summary>
    private void AppendLine(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > maxVisibleMessages) _lines.Dequeue();

        if (messagesText != null)
        {
            messagesText.enableWordWrapping = true;
            messagesText.richText = true;
            messagesText.alignment = TextAlignmentOptions.TopLeft;
            messagesText.text = string.Join("\n", _lines);

            Canvas.ForceUpdateCanvases();
            if (scrollRect != null && scrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(messagesText.rectTransform);
                float newHeight = messagesText.preferredHeight + 20f;
                var size = scrollRect.content.sizeDelta;
                scrollRect.content.sizeDelta = new Vector2(size.x, newHeight);
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    /// <summary>
    /// Adiciona mensagem de sistema
    /// </summary>
    private void AppendSystem(string text) => AppendLine($"<color=#888>[system]</color> {text}");

    /// <summary>
    /// Tenta obter referências automáticas de UI se não estiverem atribuídas no Inspector
    /// </summary>
    void TryAutoWireUI()
    {
        if (scrollRect == null) scrollRect = GetComponentInChildren<ScrollRect>(true);
        if (messagesText == null && scrollRect != null)
            messagesText = scrollRect.content != null ? scrollRect.content.GetComponentInChildren<TMP_Text>(true) : null;
    }

    #endregion

    #region Photon Chat Callbacks

    public void OnConnected()
    {
        Debug.Log("[SimpleLobbyChat] OnConnected()");
        AppendSystem("Connected. Subscribing channel...");
        isSubscribed = false;
        try
        {
            _chat?.Subscribe(new[] { lobbyChannel }, 0);
        }
        catch (Exception ex)
        {
            Debug.LogError("[SimpleLobbyChat] Ex on Subscribe: " + ex);
            AppendSystem("[chat] Erro ao subscrever canal: " + ex.Message);
        }
    }

    public void OnSubscribed(string[] channels, bool[] results)
    {
        Debug.Log("[SimpleLobbyChat] OnSubscribed(): channels=" + string.Join(",", channels));
        isSubscribed = true;
        AppendSystem($"Joined channel <b>{lobbyChannel}</b>.");
        SetChatInteractable(true);
    }

    public void OnUnsubscribed(string[] channels)
    {
        Debug.Log("[SimpleLobbyChat] OnUnsubscribed()");
        isSubscribed = false;
        SetChatInteractable(false);
        AppendSystem($"Left channel(s): {string.Join(", ", channels)}.");
    }

    public void OnGetMessages(string channelName, string[] senders, object[] messages)
    {
        Debug.Log($"[SimpleLobbyChat] OnGetMessages: channel={channelName} count={messages.Length}");
        for (int i = 0; i < messages.Length; i++)
        {
            string sender = senders[i];
            string text = messages[i]?.ToString() ?? "";
            if (!string.Equals(sender, displayName))
                AppendLine($"<b>{sender}</b>: {text}");
        }
    }

    public void OnDisconnected()
    {
        Debug.Log("[SimpleLobbyChat] OnDisconnected()");
        isSubscribed = false;
        isConnectingOrConnected = false;
        SetChatInteractable(false);
        AppendSystem("<color=#f66>Disconnected.</color>");
    }

    public void OnChatStateChange(ChatState state)
    {
        Debug.Log("[SimpleLobbyChat] OnChatStateChange: " + state);
        AppendSystem($"[chat] Chat state: {state}");
    }

    public void DebugReturn(DebugLevel level, string message)
    {
        Debug.Log($"[SimpleLobbyChat] DebugReturn ({level}): {message}");
        AppendSystem($"[chat] Debug: {message}");
    }

    // Callbacks não utilizados
    public void OnPrivateMessage(string sender, object message, string channelName) { }
    public void OnUserSubscribed(string channel, string user) { }
    public void OnUserUnsubscribed(string channel, string user) { }
    public void OnStatusUpdate(string user, int status, bool gotMessage, object message) { }

    #endregion
}
