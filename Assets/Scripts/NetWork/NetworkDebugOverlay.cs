using UnityEngine;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // Para suportar o novo Input System
#endif

public class NetworkDebugOverlay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI debugText;   // arrasta aqui o TMP se quiseres
    [SerializeField] private KeyCode toggleKey = KeyCode.F3;

    private bool visible = true;
    private float fpsTimer;
    private int frames;
    private float fps;

    void Awake()
    {
        // Auto-liga se não tiver referência no Inspector
        if (!debugText) debugText = GetComponent<TextMeshProUGUI>();
        if (!debugText) debugText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    void Start()
    {
        // Arranca visível e faz um primeiro texto
        if (debugText) debugText.enabled = visible;
        ForceRefreshNow();
    }

    void Update()
    {
        // --- TOGGLE (funciona com New Input System e Legacy) ---
        bool pressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            pressed = true;
#endif

        if (Input.GetKeyDown(toggleKey))
            pressed = true;

        if (pressed)
        {
            visible = !visible;
            if (debugText) debugText.enabled = visible;
            Debug.Log($"[Overlay] Toggle -> {(visible ? "ON" : "OFF")}");
        }

        if (!visible || !debugText) return;

        // FPS (média por segundo)
        frames++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 1f)
        {
            fps = frames / fpsTimer;
            frames = 0;
            fpsTimer = 0f;
        }

        // Ping
        ulong ping = 0;
        if (NetworkManager.Singleton && NetworkManager.Singleton.IsClient)
        {
            var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            ping = transport.GetCurrentRtt(NetworkManager.ServerClientId);
        }

        // Loss
        string loss = "-";
        if (LossProbe.Instance)
        {
            float v = LossProbe.Instance.CurrentLossPercent;
            if (v >= 0f) loss = v.ToString("F1") + " %";
        }

        // Texto
        debugText.text = $"PING: {ping} ms\nLOSS: {loss}\nFPS: {fps:F0}";
    }

    private void ForceRefreshNow()
    {
        if (!debugText) return;
        debugText.text = "PING: 0 ms\nLOSS: -\nFPS: 0";
    }
}
