# Plano: SP2 Server Mod (partidas customizadas para hosts)

Objetivo: um mod BepInEx para o SimplePlanes 2 em que quem organiza uma partida define **veículos predefinidos por time**, **spawns fixos**, **feedback na tela** e **modo espectador para eliminados**, usando o framework de atividades do próprio jogo.

**Estado:** F0 a F6 escritas e compiladas; pacote de teste v0.7.0 pronto (`python tools/make-dist.py`) para a rodada de testes da F7 (ver `CLAUDE.md`). F0 a F2 foram testadas no jogo pelo Luciano; F3 e F4 só foram conferidas fora do jogo (arquivos, XML da atividade, a decisão do árbitro e a compatibilidade com as DLLs do jogo). A checagem de veículo no servidor (hash) entrou na F4, em modo `Log` por padrão até confirmar no jogo que os hashes combinam.

Tudo o que está em "Fatos do jogo" foi lido no código decompilado do `Game.dll` (nada foi testado no jogo ainda). O que não foi verificado está marcado como **[a verificar]** e vira uma tarefa de teste curto (spike) antes da fase que depende dele.

---

## 1. Decisões que mudam o resto

| Decisão | Recomendação | Por quê |
| --- | --- | --- |
| Linguagem | **Trocar o projeto de VB para C#** | O projeto atual é `SP2-Server-Mod.vbproj` com `Class1.vb` vazio. O mod Armor, os exemplos de Harmony/BepInEx e o decompilado do jogo são todos C#. Em VB não dá para reaproveitar o código do Armor (`SafePatcher`, `CompatibilityCheck`, `PanelBuilder`...) e o `tools/compile-check` só entende C#. Custa minutos agora e muito depois. |
| Mod separado ou dentro do Armor | **Plugin separado**, com ligação opcional ao Armor | O Armor Mod é de combate/blindagem; este é de organização de partidas. Quem só quer organizar partidas não precisa do Armor, e vice-versa. |
| Quem "manda" na partida | **O criador da atividade (organizador)**, não o dono do servidor | No jogo, quem cria a atividade é o "activity host" e ele roda a lógica de fim de partida, placar e mensagens. Isso funciona em qualquer servidor, até um sem o mod. O dono do servidor só entra se quiser imposição forte (ver §3). |
| Como a definição da partida viaja | **Dentro do XML da atividade** (`<Data>`) | O jogo já manda o XML inteiro a todos os clientes ao criar a atividade, e ignora atributos que não conhece. Não precisa de rede própria para a definição. |
| Mínimo de rede própria | O canal de mensagens de peça do jogo (como o Armor, tipos 200–203), com **tipos próprios 210–219** | Só para o que o jogo não carrega sozinho. Quem não tem o mod ignora essas mensagens. |

Nome de trabalho: **SP2 Server Mod** (id BepInEx `com.yourname.sp2servermod`, a trocar).

---

## 2. Fatos do jogo que sustentam o plano

**Atividades**
- A lista fica em XML (`Data/Activities/NetworkedActivities`): cada `<Activity>` tem `id`, `displayName`, `prefab`, `category`, `icon`, `requiredLocation`, `<Settings>`, `<ScoreTiers>` e `<Data>`.
- `NetworkedActivityManager.LoadActivitiesFromXml(XElement)` é público: o mod pode **registrar atividades novas**.
- Ao criar, o cliente manda o `NetworkedActivityData` inteiro (com o XML) ao servidor, que só instancia o prefab pelo nome (`Flight/Activities/<prefab>`) e usa os dados recebidos, sem comparar com os dele. Um servidor sem o mod, portanto, roda a partida (a testar).
- O deathmatch (`Deathmatch/DeathmatchActivityScript`) é **100% dirigido por dados**: `timer`, `teamDeathmatch` e `<StartLocations teamId="Team1|Team2">` com quantos `<Location position rotation startOnGround initialVelocity maxDistributionAmount>` quiser. Conta "Score" (abates) e "Deaths".
- Corridas e outros usam **prefabs** de fábrica para o percurso; o mod não cria percursos novos (fora do escopo).

