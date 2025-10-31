using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class RespawnUIManager : MonoBehaviour
{
    [Header("UI (opcional: se vazio, o script cria um painel de emergência)")]
    [SerializeField] private GameObject respawnPanel;
    [SerializeField] private Button respawnButton;
    [SerializeField] private TMP_Text messageText;

    [Header("Respawn")]
    [Tooltip("Delay mínimo antes de permitir respawn (segundos). 0 = imediato.")]
    [SerializeField] private float minRespawnDelay = 0f;

    [Header("Forçar visibilidade")]
    [Tooltip("Destaca o painel para um Canvas Overlay próprio com sorting alto.")]
    [SerializeField] private bool detachToTopCanvas = true;

    [Tooltip("Aplica anchors/offsets full-screen no painel.")]
    [SerializeField] private bool forceFullScreenAnchors = true;

    [Header("Cursor")]
    [Tooltip("Ao abrir a deathscreen: desbloqueia e mostra o cursor; ao fechar: bloqueia e oculta.")]
    [SerializeField] private bool controlCursor = true;

    private Health health;
    private PlayerDeathAndRespawn respawner;

    // Estado/recuperação do painel original
    private Transform originalParent;
    private int originalSiblingIndex;
    private Canvas originalCanvas;
    private bool isDetached;
    private GameObject runtimeCanvasGO; // Canvas criado para o painel destacado
    private GameObject runtimeFailSafeGO; // Painel de emergência criado se faltar UI

    private Coroutine waitCo;
    private Coroutine countdownCo;
    private float allowAtTime;

    void OnEnable()
    {
        // Resolver referências da UI se não foram ligadas no Inspector
        if (!respawnPanel)
        {
            var byTag = GameObject.FindWithTag("RespawnPanel");
            if (byTag) respawnPanel = byTag;
            if (!respawnPanel)
            {
                var byName = GameObject.Find("RespawnPanel");
                if (byName) respawnPanel = byName;
            }
        }
        if (respawnPanel && !respawnButton)
            respawnButton = respawnPanel.GetComponentInChildren<Button>(true);
        if (respawnPanel && !messageText)
            messageText = respawnPanel.GetComponentInChildren<TMP_Text>(true);

        if (respawnPanel) respawnPanel.SetActive(false);

        if (waitCo != null) StopCoroutine(waitCo);
        waitCo = StartCoroutine(WaitForLocalPlayerAndBind());
    }

    void OnDisable()
    {
        Unbind();
        if (waitCo != null) { StopCoroutine(waitCo); waitCo = null; }
        if (countdownCo != null) { StopCoroutine(countdownCo); countdownCo = null; }
        // Evitar exceção de SetParent durante desativação: só destruir o canvas temporário
        if (runtimeCanvasGO) { Destroy(runtimeCanvasGO); runtimeCanvasGO = null; }
        runtimeFailSafeGO = null;
        isDetached = false;
    }

    private IEnumerator WaitForLocalPlayerAndBind()
    {
        // Espera pelo NetworkManager e pelo PlayerObject local
        while (NetworkManager.Singleton == null) yield return null;
        while (NetworkManager.Singleton.LocalClient == null ||
               NetworkManager.Singleton.LocalClient.PlayerObject == null) yield return null;

        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            Debug.LogError("[RespawnUI] Local PlayerObject nulo.");
            yield break;
        }

        // Obtém componentes no Player local
        health = playerObj.GetComponentInChildren<Health>();
        respawner = playerObj.GetComponentInChildren<PlayerDeathAndRespawn>();

        if (health == null)
        {
            Debug.LogError("[RespawnUI] Health não encontrado no Player local.");
            yield break;
        }
        if (respawner == null)
        {
            Debug.LogWarning("[RespawnUI] PlayerDeathAndRespawn não encontrado no Player local.");
        }

        // Subscrever
        health.isDead.OnValueChanged += OnIsDeadChanged;
        health.OnDied.AddListener(OnDiedUnityEvent);

        // Botão
        if (respawnPanel && respawnButton == null)
            respawnButton = respawnPanel.GetComponentInChildren<Button>(true);
        if (respawnButton != null)
        {
            respawnButton.onClick.RemoveListener(OnRespawnClicked);
            respawnButton.onClick.AddListener(OnRespawnClicked);
        }
        else
        {
            Debug.LogWarning("[RespawnUI] Respawn Button não definido/encontrado no painel.");
        }

        Debug.Log($"[RespawnUI] Ligado ao Player local. Panel={(respawnPanel ? respawnPanel.name : "null")} isDead={health.isDead.Value}");

        // Estado inicial
        ApplyIsDeadState(health.isDead.Value);

        waitCo = null;
    }

    private void Unbind()
    {
        if (health != null)
        {
            health.isDead.OnValueChanged -= OnIsDeadChanged;
            health.OnDied.RemoveListener(OnDiedUnityEvent);
        }
        if (respawnButton != null)
            respawnButton.onClick.RemoveListener(OnRespawnClicked);
    }

    private void OnIsDeadChanged(bool prev, bool curr)
    {
        ApplyIsDeadState(curr);
    }

    private void OnDiedUnityEvent()
    {
        ApplyIsDeadState(true);
    }

    private void ApplyIsDeadState(bool isDead)
    {
        if (isDead)
        {
            // Se não tiver painel, cria um painel de emergência
            if (!respawnPanel)
            {
                CreateFailSafePanel();
            }
            ShowPanel();
        }
        else
        {
            HidePanel();
        }
    }

    private void ShowPanel()
    {
        if (!respawnPanel)
            return;

        PreparePanelForDisplay(respawnPanel);

        if (messageText)
            messageText.text = (minRespawnDelay > 0f)
                ? $"Respawn in {minRespawnDelay:0}s"
                : "You died!";

        allowAtTime = Time.unscaledTime + Mathf.Max(0f, minRespawnDelay);
        if (respawnButton) respawnButton.interactable = (minRespawnDelay <= 0f);

        if (countdownCo != null) StopCoroutine(countdownCo);
        if (minRespawnDelay > 0f)
            countdownCo = StartCoroutine(CountdownRoutine());

        if (controlCursor) GameplayCursor.Unlock();

        respawnPanel.SetActive(true);
        respawnPanel.transform.SetAsLastSibling();
    }

    private void HidePanel()
    {
        if (countdownCo != null) { StopCoroutine(countdownCo); countdownCo = null; }

        if (controlCursor) GameplayCursor.Lock();

        if (respawnPanel && respawnPanel.activeSelf)
            respawnPanel.SetActive(false);

        ReattachIfDetached();
        DestroyFailSafeIfAny();
    }

    private IEnumerator CountdownRoutine()
    {
        while (Time.unscaledTime < allowAtTime)
        {
            float remain = Mathf.Max(0f, allowAtTime - Time.unscaledTime);
            if (messageText) messageText.text = $"Respawn in {remain:0}s";
            if (respawnButton) respawnButton.interactable = false;
            yield return null;
        }
        if (messageText) messageText.text = "Press Respawn";
        if (respawnButton) respawnButton.interactable = true;
        countdownCo = null;
    }

    private void OnRespawnClicked()
    {
        if (Time.unscaledTime < allowAtTime)
        {
            Debug.LogWarning("[RespawnUI] Clique antes do delay permitido.");
            return;
        }

        if (respawner == null)
        {
            Debug.LogError("[RespawnUI] PlayerDeathAndRespawn ausente no Player local.");
            return;
        }

        Debug.Log("[RespawnUI] Botão Respawn clicado -> enviando RPC ao servidor");
        respawner.RespawnServerRpc();
        // Fecha a UI já (quando isDead=false propagar, continuamos bloqueado)
        HidePanel();
    }

    // ---------- Forçar visibilidade / destacar para Canvas próprio ----------

    private void PreparePanelForDisplay(GameObject panel)
    {
        // Ativa toda a cadeia de pais
        var t = panel.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }

        if (detachToTopCanvas)
            DetachToOverlayCanvas(panel);

        // CanvasGroup visível
        var group = panel.GetComponent<CanvasGroup>();
        if (!group) group = panel.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        if (forceFullScreenAnchors)
            ForceFullScreen(panel);
    }

    private void DetachToOverlayCanvas(GameObject panel)
    {
        if (isDetached) return;

        originalParent = panel.transform.parent;
        originalSiblingIndex = panel.transform.GetSiblingIndex();
        originalCanvas = panel.GetComponentInParent<Canvas>();

        // Cria Canvas Overlay dedicado
        runtimeCanvasGO = new GameObject("RespawnCanvas_Top", typeof(Canvas), typeof(GraphicRaycaster));
        var cv = runtimeCanvasGO.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 32760; // bem alto
        runtimeCanvasGO.layer = (originalParent ? originalParent.gameObject.layer : 0);

        // Mover painel para este Canvas
        panel.transform.SetParent(runtimeCanvasGO.transform, worldPositionStays: false);
        isDetached = true;

        // Garante Raycaster
        if (!runtimeCanvasGO.TryGetComponent<GraphicRaycaster>(out _))
            runtimeCanvasGO.AddComponent<GraphicRaycaster>();
    }

    private void ReattachIfDetached()
    {
        if (!isDetached || !respawnPanel) return;

        // Se o canvas temporário foi destruído/desativado, não tenta reanexar agora
        if (runtimeCanvasGO != null && !runtimeCanvasGO.activeInHierarchy)
        {
            isDetached = false;
            runtimeCanvasGO = null;
            return;
        }

        if (originalParent)
        {
            respawnPanel.transform.SetParent(originalParent, worldPositionStays: false);
            respawnPanel.transform.SetSiblingIndex(originalSiblingIndex);
        }
        isDetached = false;

        if (runtimeCanvasGO) Destroy(runtimeCanvasGO);
        runtimeCanvasGO = null;
    }

    private void ForceFullScreen(GameObject panel)
    {
        var rt = panel.GetComponent<RectTransform>();
        if (rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition3D = Vector3.zero;
            rt.localScale = Vector3.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }

    // ---------- Fail-safe: cria painel simples se nenhum foi fornecido ----------

    private void CreateFailSafePanel()
    {
        if (runtimeFailSafeGO)
            return;

        // Canvas Overlay dedicado
        runtimeCanvasGO = new GameObject("RespawnCanvas_FailSafe", typeof(Canvas), typeof(GraphicRaycaster));
        var cv = runtimeCanvasGO.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 32760;

        // Painel
        runtimeFailSafeGO = new GameObject("RespawnPanel_FailSafe", typeof(RectTransform), typeof(Image));
        runtimeFailSafeGO.transform.SetParent(runtimeCanvasGO.transform, false);
        var img = runtimeFailSafeGO.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0.5f); // escurece o fundo
        ForceFullScreen(runtimeFailSafeGO);

        // Botão
        var btnGO = new GameObject("RespawnButton_FailSafe", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(runtimeFailSafeGO.transform, false);
        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.sizeDelta = new Vector2(300, 100);
        btnRT.anchorMin = btnRT.anchorMax = new Vector2(0.5f, 0.5f);
        btnRT.anchoredPosition = Vector2.zero;

        var btn = btnGO.GetComponent<Button>();
        btn.onClick.AddListener(OnRespawnClicked);

        // Texto do botão
        TMP_Text tmp = null;
        var tmpGO = new GameObject("Label_TMP", typeof(RectTransform));
        tmpGO.transform.SetParent(btnGO.transform, false);
        var tmpRT = tmpGO.GetComponent<RectTransform>();
        tmpRT.anchorMin = Vector2.zero; tmpRT.anchorMax = Vector2.one;
        tmpRT.offsetMin = Vector2.zero; tmpRT.offsetMax = Vector2.zero;
        tmp = tmpGO.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 36;
        tmp.text = "RESPAWN";

        // Liga às variáveis do manager
        respawnPanel = runtimeFailSafeGO;
        respawnButton = btn;
        messageText = null;
    }

    private void DestroyFailSafeIfAny()
    {
        if (runtimeFailSafeGO)
        {
            Destroy(runtimeFailSafeGO);
            runtimeFailSafeGO = null;
        }
        if (runtimeCanvasGO)
        {
            Destroy(runtimeCanvasGO);
            runtimeCanvasGO = null;
        }
        if (!isDetached)
        {
            originalParent = null;
            originalCanvas = null;
        }
    }
}