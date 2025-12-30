# Documentação de Rede – Clash Arena

## 1. Visão Geral do Sistema de Rede

O **Clash Arena** é um FPS de arena 3D desenvolvido na **Unity 6.2** com **Universal Render Pipeline (URP)**, concebido para suportar **multiplayer competitivo** (PvP) e partidas com **bots controlados por IA**.

A arquitetura de rede combina **Photon PUN** e **Netcode for GameObjects (NGO)**:

* **Photon PUN**: Para matchmaking, criação de salas, lobbies e comunicação fiável.
* **NGO**: Para sincronização em tempo real de jogadores, projéteis, pontuação e bots.

Esta combinação permite:

* Jogos de baixa latência com atualização rápida de estado de jogo (UDP via NGO).
* Comunicação fiável e persistente para lobby, chat e eventos críticos (TCP via Photon PUN).

---

## 2. Tecnologias de Rede

| Tecnologia                    | Função                                                                    | Protocolo        |
| ----------------------------- | ------------------------------------------------------------------------- | ---------------- |
| Photon PUN                    | Criação de salas, lobby, matchmaking, chat                                | TCP (fiável)     |
| Netcode for GameObjects (NGO) | Sincronização de entidades dinâmicas, projéteis, posição, vida, pontuação | UDP (não-fiável) |
| Unity Relay                   | Facilita conexões de host/client sem necessidade de abrir portas          | UDP/TLS          |

**Nota sobre protocolos:**

* **TCP** garante entrega de mensagens, usado para eventos de lobby, chat e atualizações de baixa frequência.
* **UDP** transmite dados críticos de jogabilidade (posições, projéteis) com baixa latência, tolerando perda ocasional de pacotes.

---

## 3. Arquitetura de Rede

### 3.1 Lobby e Criação de Sala (Photon PUN)

O **LobbyManager** gere toda a experiência de lobby:

* Criação de salas privadas com códigos únicos.
* Entrada em salas existentes via código.
* Preparação para início do jogo com countdown.
* Ligação ao **Unity Relay** para inicializar o Host/Client via NGO.

**Exemplo: criação de sala com código único:**

```csharp
void OnClickCreate()
{
    string code = GenerateRoomCode(6);
    var options = new RoomOptions
    {
        MaxPlayers = 2,
        IsVisible = false,
        IsOpen = true,
        CustomRoomProperties = new Hashtable { { "relay", "" } },
        CustomRoomPropertiesForLobby = new[] { "relay" }
    };
    PhotonNetwork.CreateRoom(code, options, TypedLobby.Default);
}
```

**Fluxo:**

1. Jogador liga ao Photon (`OnClickConnect()`).
2. Cria ou entra numa sala.
3. Master Client define `startCountdown` e propriedades de Relay.
4. Todos os jogadores recebem notificação de início do jogo (`OnRoomPropertiesUpdate`).

---

### 3.2 Inicialização de Host/Client via Relay (NGO)

O Host cria uma alocação Relay e inicia o servidor NGO:

```csharp
async Task StartHostWithRelayAndLoadAsync()
{
    Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
    string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
    PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { "relay", joinCode } });

    var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(alloc, "dtls"));

    NetworkManager.Singleton.StartHost();
    NetworkManager.Singleton.SceneManager.LoadScene("Prototype", LoadSceneMode.Single);
}
```

Clientes usam o `joinCode` para se ligar:

```csharp
async Task StartClientWithRelayAsync(string joinCode)
{
    var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
    JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAlloc, "dtls"));
    NetworkManager.Singleton.StartClient();
}
```

---

### 3.3 Spawn Seguro de Jogadores

* `NetworkSpawnHandler` assegura spawn inicial apenas para o proprietário (`IsOwner`).
* Chama `RespawnServerRpc(ignoreAliveCheck: true)` para spawn inicial seguro.
* Previne colisões ou spawn duplicado.

**Exemplo de spawn seguro:**