**Veículo do jogador**
- É o texto `NetworkPlayer.CraftId` (nome de arquivo no `CraftDatabase`). Ao reaparecer, a máquina **do próprio jogador** lê o XML (`CraftDatabase.TryGetCraft` + `LoadXml`) e o envia comprimido aos outros: o host não precisa ter o arquivo.
- O botão "Change craft" faz `SaveCraft("__editor__.xml", xml, ...)` + `CraftId = ...` + `FlightUI.RestartHere()`. Arquivos `__nome__.xml` são ocultos da lista (convenção do jogo, como `Required Craft\__actBomberIntercept_Bomber__.xml`).
- Download pelo site: `Game.GetDownloadAircraftUrl(idDoSite)` + `Utility.LoadCraftXmlFromBytes`.
- Na largada, o jogo mede o veículo atual (`GetCraftBounds`), pede o spawn ao servidor e teleporta o veículo. Logo, **o veículo certo tem de estar no jogador antes da largada**.

**Restart, respawn e vidas**
- Todo respawn passa por `FlightScenePlayer.SpawnAircraft` e, no servidor, por `RpcSpawnPlayerAircraft` (`NetworkPlayerScript`). Caminhos de UI: `FlightUIScript.Restart/RestartHere/RepositionAtSpawn/RepositionAndUprightCraft`, copiar veículo da lista de jogadores, "Change craft", designer, carregamento de fase.
- O jogo não tem vidas. Mas o deathmatch já soma `Deaths` (evento `FlightScenePlayer.AircraftKilled`, com `KillerId`) e esse placar é de rede.
- O estado de jogador `Spectating` e o time `Spectator` existem e **nunca são usados**. `JoinTeam(jogador, Spectator)` é aceito pelo servidor (só `CanJoinTeam` pode recusar). No deathmatch sem times o tipo de time do Spectator é "hostil por jogador" (precisa de patch).
- Entrada tardia é permitida (`LateJoinActivity`); `CanJoinActivity` só nega depois que a atividade termina.

**Câmeras**
- `CameraManagerScript.EnterKillCam(PartScript)` (público) orbita qualquer peça; `ExitKillCam()` sai. `ChaseCameraController`, `OrbitCameraController` e `FlyByCameraController` aceitam uma `PartScript`; `SwitchToCamera(controlador)` é público. A cabine é `AircraftScript.MainCockpit`.

**Texto na tela de quem não tem o mod**
- `FlightSceneNetwork.ShowMessageToAllPlayers/ShowMessageToTargetPlayer` (texto, tempo, destaque) é exibido pelo código de fábrica. `ShowMessageServerRpc` tem `RequireOwnership = false` e o servidor repassa sem checar o remetente: **qualquer cliente com o mod pode mandar mensagens a todos** (precisa de limite no mod).
- Chat: `SendChatMessageToAllClients` (com filtro de palavrões). Relógio: `StartTimer/StopTimer`. Placar: só os ids que a classe da atividade registra ("Score", "Deaths").
- Não dá, sem mod no cliente, para desenhar HUD, marcadores ou colunas novas.

---

## 3. Arquitetura

### 3.1 Dois níveis de imposição (mesma lógica, dois "árbitros")

| Nível | Quem precisa ter o mod | O que garante |
| --- | --- | --- |
| **Organizador** (padrão) | O organizador e os jogadores que querem as regras | Funciona em qualquer servidor. Os clientes obedecem (troca de veículo, bloqueio de respawn, espectador); o organizador decide o fim da partida. Um jogador sem o mod pode burlar (voltar a jogar). |
| **Servidor** (opcional) | O dono do servidor | Em cima do anterior, o servidor recusa o respawn de eliminados (`RpcSpawnPlayerAircraft`) e pode checar o veículo de quem entra, valendo até para quem não tem o mod. |

Cada regra é escrita uma vez, como `IMatchRule`, e consultada pelos dois níveis.

### 3.2 O que cada tipo de jogador vê

| | Com o mod | Sem o mod |
| --- | --- | --- |
| Spawns, tempo, times | Sim | Sim (é só XML da atividade) |
| Veículo predefinido | Troca automática | Não (usa o próprio veículo) |
| Mensagens, relógio, placar k/d | Sim | Sim (texto de fábrica) |
| HUD próprio (vidas, objetivo, marcadores) | Sim | Não |
| Espectador | Câmera própria | Não (fica eliminado, sem respawn só no servidor com mod) |

### 3.3 Módulos (pasta do projeto C#)

