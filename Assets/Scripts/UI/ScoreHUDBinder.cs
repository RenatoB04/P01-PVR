using UnityEngine;
using TMPro;
using Unity.Netcode;

public class ScoreHUDBinder : NetworkBehaviour
{
    [Header("Refs (arrasta do Canvas)")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI killsText;

    private PlayerScore ps;

    public override void OnNetworkSpawn()
    {
        // Só o dono local atualiza a sua própria UI
        if (!IsOwner) { enabled = false; return; }

        // Procura o PlayerScore no mesmo Player (root)
        ps = GetComponentInParent<PlayerScore>();
        if (ps == null)
        {
            Debug.LogError("ScoreHUDBinder: PlayerScore não encontrado no Player.");
            enabled = false;
            return;
        }

        // Bootstrapping (mostrar valores atuais)
        RefreshAll();

        // Subscrever mudanças
        ps.Score.OnValueChanged += OnScoreChanged;
        ps.Kills.OnValueChanged += OnKillsChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (ps != null)
        {
            ps.Score.OnValueChanged -= OnScoreChanged;
            ps.Kills.OnValueChanged -= OnKillsChanged;
        }
    }

    private void OnScoreChanged(int prev, int curr)
    {
        if (scoreText) scoreText.text = "Score: " + curr;
    }

    private void OnKillsChanged(int prev, int curr)
    {
        if (killsText) killsText.text = "Kills: " + curr;
    }

    private void RefreshAll()
    {
        if (scoreText) scoreText.text = "Score: " + (ps != null ? ps.Score.Value : 0);
        if (killsText) killsText.text = "Kills: " + (ps != null ? ps.Kills.Value : 0);
    }
}