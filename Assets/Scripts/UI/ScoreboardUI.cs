using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class ScoreboardUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text listText;

    [Header("Input (novo Input System)")]
    [Tooltip("Ação para mostrar o scoreboard (ex.: Tab). Usa IsPressed() para manter aberto enquanto carregado.")]
    [SerializeField] private InputActionReference showScoreboardAction;

    [Header("Opções")]
    [Tooltip("Atualizações por segundo quando o painel está visível.")]
    [SerializeField] private float refreshRate = 10f;

    float nextRefreshTime;

    void OnEnable()
    {
        if (showScoreboardAction && !showScoreboardAction.action.enabled)
            showScoreboardAction.action.Enable();

        if (panel) panel.SetActive(false);
        nextRefreshTime = 0f;
    }

    void OnDisable()
    {
        if (showScoreboardAction && showScoreboardAction.action.enabled)
            showScoreboardAction.action.Disable();
    }

    void Update()
    {
        bool wantShow = showScoreboardAction != null && showScoreboardAction.action.IsPressed();

        if (panel && panel.activeSelf != wantShow)
        {
            panel.SetActive(wantShow);
            // refresh imediato ao abrir
            if (wantShow) RefreshNow();
        }

        if (wantShow && Time.unscaledTime >= nextRefreshTime)
        {
            RefreshNow();
            nextRefreshTime = Time.unscaledTime + (refreshRate > 0f ? 1f / refreshRate : 0.2f);
        }
    }

    void RefreshNow()
    {
        if (!listText)
            return;

        // Apanha todos os PlayerScore presentes na cena (replicados pelo Netcode)
        var scores = FindObjectsOfType<PlayerScore>(includeInactive: true);
        if (scores == null || scores.Length == 0)
        {
            listText.text = "No players.";
            return;
        }

        // Cria snapshot ordenado: Score desc, Kills desc, ClientId asc
        var sorted = new List<(string name, int kills, int score)>(scores.Length);
        foreach (var ps in scores)
        {
            if (ps == null) continue;

            string pname = TryGetPlayerName(ps.gameObject);
            int kills = 0;
            int score = 0;

            // PlayerScore em Netcode: usa NetworkVariable<Value>
            // (Se no teu projeto a classe tiver outros campos, adapta aqui)
            try
            {
                kills = ps.Kills != null ? ps.Kills.Value : 0;
                score = ps.Score != null ? ps.Score.Value : 0;
            }
            catch
            {
                // fallback caso não seja NetworkVariable
                var type = ps.GetType();
                var kf = type.GetField("Kills");
                var sf = type.GetField("Score");
                if (kf != null) kills = (int)(kf.GetValue(ps) ?? 0);
                if (sf != null) score = (int)(sf.GetValue(ps) ?? 0);
            }

            sorted.Add((pname, kills, score));
        }

        var ordered = sorted
            .OrderByDescending(e => e.score)
            .ThenByDescending(e => e.kills)
            .ThenBy(e => e.name, System.StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("PLAYER                Kills   Score");
        sb.AppendLine("-----------------------------------");
        foreach (var e in ordered)
            sb.AppendLine($"{e.name,-20}  {e.kills,5}   {e.score,5}");

        listText.text = sb.ToString();
    }

    string TryGetPlayerName(GameObject go)
    {
        // Tenta componente "PlayerName" com string pública Name
        var playerNameComp = go.GetComponent("PlayerName");
        if (playerNameComp != null)
        {
            var nameProp = playerNameComp.GetType().GetField("Name");
            if (nameProp != null)
            {
                var val = nameProp.GetValue(playerNameComp);
                if (val != null) return val.ToString();
            }
        }

        // Usa OwnerClientId se houver NetworkObject
        var no = go.GetComponent<NetworkObject>();
        if (no) return $"Player {no.OwnerClientId}";

        // Fallback: nome do GameObject
        return go.name;
    }
}