```
SP2-Server-Mod/
  SP2ServerModPlugin.cs      plugin, configurações, patches (cada um isolado)
  CompatibilityCheck.cs      confere por reflexão tudo que o mod usa do jogo
  Match/
    MatchDefinition.cs       modelo: modo, tempo, vidas, limite de abates, times, spawns, veículos
    MatchStore.cs            ler/gravar .match.xml (pasta BepInEx/config/sp2servermod/)
    MatchRegistry.cs         monta o <Activity> e chama LoadActivitiesFromXml
    MatchSession.cs          estado da partida em andamento (lado de cada cliente)
  Rules/
    IMatchRule.cs            interface das regras
    LivesRule.cs             vidas, eliminação, fim da partida (árbitro = organizador)
    RespawnGuard.cs          bloqueios de restart/respawn (cliente e servidor)
    JoinGuard.cs             entrada tardia, veículo fora da regra
  Loadout/
    CraftProvider.cs         obter o XML (arquivo local, id do site, cache, hash)
    TeamLoadout.cs           trocar CraftId ao entrar/trocar de time, respawn, restaurar ao sair
  Spectator/
    SpectatorController.cs   estado Spectating, escolha do alvo, teclas
    SpectatorCamera.cs       câmeras de chase/órbita na cabine do alvo
  Feedback/
    Notifier.cs              mensagens de fábrica (com limite de frequência)
    MatchHud.cs              HUD do mod (placar de time, vidas, objetivo, marcadores)
  UI/
    MatchSetupPanel.cs       editor da partida dentro do jogo
  Net/
    MatchMessages.cs         mensagens 210–219 pelo canal de peça
```

Reaproveitar do mod Armor (copiar e adaptar, não referenciar): `SafePatcher`, `CompatibilityCheck`, `UI/PanelBuilder` + `ArmorPanelScript` (painel em Juicy), a técnica de `TurretAim.BlockViewKeys`, a de desenho do `TurretGroupsHud`/`AmmoHud` e `GL` do `TurretAim.OnGui`, o `Lifecycle` (recarga a quente, marcado `RELEASE-STRIP`) e o fluxo `make-release.py`.

---

## 4. Fases

Cada fase só vale depois da verificação indicada (como no mod Armor).

### F0. Esqueleto em C# (pequena)
- Criar o projeto C# `netstandard2.1` (referências copiadas do `ArmorFragments.csproj`: BepInEx, 0Harmony, Game, UnityEngine.*, FishNet via Game), `LangVersion latest`, símbolos embutidos. Remover o projeto VB da solução.
- Plugin que carrega, loga a versão, `CompatibilityCheck` (confere por reflexão: `NetworkedActivityManager.LoadActivitiesFromXml`, `FlightScenePlayer.SpawnAircraft`, `CameraManagerScript.EnterKillCam`, etc.).
- Preparar `tools/compile-check` apontando para o decompilado (como no Armor).
- **Verificação:** compila; o log mostra "loaded" e `[COMPAT]` sem faltas.

### F1. Atividade customizada com spawns fixos (média)
- `MatchDefinition` mínimo (nome, mapa/local, tempo, times sim/não, lista de spawns por time).
- `MatchRegistry` gera `<Activity prefab="Deathmatch/DeathmatchActivity" category="Combat" ...><Data position rotation timer teamDeathmatch><StartLocations ...>` e registra com `LoadActivitiesFromXml` depois de o `NetworkedActivityManager` existir.
- Spawn múltiplo: o jogo reparte por `maxDistributionAmount` e usa os locais em ordem nos respawns.
- **Verificação:** a atividade aparece na lista; criada, os jogadores largam nos spawns definidos; no TDM cada time no seu. Teste extra: **servidor sem o mod** aceita a atividade criada por um cliente com o mod **[a verificar]**.

### F2. Editor da partida dentro do jogo (média)
- `MatchSetupPanel`: nome, modo (FFA/TDM), tempo, vidas, limite de abates; lista de times.
- "Adicionar spawn aqui" (posição, rotação, no chão ou no ar, velocidade) a partir do veículo/câmera do organizador, por time; botões de apagar e de ir até o spawn; marcadores dos spawns no mundo (só para o organizador).
- Salvar/carregar `.match.xml` (compartilhável por arquivo).
- **Verificação:** montar uma partida só pelo painel, salvar, reabrir o jogo, carregar e criar.

