using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public struct AbilityCooldownState : INetworkSerializable, IEquatable<AbilityCooldownState>
{
    public FixedString32Bytes Id;   // ex.: "dash", "grenade"
    public double EndTime;          // tempo de fim no relógio de rede (NetworkTime)

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref EndTime);
    }

    public bool Equals(AbilityCooldownState other) => Id.Equals(other.Id) && Math.Abs(EndTime - other.EndTime) < 0.0001;
}

public class AbilityCooldowns : NetworkBehaviour
{
    // Lista replicada: o servidor escreve; todos leem.
    public NetworkList<AbilityCooldownState> Cooldowns;

    // Cache (lado cliente) para consulta rápida
    private readonly Dictionary<string, int> indexById = new();

    void Awake()
    {
        Cooldowns = new NetworkList<AbilityCooldownState>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Cooldowns.OnListChanged += OnCooldownsChanged;

        // Se quiseres estados iniciais no servidor, podes setar aqui.
        if (IsServer && Cooldowns.Count == 0)
        {
            // Exemplo: registra habilidades conhecidas com EndTime 0 (prontas)
            // RegisterAbilityServer("dash");
            // RegisterAbilityServer("grenade");
        }
    }

    public override void OnNetworkDespawn()
    {
        Cooldowns.OnListChanged -= OnCooldownsChanged;
        base.OnNetworkDespawn();
    }

    private void OnCooldownsChanged(NetworkListEvent<AbilityCooldownState> change)
    {
        // Rebuild simples do índice quando a lista muda
        indexById.Clear();
        for (int i = 0; i < Cooldowns.Count; i++)
            indexById[Cooldowns[i].Id.ToString()] = i;
    }

    // ---------------- API de Servidor ----------------

    // Registra a habilidade se não existir (EndTime = 0 => pronta)
    public void RegisterAbilityServer(string id)
    {
        if (!IsServer) return;
        if (indexById.ContainsKey(id)) return;

        var state = new AbilityCooldownState
        {
            Id = new FixedString32Bytes(id),
            EndTime = 0
        };
        Cooldowns.Add(state);
        indexById[id] = Cooldowns.Count - 1;
    }

    // Tenta usar a habilidade: se pronta, inicia cooldown e retorna true; senão false
    public bool TryUseAbilityServer(string id, float cooldownSeconds)
    {
        if (!IsServer) return false;

        int idx = EnsureIndexServer(id);
        double now = NetworkManager.LocalTime.Time;

        var st = Cooldowns[idx];
        if (st.EndTime <= now)
        {
            st.EndTime = now + Mathf.Max(0.01f, cooldownSeconds);
            Cooldowns[idx] = st; // dispara replicação
            return true;
        }
        return false;
    }

    // Força o cooldown (ex.: cancelar, resetar, aplicar penalidade)
    public void SetCooldownServer(string id, float secondsFromNow)
    {
        if (!IsServer) return;
        int idx = EnsureIndexServer(id);
        double now = NetworkManager.LocalTime.Time;

        var st = Cooldowns[idx];
        st.EndTime = now + Mathf.Max(0f, secondsFromNow);
        Cooldowns[idx] = st;
    }

    // ---------------- Consulta (Cliente/Servidor) ----------------

    public float GetRemaining(string id)
    {
        if (!indexById.TryGetValue(id, out int idx)) return 0f;
        double now = NetworkManager ? NetworkManager.LocalTime.Time : Time.unscaledTimeAsDouble;
        double remain = Cooldowns[idx].EndTime - now;
        return (float)Math.Max(0.0, remain);
    }

    public bool IsReady(string id) => GetRemaining(id) <= 0.0001f;

    // ---------------- Helpers ----------------

    private int EnsureIndexServer(string id)
    {
        if (indexById.TryGetValue(id, out int idx)) return idx;

        var state = new AbilityCooldownState
        {
            Id = new FixedString32Bytes(id),
            EndTime = 0
        };
        Cooldowns.Add(state);
        idx = Cooldowns.Count - 1;
        indexById[id] = idx;
        return idx;
    }

    // Exemplo de ServerRpc: o cliente pede para usar a habilidade; servidor valida e inicia cooldown
    [ServerRpc(RequireOwnership = false)]
    public void RequestUseAbilityServerRpc(string id, float cooldownSeconds)
    {
        TryUseAbilityServer(id, cooldownSeconds);
        // Aqui também executarías o efeito da habilidade (dash, granada, etc.) no servidor.
    }
}