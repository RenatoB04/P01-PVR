using UnityEngine;
using Unity.Netcode;   

/// <summary>
/// SpawnsManager
/// ---------------------------------------------------------------------------
/// Responsável por gerir os pontos de spawn dos jogadores no mapa.
/// Permite obter a próxima posição e rotação disponíveis, em ordem cíclica.
/// Pode ser usado para spawn inicial ou respawns.
/// ---------------------------------------------------------------------------
/// </summary>
public class SpawnsManager : MonoBehaviour
{
    /// <summary>
    /// Instância singleton global para fácil acesso
    /// </summary>
    public static SpawnsManager I;

    /// <summary>
    /// Array de Transforms representando os pontos de spawn no mapa
    /// </summary>
    public Transform[] points;

    /// <summary>
    /// Índice do próximo ponto a usar
    /// </summary>
    int nextIdx = 0;

    void Awake() => I = this; // Inicializa singleton na cena

    /// <summary>
    /// Obtém a próxima posição e rotação de spawn
    /// </summary>
    /// <param name="pos">Posição de spawn devolvida</param>
    /// <param name="rot">Rotação de spawn devolvida</param>
    public void GetNext(out Vector3 pos, out Quaternion rot)
    {
        // Se não houver pontos definidos, devolve posição e rotação padrão
        if (points == null || points.Length == 0)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            return;
        }

        // Seleciona o próximo ponto de spawn, usando modulo para ciclar
        var t = points[nextIdx % points.Length];
        nextIdx++;

        // Ajusta a posição ligeiramente acima do solo para evitar clipping
        pos = t.position + Vector3.up * 0.1f;
        rot = t.rotation;
    }

    /// <summary>
    /// Coloca um jogador (NetworkObject) na próxima posição de spawn
    /// </summary>
    /// <param name="playerObj">Objeto de rede do jogador a posicionar</param>
    public void Place(NetworkObject playerObj)
    {
        GetNext(out var pos, out var rot);       // Obtém próximo ponto
        playerObj.transform.SetPositionAndRotation(pos, rot); // Aplica posição e rotação
    }
}