### F3. Veículos predefinidos por time (grande)
- `CraftProvider`: fonte = arquivo local (pasta do mod) **ou** id do site (download sem diálogo, cache no disco), com **hash** guardado na definição para todos terem o mesmo veículo.
- `TeamLoadout`: ao entrar na atividade e ao entrar em um time (`PlayerJoined`/`TeamJoined`), gravar `__sp2_<partida>_<time>__.xml` (oculto), guardar o `CraftId` anterior, definir o novo e reaparecer no lugar (`SpawnAircraft` com local temporário "Restart here"), esperando carregar antes da largada. Ao sair ou terminar a partida, restaurar o `CraftId` anterior.
- Bloqueios: "Change craft" (`ChangeCraftScript.LoadSelectedCraft` e download por URL) e copiar veículo, enquanto a regra exigir veículo fixo.
- Transferência quando o jogador não tem o arquivo (opção B): **[a verificar]** copiar o XML de um veículo de rede do organizador (`AllowCopyCraftXml`/`GetCraftXml`) ou de um veículo de IA que ele cria, para não depender de download.
- **Modo servidor (decisão do usuário):** a troca automática roda no cliente, então só vale para quem tem o mod. Para quem não tem, o servidor **checa o veículo** (nome/hash do veículo carregado, a verificar como ler no servidor) e **recusa a entrada ou o respawn** com mensagem explicando qual veículo usar. A escolha final (recusar ou exigir o mod do cliente) fica em aberto, §7.
- **Verificação:** dois times com veículos diferentes; ao entrar/trocar de time o veículo muda; ao morrer e reaparecer volta o mesmo; ao sair da partida volta o veículo antigo; jogador sem o mod vê o veículo dos outros normalmente.

### F4. Vidas, eliminação e fim de partida, com imposição no servidor (grande)
**Decisão do usuário: o primeiro lançamento é o MODO SERVIDOR** (o dono do servidor instala o mod; quem entra não precisa dele). Por isso a imposição no servidor é a base desta fase, e o modo organizador fica como extra.
- **Imposição no servidor (prioridade):** prefixo em `NetworkPlayerScript.RpcLogic___RpcSpawnPlayerAircraft___…` (localizado por reflexão) que recusa o respawn de jogador eliminado numa partida em andamento, valendo também para quem não tem o mod; o servidor avisa o jogador com mensagem de fábrica. Recusar a entrada de veículo fora da regra (ver F3).
- Eliminado = `Deaths` ≥ vidas (placar de rede, sem mensagem nova). O árbitro é o servidor (o mod do dono confere a cada tick e chama `EndActivity()` quando restar um jogador/time, ou ao bater o limite de abates ou o tempo).
- Patches no deathmatch só quando o `<Data>` tiver atributos do mod (`lives`, `killLimit`, `lms`): `CompareScores` (sobrevivente primeiro), `GetTeamType` (Spectator neutro), `CanJoinActivity` (sem entrada tardia).
- `RespawnGuard`: patch em `FlightScenePlayer.SpawnAircraft` (cliente) negando o respawn de eliminado com mensagem; bloqueio de `RepositionAtSpawn`/`RepositionAndUprightCraft` durante a partida; modo servidor: prefixo em `RpcLogic___RpcSpawnPlayerAircraft___…` (**o nome contém um hash que muda com atualizações do jogo: localizar por reflexão, não por texto fixo**).
- Mortes pelo Armor Mod contam (passam por `VehicleRules.Kill`/morte do jogo) **[a verificar]**.
- **Verificação:** vida única: morrer = não consegue reaparecer por nenhum caminho (teclas, menu, trocar veículo, designer); a partida termina quando sobra um; o placar mostra o vencedor.

### F5. Feedback na tela (média)
- **Para todos (sem mod):** `Notifier` com mensagens de fábrica: início, "X eliminado por Y", "restam N", fim e vencedor. Limite de frequência e só o organizador envia.
- **Para quem tem o mod:** `MatchHud` (no estilo do `TurretGroupsHud`, widgets de Juicy dentro de `main-ui`): placar de time, vidas restantes, tempo, objetivo; marcadores no mundo (projeção de pontos para a tela, desenho com `GL`) para spawns, ponto A etc. Dados vêm do que já é de rede (placar, `Deaths`, XML); só o que faltar vai por `MatchMessages` (210–219).
- **Verificação:** com 2 clientes (um com mod, um sem), ambos veem as mensagens; só o com mod vê o HUD e os marcadores.

### F6. Modo espectador (média/grande)
- Ao eliminar: `ChangePlayerState(jogador, Spectating)` (**[a verificar]** se o próprio jogador pode) e, opcional, `JoinTeam(Spectator)`.
- `SpectatorCamera`: câmera de chase/órbita criada na `MainCockpit` do alvo e ativada com `SwitchToCamera` (ou `EnterKillCam` como plano B). **[a verificar]** se as cópias de rede têm `Body.RigidBody`; a câmera muda de alvo sozinha quando ele morre e sai ao fim da partida.
- Teclas próprias de próximo/anterior jogador e de câmera livre; bloquear a tecla de trocar vista do jogo (como o Armor faz); dica na tela.
- O jogo coloca o jogador a pé (`AvatarCameraTarget`) quando o veículo some: o mod reassume a câmera todo quadro.
- **Verificação:** o eliminado vê os vivos, alterna entre eles, não consegue reaparecer; ao fim da partida volta ao normal.