```csharp
public override void OnNetworkSpawn()
{
    if (IsOwner)
    {
        StartCoroutine(SafeRespawnCoroutine());
    }
}

private IEnumerator SafeRespawnCoroutine()
{
    yield return null;
    respawnController.RespawnServerRpc(true);
}
```

---

### 3.4 Testes Locais de Rede

* `NetworkUI_TestButtons` permite iniciar **Host, Client ou Server** diretamente na cena.
* Cria automaticamente `NetworkObject` para cada jogador.
* Demonstra spawn sincronizado entre clientes.

---

### 3.5 Sincronização de Qualidade de Rede

#### 3.5.1 Packet Loss – LossProbe

* Mede perda de pacotes em tempo real usando **ServerRPCs** e **ClientRPCs**.
* Mantém histórico de pacotes enviados e ecoados.
* `CurrentLossPercent` fornece a percentagem de pacotes perdidos.

#### 3.5.2 Debug Overlay – NetworkDebugOverlay

* Mostra **PING**, **LOSS** e **FPS**.
* Usa dados do `UnityTransport` e `LossProbe`.
* Permite alternar visibilidade com tecla (`F3` por defeito).

**Exemplo: leitura de perda de pacotes:**

```csharp
if (LossProbe.Instance)
{
    float loss = LossProbe.Instance.CurrentLossPercent;
    debugText.text = $"LOSS: {loss:F1}%";
}
```

---

### 3.6 Gestão de Spawn Points

* `SpawnsManager` gere os pontos de spawn da arena.
* Garante que cada jogador recebe spawn seguro, alternando entre os pontos definidos.

**Exemplo: spawn automático:**

```csharp
var playerObj = Instantiate(playerPrefab);
SpawnsManager.I.Place(playerObj.GetComponent<NetworkObject>());
```

---

### 3.7 Chat no Lobby (Photon Chat)

* `SimpleLobbyChat` usa **Photon Chat** para comunicação em lobby.
* Mensagens são fiáveis (TCP) e distribuídas a todos os jogadores.
* Permite auto-conexão para testes, validação de nome, e mensagens do sistema.

**Exemplo de envio de mensagem:**

```csharp
_chat.PublishMessage("global-lobby", "Olá a todos!");
```

---

### 3.8 Bootstrap e Persistência

* `NetcodeBootstrap` mantém instância única do `NetworkManager` entre cenas.
* Previne múltiplas instâncias que causariam conflitos de rede.

```csharp
void Awake()
{
    var others = FindObjectsOfType<NetcodeBootstrap>();
    if (others.Length > 1) Destroy(gameObject);
    DontDestroyOnLoad(gameObject);
}
```

---

## 4. Fluxo Completo – Do Lobby ao Jogo

1. Jogador conecta ao Photon Master (`LobbyManager.OnClickConnect()`).
2. Cria ou entra em uma sala.
3. Master Client define início do jogo (`startCountdown`) e propriedades Relay.
4. Todos recebem notificação de Relay e conectam via NGO.
5. Host inicializa `NetworkManager` e carrega a cena.
6. Jogadores recebem spawn seguro via `NetworkSpawnHandler`.
7. Projéteis, movimentos e ações são sincronizados em tempo real.
8. Bots também são entidades de rede, com decisões FSM sincronizadas.
9. `LossProbe` e `NetworkDebugOverlay` monitorizam a qualidade da rede.

---

## 5. Boas Práticas e Considerações Técnicas

* **Server-authoritative**: Todo o dano, projéteis e respawns são validados pelo servidor.
* **Bots como entidades de rede**: As decisões táticas são sincronizadas com todos os clientes.
* **Monitorização da rede**: Sempre ativar `LossProbe` e `NetworkDebugOverlay` durante testes.
* **Spawn seguro**: Evitar spawn duplicado ou colisões usando `NetworkSpawnHandler`.
* **Relay**: Ideal para evitar problemas de NAT e portas fechadas.

---

Projeto realizado por:
- Paulo Bastos 27945
- Bruno Mesquita 27947
- José Lima 27935
