using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// LossProbe
/// ---------------------------------------------------------------------------
/// Componente de diagnóstico de rede que estima a percentagem de perda de pacotes
/// numa janela temporal deslizante, recorrendo a RPCs do Netcode for GameObjects.
///
/// Este sistema NÃO interfere com a jogabilidade.
/// É utilizado apenas para monitorização e análise de qualidade de rede,
/// sendo particularmente útil em contexto académico (latência, perda, robustez).
/// ---------------------------------------------------------------------------
/// Funcionamento resumido:
/// - O cliente envia periodicamente "probes" (mensagens leves) ao servidor
/// - O servidor devolve imediatamente a mesma mensagem ao cliente (eco)
/// - O cliente mede quantas mensagens enviadas não foram recebidas de volta
/// - A perda é calculada numa janela temporal configurável
/// ---------------------------------------------------------------------------
/// Arquitetura:
/// - Comunicação baseada em UDP (NGO)
/// - Modelo client → server → client
/// - ServerRpc sem ownership para permitir chamadas globais
/// - ClientRpc direcionado apenas ao cliente emissor
/// ---------------------------------------------------------------------------
/// </summary>
public class LossProbe : NetworkBehaviour
{
    /// <summary>
    /// Instância singleton para acesso global ao estado da perda de pacotes.
    /// Útil para HUDs, debug overlays ou logging.
    /// </summary>
    public static LossProbe Instance { get; private set; }

    [Header("Configuração da Sondagem")]
    
    [SerializeField]
    [Tooltip("Intervalo (em segundos) entre probes enviadas para o servidor.")]
    float interval = 0.5f;

    [SerializeField]
    [Tooltip("Janela temporal (em segundos) usada para calcular a perda de pacotes.")]
    float window = 10f;

    /// <summary>
    /// Percentagem atual de perda de pacotes estimada.
    /// -1 indica que ainda não existem dados suficientes.
    /// </summary>
    public float CurrentLossPercent { get; private set; } = -1f;

    /// <summary>
    /// Sequência incremental usada para identificar probes.
    /// Não depende de tempo de rede, apenas de ordem lógica.
    /// </summary>
    ulong _seq = 0;

    /// <summary>
    /// Fila de probes enviadas:
    /// - time: momento local em que a probe foi enviada
    /// - seq: identificador da probe
    /// </summary>
    readonly Queue<(float time, ulong seq)> sent = new();

    /// <summary>
    /// Fila de probes recebidas de volta (eco do servidor).
    /// Permite calcular quantas probes foram efetivamente entregues.
    /// </summary>
    readonly Queue<(float time, ulong seq)> echoed = new();

    /// <summary>
    /// Temporizador local para controlo do intervalo entre probes.
    /// Usa tempo não escalado para não ser afetado por pausas ou slow-motion.
    /// </summary>
    float _timer;

    void Awake()
    {
        // Registo do singleton.
        // Não depende da rede, apenas do ciclo de vida do GameObject.
        Instance = this;
    }

    void OnDestroy()
    {
        // Limpeza segura do singleton
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        // Garante que o NetworkManager existe e está ativo.
        // Evita chamadas RPC antes da inicialização da rede.
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsListening)
            return;

        // Caso especial:
        // - Servidor dedicado (sem cliente local)
        // - Host (servidor + cliente no mesmo processo)
        //
        // Nestes casos não existe tráfego de rede real,
        // logo a perda é considerada 0%.
        if (IsServer && !IsClient)
        {
            CurrentLossPercent = 0f;
            return;
        }

        if (IsServer && IsClient)
        {
            CurrentLossPercent = 0f;
            return;
        }

        // ---------------------------
        // ENVIO PERIÓDICO DE PROBES
        // ---------------------------
        _timer += Time.unscaledDeltaTime;
        if (_timer >= interval)
        {
            _timer = 0f;

            // Incrementa a sequência lógica da probe
            _seq++;

            // Envia a probe para o servidor.
            // O cliente NÃO espera confirmação fiável (UDP).
            SendProbeServerRpc(_seq);

            // Regista localmente a probe enviada
            sent.Enqueue((Time.unscaledTime, _seq));
        }

        // ---------------------------
        // LIMPEZA DA JANELA TEMPORAL
        // ---------------------------
        float cutoff = Time.unscaledTime - window;

        // Remove probes antigas fora da janela de análise
        while (sent.Count > 0 && sent.Peek().time < cutoff)
            sent.Dequeue();

        while (echoed.Count > 0 && echoed.Peek().time < cutoff)
            echoed.Dequeue();

        // ---------------------------
        // CÁLCULO DA PERDA
        // ---------------------------
        if (sent.Count > 0)
        {
            int enviados = sent.Count;
            int recebidos = echoed.Count;

            int perdidos = Mathf.Clamp(enviados - recebidos, 0, enviados);

            // Percentagem de perda baseada apenas em contagem,
            // não em RTT ou latência.
            CurrentLossPercent = (perdidos * 100f) / enviados;
        }
        else
        {
            // Ainda não existem probes suficientes para cálculo
            CurrentLossPercent = -1f;
        }
    }

    /// <summary>
    /// ServerRpc chamado pelo cliente para enviar uma probe.
    ///
    /// - RequireOwnership = false:
    ///   Permite que qualquer cliente invoque este RPC,
    ///   independentemente de ser dono do NetworkObject.
    ///
    /// O servidor não processa lógica pesada:
    /// apenas devolve imediatamente a probe ao cliente emissor.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    void SendProbeServerRpc(ulong seq, ServerRpcParams rpcParams = default)
    {
        // Define explicitamente o cliente de destino.
        // Isto evita broadcast desnecessário para todos os clientes.
        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
            }
        };

        // Eco da probe de volta ao cliente original
        EchoClientRpc(seq, target);
    }

    /// <summary>
    /// ClientRpc executado apenas no cliente que enviou a probe.
    ///
    /// Serve como confirmação não fiável de entrega.
    /// Se esta chamada não chegar, a probe é considerada perdida.
    /// </summary>
    [ClientRpc]
    void EchoClientRpc(ulong seq, ClientRpcParams rpcParams = default)
    {
        // Regista o eco com timestamp local
        echoed.Enqueue((Time.unscaledTime, seq));
    }
}