### F7. Robustez, testes e lançamento (média)
- Casos de borda: organizador sai (a atividade termina), jogador desconecta, entrada tardia, partida já em andamento ao recarregar (F6 do ScriptEngine, `Lifecycle`), mudança de versão do jogo.
- Teste multijogador local com **duas cópias do jogo** (jogo original como host e a cópia `C:\Geral\SP2_ALT` como cliente, se o usuário concordar; pedir permissão antes de agir nessa cópia).
- Documentação do jogador (`PATCH_NOTES.md`, em inglês), do desenvolvedor (`CLAUDE.md`, em português), formato do `.match.xml` e guia para organizadores.
- Versão para o público sem hot reload/diagnósticos (mesma técnica do `make-release.py`, marcas `RELEASE-STRIP`).

### Depois (ideias, fora do primeiro lançamento)
- Objetivos: capturar pontos (zonas), bandeira, escolta; placar de time por pontos.
- Modo servidor completo (checar veículo e imposição para todos), lista de partidas na lobby.
- Outras atividades de fábrica com dados editáveis (Mech Invasion, Bomber Intercept).
- Percursos de corrida novos (exige clonar prefab de pista em execução).

---

## 5. Riscos e incertezas (cada um com um teste curto)

| # | Incerteza | Teste antes de |
| --- | --- | --- |
| 1 | Servidor sem o mod aceita uma atividade registrada só no cliente do organizador | F1 |
| 2 | Atributos extras no `<Data>` chegam intactos aos outros clientes e não quebram a atividade de fábrica | F1 |
| 3 | Baixar veículo do site de dentro do mod, sem a janela do jogo | F3 |
| 4 | Trocar `CraftId` + `SpawnAircraft` na lobby não atrapalha a largada (medida do veículo, spawn) | F3 |
| 5 | Copiar o XML do veículo de outro jogador/IA para distribuir sem download | F3 (opção B) |
| 6 | `ChangePlayerState` pelo próprio jogador e `JoinTeam(Spectator)` funcionam | F6 |
| 7 | Cópias de rede de veículos têm corpo físico para a câmera seguir | F6 |
| 8 | Patch no `RpcLogic___RpcSpawnPlayerAircraft` (nome com hash) localizado por reflexão e seguro | F4 (modo servidor) |
| 9 | Patch em métodos privados do deathmatch (`CompareScores` etc.) só com atributos do mod, sem afetar partidas de fábrica | F4 |
| 10 | Mortes causadas pelo mod Armor contam abate/morte no placar | F4 |
| 11 | Atualização do jogo muda assinaturas (`CompatibilityCheck` + `[COMPAT]` no log) | contínuo |

---

## 6. Convenções de trabalho (iguais às do Armor Mod)
- Responder no idioma da última mensagem; dizer sempre o que foi **testado no jogo** e o que só **compilou**.
- Compilar depois de toda mudança; depois copiar a DLL para `BepInEx\scripts` e pedir **F6** (o log só mostra a DLL nova depois do F6).
- Cada patch isolado e registrado no log (`Patch applied: ...`); nunca acessar diretamente membros do jogo que possam mudar de tipo (usar reflexão onde a falha seria silenciosa).
- Mudanças e lições anotadas no `CLAUDE.md` do projeto novo (criar na F0).
- Tags e releases só por script, com confirmação do usuário antes de qualquer push.

---

## 7. Perguntas em aberto para o usuário
1. Trocar o projeto de VB para C# (recomendado)?
2. ~~Prioridade do primeiro lançamento~~ **Decidido: modo servidor** (o dono do servidor instala o mod). F0 e F1 não mudam; a imposição no servidor sobe para a F4/F3; HUD e espectador por câmera (F5/F6) ficam como extras para quem tem o mod. Pendente: com jogador sem o mod e veículo errado, recusar a entrada ou exigir o mod?
3. Modos do primeiro lançamento: FFA e TDM com vidas (1 ou N)? "Último de pé" entra já na F4?
4. Fonte dos veículos: arquivos + id do site (F3 básica) bastam, ou a distribuição automática pelo organizador (opção B) é requisito?
5. Nome do mod e id do plugin.
6. Uma partida por vez por servidor, ou várias atividades simultâneas?
