using UnityEngine;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; 
#endif

/// <summary>
/// NetworkDebugOverlay
/// ---------------------------------------------------------------------------
/// Componente que fornece um overlay de debug em tempo real, exibindo:
/// - PING atual do cliente em relação ao servidor
/// - Percentagem de perda de pacotes (via LossProbe)
/// - FPS local
///
/// Permite alternar visibilidade com uma tecla (por defeito F3)
/// e suporta tanto o antigo InputManager como o novo Input System.
/// ---------------------------------------------------------------------------
/// Observações de networking:
/// - PING é obtido diretamente do UnityTransport do Netcode for GameObjects
/// - LOSS é obtido através da instância singleton de LossProbe (client → server → client)
/// - Ideal para monitorização de rede em tempo real durante testes e desenvolvimento
/// ---------------------------------------------------------------------------
/// </summary>
public class NetworkDebugOverlay : MonoBehaviour
{
    [Header("Referências UI")]
    [SerializeField] private TextMeshProUGUI debugText;   // Texto onde os dados são exibidos

    [Header("Configuração")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F3; // Tecla para mostrar/ocultar overlay

    // --------------------------
    // Estado interno
    // --------------------------
    private bool visible = true;    // Overlay visível por defeito
    private float fpsTimer;         // Acumula tempo para cálculo do FPS
    private int frames;             // Contagem de frames no intervalo
    private float fps;              // FPS calculado

    void Awake()
    {
        // Garantir referência ao TMP_Text
        if (!debugText) debugText = GetComponent<TextMeshProUGUI>();
        if (!debugText) debugText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    void Start()
    {
        // Ativar ou desativar UI conforme estado
        if (debugText) debugText.enabled = visible;

        // Inicializar overlay com valores padrão
        ForceRefreshNow();
    }

    void Update()
    {
        // --------------------------
        // Gestão de toggle de visibilidade
        // --------------------------
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        // Suporte para novo Input System
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            pressed = true;
#endif

        // Suporte para InputManager tradicional
        if (Input.GetKeyDown(toggleKey))
            pressed = true;

        if (pressed)
        {
            visible = !visible;
            if (debugText) debugText.enabled = visible;
            Debug.Log($"[Overlay] Toggle -> {(visible ? "ON" : "OFF")}");
        }

        if (!visible || !debugText) return;

        // --------------------------
        // Cálculo de FPS
        // --------------------------
        frames++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 1f)
        {
            fps = frames / fpsTimer;
            frames = 0;
            fpsTimer = 0f;
        }

        // --------------------------
        // Obtém PING do cliente para o servidor
        // --------------------------
        ulong ping = 0;
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsClient)
        {
            var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            ping = transport.GetCurrentRtt(NetworkManager.ServerClientId); // RTT em ms
        }

        // --------------------------
        // Obtém LOSS via LossProbe
        // --------------------------
        string loss = "-";
        if (LossProbe.Instance)
        {
            float v = LossProbe.Instance.CurrentLossPercent;
            if (v >= 0f) loss = v.ToString("F1") + " %";
        }

        // --------------------------
        // Atualiza UI
        // --------------------------
        debugText.text = $"PING: {ping} ms\nLOSS: {loss}\nFPS: {fps:F0}";
    }

    /// <summary>
    /// Atualiza o overlay imediatamente com valores padrão.
    /// Útil para inicialização ou reset visual.
    /// </summary>
    private void ForceRefreshNow()
    {
        if (!debugText) return;
        debugText.text = "PING: 0 ms\nLOSS: -\nFPS: 0";
    }
}
