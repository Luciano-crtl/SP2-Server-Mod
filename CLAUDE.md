# SP2 Server Mod

Mod BepInEx para o SimplePlanes 2 (Unity 6, Mono, `netstandard2.1`, C#) para quem **organiza partidas customizadas**: spawns fixos, times, veículos predefinidos por time, feedback na tela e espectador para eliminados. O plano completo está em `PLANO.md`; este arquivo guarda o estado e as lições.

Companheiro do mod de blindagem (`C:\Users\lucia\source\repos\Armor Mod`, repositório do Luciano): as convenções de trabalho são as mesmas. O código decompilado do jogo (só estudo pessoal, não vai para o repositório) é consultado em `...\scratchpad\gamesrc\game` das conversas do Armor Mod; reenviar se faltar.

**Quem é o usuário:** Luciano, iniciante em programação e em Unity, usa Windows e o Visual Studio em português, conhece bem o SimplePlanes 2 como jogador. Responder no idioma da última mensagem; dizer sempre o que foi **testado no jogo** e o que só **compilou**; pedir o texto exato dos erros ou as últimas linhas de `BepInEx/LogOutput.log`.

## Estado

| Fase | Estado |
| --- | --- |
| F0 esqueleto em C#, plugin, `CompatibilityCheck` | Escrita e compilada (0 erros). **Não testada no jogo.** |
| F1 partida customizada com spawns fixos | Escrita e compilada. O XML gerado foi passado pelo **parser do próprio jogo fora do jogo** (`NetworkedActivityData.LoadFromXml` e `StartLocationData`): lê tudo certo e sobrevive à ida e volta da rede. **Registro na lista de atividades e a partida em si: não testados no jogo.** |
| Menu do mod + raios nos spawns (pedido à parte, ver "Menu do mod") | Escrito e compilado. O layout da janela, o texto das linhas e a checagem de compatibilidade foram conferidos **fora do jogo**. **A janela, o botão no menu de voo e o raio desenhado: não testados no jogo.** |
| F2 editor da partida no painel | Escrita e compilada. **Ida e volta conferida fora do jogo** (`scratchpad/roundtrip`, 40 verificações, todas ok): montar a partida como o painel faz, salvar, ler de novo (XML idêntico, acentos e `& " < >` no nome), sobrescrever, clonar (cópia funda), `FreeId`, apagar guardando cópia, e o `<Activity>` gerado passa pelo parser do jogo (`lives`/`killLimit` incluídos). **Os botões, os campos de texto, os controles deslizantes e o "Go to spawn" do painel: não testados no jogo.** |
| F3 veículo por time | Escrita e compilada. **Fora do jogo:** salvar/ler o veículo e o hash de cada time no `.match.xml`, copiar na partida, e os atributos `craft1/craftHash1/craft2` no `<Data>` da atividade (passam pelo parser do jogo e pela ida e volta da rede), `SafeName`, `TryLoad`, `Check` (17 verificações novas, todas ok). **Não testável fora do jogo:** o hash (`CraftDatabase.CompressCraftXml/ComputeHash` usam o profiler nativo do Unity) e tudo que troca o veículo. **A troca do veículo, o bloqueio e a devolução do veículo próprio: não testados no jogo.** |
| F4 regras no servidor | Escrita e compilada. **Fora do jogo:** a decisão do árbitro (`RefereeLogic`, 20 casos: vidas, limite de abates, times, empate, jogador sozinho) e a checagem de compatibilidade contra as DLLs reais do jogo (acha os métodos `RpcLogic___...` do servidor, `OnUpdateServer`, `CanJoinActivity` etc.). **Os patches em si (aplicar no jogo), a partida terminar, a recusa de spawn e a checagem de veículo no servidor: não testados no jogo.** |
| F5 feedback na tela | Escrita e compilada. **Fora do jogo:** os textos das mensagens (`RefereeLogic.StartText/Changes`, 10 casos: morte com vidas sobrando, eliminado, quem resta, time, nome do abatedor só quando é claro, sem mensagem sem vidas) e a compatibilidade com as DLLs do jogo. **As barras na tela, os marcadores no mundo e as mensagens aparecendo na tela de todos: não testados no jogo.** |
| F6 modo espectador | Escrita e compilada; a checagem de compatibilidade contra as DLLs reais do jogo acha tudo o que ela usa (`EnterKillCam`, `ExitKillCam`, `IsKillCam`, `GameInput`, `MainCockpit`...). **Nada do espectador rodou no jogo** (a escolha do alvo, a câmera presa em jogador remoto, as teclas e a dica na tela). |
| F7 robustez, testes e lançamento | **Pacote de teste v0.7.0 pronto** (ver "Distribuição"). A verificação da F7 é o teste do Luciano com outros jogadores (`dist-src/TEST-PLAN.txt`); os reparos e a versão pública final vêm depois dele. | `Lives` e `Kill limit` já são salvos no arquivo e no XML da atividade, mas **nenhuma regra os aplica ainda** (vem na F4). |

O projeto era VB (`Class1.vb` vazio) e foi trocado por C# na F0.

## Como usar (F1)

1. Copiar `SP2ServerMod.dll` (de `SP2-Server-Mod\bin\Debug\netstandard2.1`) para `BepInEx\scripts` do jogo (recarrega com **F6**) ou para `BepInEx\plugins`.
2. Na primeira vez o mod cria `BepInEx\config\sp2servermod\example.match.xml`. Cada `*.match.xml` dessa pasta vira uma atividade na categoria **Combat** do menu de atividades.
3. Em voo, **Insert** (`Keys/MarkSpawnKey`) escreve a linha `<Spawn .../>` do lugar onde o jogador está (log, tela e área de transferência) para colar no arquivo. **End** (`Keys/ReloadMatchesKey`) lê os arquivos de novo sem reiniciar o jogo.
4. Formato do `.match.xml`:

```xml
<Match id="arena" name="Arena" description="..." mode="TDM" timer="300">
  <Team id="1"><Spawn position="12873,4.55,-29883" rotation="0,45,0" onGround="true" speed="0" spread="15" /></Team>
  <Team id="2">...</Team>
</Match>
```

`mode`: `FFA` (padrão) ou `TDM`. `timer` em segundos. `spread` = quanto o jogo espalha, lado a lado, os veículos que largam juntos (padrão 15 m). Com `onGround="false"` e `speed` > 0 o veículo larga no ar com essa velocidade.

## Como funciona (fatos lidos no código do jogo)

- A lista de atividades é um dicionário (`NetworkedActivityManager._registeredActivities`) preenchido por `LoadActivitiesFromXml(XElement)`, que é público. O mod gera um `<Activity prefab="Deathmatch/DeathmatchActivity" category="Combat" icon="IconCombat" allowPeacefulMode="false">` com `<Data position rotation timer teamDeathmatch>` e `<StartLocations teamId="Team1|Team2">` (`MatchRegistry.BuildActivityXml`). O deathmatch lê só esses atributos.
- Ao criar uma atividade, o cliente manda o XML dela ao servidor, que instancia o prefab pelo nome e usa esses dados (não compara com os dele); os outros clientes recebem o mesmo XML. Por isso não precisa de rede própria na F1. **Não verificado no jogo:** que um servidor sem o mod aceita a atividade.
- O jogo não tem "remover atividade": `MatchRegistry.Unregister` tira as entradas do mod do dicionário privado por reflexão (para uma partida editada substituir a antiga e para o F6 limpar). `OnDestroy` do plugin chama isso.
- O menu de atividades monta a lista quando abre (`SelectActivityDialogScript.Start`); as categorias são fixas (Air Races, Car Races, Combat, Challenges), por isso as partidas ficam em Combat.
- `StartLocationData.GetSpawnLocationHashCode` identifica um local temporário pela posição **arredondada a 10 m**: dois spawns na mesma célula de 10 m contam como um. `MatchDefinition.Validate` avisa no log.
- `FlightScenePlayer.GlobalPosition` é a posição absoluta (a mesma dos locais de largada do XML); `Rotation.y` é o rumo.
- `Game.Instance.NetworkedActivityManager` existe desde o início do jogo; `MatchRegistry.Tick` espera por ele e registra uma vez (ou de novo se a instância mudar ou a tecla de recarregar for apertada).

## Menu do mod (janela no menu de voo)

- **Botão "SP2 Server Mod"** (azul) no menu de voo do jogo, ao lado do botão do Armor Mod; abre/fecha a janela (`UI/ServerPanel.cs`). A janela fica no canto **superior direito** (a do Armor Mod no esquerdo, para poderem estar abertas juntas) e não bloqueia o jogo; some junto com a interface de voo.
- **Linhas:** versão; "You are at" e "Nearest spawn" (ao vivo, a cada 0,5 s); botões **Reload matches** (o mesmo da tecla End) e **Copy spawn line here** (o mesmo da tecla Insert); seção **Spawn beams** com o interruptor "Beam at every spawn" e o controle "Beam height"; depois, por partida: o nome, modo e tempo, o estado do registro ("registered as SP2SM.x", ou por que não foi), e, para cada spawn, uma linha "Team N #i" com `x, y, z | hdg rumo | ground/air` (os mesmos dígitos do arquivo) e o interruptor **Vertical beam**.
- **Raio vertical (`Beams/SpawnBeams.cs`):** um `LineRenderer` em coordenadas do mundo, de 3 m abaixo do spawn até `Beams/BeamHeight` (padrão 1000 m, 100 a 3000, ajustável no menu e no `.cfg`), azul no time 1, vermelho no time 2, laranja no FFA, opaco no pé e quase transparente no alto. A posição é refeita **todo quadro** a partir da coordenada absoluta do spawn (`Utility.ConvertAbsoluteToFloatingOriginPosition`: o jogo move o mundo em volta do jogador), e a espessura cresce com a distância da câmera (0,0035 × distância, mínimo 0,6 m) para o raio aparecer de quilômetros. Material: o primeiro shader existente da lista de sempre (`Sprites/Default`...), camada "Mod Layer 1" (a câmera principal desenha; cai para a camada 0 se não existir). Os raios ficam "ligados" fora de um voo e voltam no voo seguinte; se a partida ou o spawn some do arquivo (editado e recarregado), o raio some.
- **Como o jogo constrói a janela (portado do Armor Mod, onde funciona):** três patches Harmony em `UI/UiPatches.cs`: `MenuPanelScript.OnWidgetInitialized` (Postfix, põe o botão), `Jundroo.Juicy.ResourceLoader.LoadXml` (Prefix, entrega o XML de layout para o caminho `Mods/SP2ServerMod/Panel`) e `ResourceLoader.GetScriptType` (Postfix, resolve nomes que começam com `SP2ServerMod.`). Os dois mods respondem só pelos próprios nomes e deixam o resto passar, então convivem. `UI/PanelBuilder.cs` cria os controles do próprio jogo (cabeçalho, linha de texto, interruptor, controle deslizante, botões).
- **Recarga (F6):** `Patcher` usa um id de Harmony **único por cópia do plugin** e `UnpatchSelf` no `OnDestroy`, para a cópia nova nunca perder os patches por causa da antiga; `ServerPanel.Tick` põe o botão no menu que já existe 1,5 s depois de carregar (a cópia nova perdeu o momento em que o jogo montou o menu); `OnDestroy` fecha a janela, tira o botão, destrói os raios e retira as partidas da lista do jogo.
- **`MatchRegistry`** agora guarda `Loaded` (as partidas lidas, mesmo as que não puderam ser registradas), `StatusOf(id)`, `Version` (sobe a cada leitura; a janela reconstrói as linhas quando muda) e `RequestReload()`.

## Editor da partida (F2)

Tudo no painel do mod; **cada mudança grava o `.match.xml` na hora** (`MatchEditing.Commit`: `MatchStore.Save` escreve num `.tmp` e troca o arquivo, depois `MatchRegistry.Apply` registra de novo na lista de atividades do jogo, então arquivo, painel e jogo ficam iguais).
- **Topo:** **New match** (cria `match.match.xml`, time 1 vazio, id livre pelo `FreeId`), Reload matches, Copy spawn line here.
- **Por partida:** nome e descrição (campos de texto; valem ao dar Enter ou sair do campo), **Two teams (TDM)** (liga/desliga o time 2), **Time limit** (30–3600 s, de 10 em 10), **Lives (F4)** e **Kill limit (F4)** (só guardados), **Duplicate** (id `<id>-copy`) e **Delete (keeps a copy)** (o arquivo vai para `sp2servermod\deleted\<arquivo>.<data-hora>`, nada se perde por um clique errado).
- **Por time:** **Add spawn here** (posição e rumo de onde o jogador está; no chão se estiver a menos de 10 m do solo, senão no ar com a velocidade atual; o raio do spawn novo já liga). O time 2 só aparece em TDM ou se já tem spawns (em FFA diz "not used in FFA").
- **Por spawn** (uma seção própria, dobrada sozinha se o time tem mais de 4): Vertical beam, **Starts on the ground**, **Air speed** (só no ar), **Spread**, **Go to spawn** (teleporta o próprio veículo com `PositionUtility.TeleportPlayer`; recusa dentro de uma atividade e onde o servidor desligou o teleporte), **Move here**, **Delete spawn** (os raios dos spawns seguintes acompanham os seus spawns: `SpawnBeams.RemoveSpawn`).
- O painel é reconstruído quando a lista de partidas muda (`MatchRegistry.Version`), quando um botão pede (`rebuildRequested`, feito no `Update` e não dentro do clique) ou quando o status/avisos de alguma partida mudam (assinatura, conferida a cada 0,5 s).
- **Novos arquivos:** `Match/MatchEditing.cs` (as operações), `Notifier.cs` (log + aviso na tela). `MatchDefinition` ganhou `Lives`, `KillLimit`, `Clone()`, `GetOrAddTeam()`; `MatchStore` ganhou `ToXml`, `Save`, `Delete`, `FreeId`; `MatchRegistry` ganhou `Apply`, `Add`, `Remove`, `WarningsOf`; `PanelBuilder` ganhou `TextInput`.
- **Limite do teste fora do jogo:** `Quaternion.Euler` (velocidade de um spawn no ar com `speed` > 0) é chamada nativa do Unity e só roda no jogo; o teste usa velocidade 0 nessa parte.

## Veículo por time (F3)

- **Definição:** `<Team id="1" craft="tank-a.xml" craftHash="...">` no `.match.xml` (`TeamDefinition.CraftFile/CraftHash`). Na atividade, `<Data craft1= craftHash1= craft2= craftHash2=>` (FFA só leva o do time 1): viaja a todos os clientes dentro do XML da atividade, então quem tem o mod conhece o veículo mesmo sem ter o arquivo da partida.
- **De onde vem o veículo (`Loadout/CraftProvider.cs`):** o arquivo mora em `BepInEx\config\sp2servermod\crafts`; se não está lá, procura o mesmo nome na lista de veículos do próprio jogo. Hash = o do jogo (SHA-256 em base64 do XML comprimido, `CraftDatabase.ComputeHash(CompressCraftXml(xml))`): se a definição tem hash e o veículo do jogador tem outro, **não é usado** (mensagem: peça o arquivo ao organizador). Sem hash na definição, qualquer versão serve.
- **No painel (por time):** "Team craft" (nome ou "none"), **Use the craft I'm flying** (salva o veículo que você pilota na pasta `crafts` como `<id>-team<N>.xml` e grava o hash), **No fixed craft**, e **Craft from my list** (digite o nome de um veículo da sua lista; é copiado para a pasta). A linha "Team craft" no topo mostra o estado ao vivo do veículo do jogador (`TeamLoadout.Status`). Quem joga precisa ter o arquivo na pasta `crafts` (o organizador entrega a pasta); a transferência automática entre jogadores (opção B do plano) **não foi feita**; para veículos publicados no site, ver abaixo.
- **Veículo publicado no site (`Loadout/SiteCrafts.cs`):** no painel, "Craft from the website" aceita o link (`.../a/<id>/...` ou `.../Feedback/View/<id>`) ou o id. O mod baixa como o próprio jogo faz (`Game.GetDownloadAircraftUrl(id)` + `WebRequest.Get` do jogo, resposta "0" = não existe; bytes lidos por `Utility.LoadCraftXmlFromBytes`, sem a janela de download do jogo, e com tempo limite de 60 s), grava `crafts\site-<id>.xml`, lê o hash do arquivo gravado e põe `craftSite` (e `craft`, `craftHash`) no time. A atividade leva `craftSite1/craftSite2`: o jogador que entra no time e **não tem o arquivo** baixa sozinho (estado "downloading the team's craft from the website") e a troca segue como antes; falha de download = aviso e nova tentativa em 30 s. O id só aceita letras e dígitos (`ParseId`; usado no nome do arquivo). **Limites:** baixa sempre a revisão atual do autor: se ele publicar outra revisão, o hash não bate e o jogador é avisado (o jogo aceita `revision` no endereço, mas não há como saber o número da revisão que o organizador usou); só funciona com internet; o hash e o download só rodam dentro do jogo.
- **Troca no jogador (`Loadout/TeamLoadout.cs`, a cada 0,25 s):** quando o jogador local está numa atividade `SP2SM.*` e num time (Team1/Team2) que tem veículo: carrega o veículo, grava como oculto `__sp2_<partida>_t<N>__.xml` na lista do jogo (nomes entre dois sublinhados duplos ficam escondidos), guarda o `CraftId` anterior, põe o novo e chama `FlightUI.RestartHere()` (espera o veículo terminar de carregar e o tempo de espera do jogo). Dali em diante todo reaparecimento usa esse veículo porque o jogo reaparece com o `CraftId`. Ao sair da partida (ou ela sumir), o `CraftId` volta e o veículo é reiniciado com o próprio. Time espectador: nada. Veículo que não achou ou com hash diferente: avisa uma vez e tenta de novo a cada 30 s (o jogador pode pôr o arquivo na pasta).
- **Bloqueio:** `CraftIdLockPatch` (Prefix no setter de `NetworkPlayerScript.CraftId`) devolve o veículo da partida a qualquer tentativa de troca (Change craft, copiar veículo da lista, designer) com a mensagem "This match requires your team's craft"; rede de segurança: o `Tick` confere o `CraftId` e refaz. Se o jogo tiver compilado o setter dentro dos chamadores, só a rede de segurança age (um ou dois reinícios a mais). Os arquivos `__editor__.xml` do jogador não são tocados (só o `CraftId` muda).
- **Não é regra de servidor:** quem não tem o mod voa o que quiser. A checagem no servidor (recusar veículo com outro hash: o servidor recebe o hash em `NetworkAircraftScript.RpcLoadRemoteCraftServer`) fica na **F4**, junto com o resto da imposição no servidor.
- **Armadilha de compilação:** `Game.Instance` sozinho não compila neste projeto (existe um namespace `Game` no caminho); usar `Assets.Scripts.Game.Instance`.

## Regras no servidor (F4)

Tudo roda **onde o jogo é o servidor** (quem hospeda a partida; só ele precisa do mod). Funciona para jogadores com e sem o mod. As regras só valem para atividades `SP2SM.*`.
- **Contagem (do próprio jogo):** o deathmatch já soma "Score" (abates) e "Deaths" (mortes) de cada jogador em todas as máquinas; o mod só lê (`Rules/MatchRules.cs`). Eliminado = `lives` > 0 e `Deaths` ≥ `lives`.
- **Fim da partida (`Rules/MatchReferee.cs` + `RefereeLogic.cs`):** um Postfix em `NetworkedActivityScript.OnUpdateServer` (a cada 0,5 s, só com a partida em `Started`) olha os jogadores em `Playing/Starting`: **limite de abates** (individual; com times, a soma do time), **vidas** (o último jogador, ou o último time, com vidas vence; todos fora = empate; um jogador sozinho nunca "sobra": é preciso ter havido pelo menos 2). Ao decidir: mensagem a todos (`ShowMessageToAllPlayers`, texto de fábrica) e `EndActivity()`. O tempo continua sendo do jogo (o criador da atividade). `Server/Referee` liga e desliga.
- **Sem volta sem vidas (`Server/EnforceRespawn`):** (1) servidor: Prefix em `NetworkPlayerScript.RpcLogic___RpcSpawnPlayerAircraft___*` (nome achado pelo começo: o número do fim muda com atualizações) recusa o spawn de eliminado de partida em andamento, com mensagem (`ShowMessageToTargetPlayer`); vale para qualquer caminho (menu, teclas, Change craft, designer). (2) cliente com o mod: Prefix em `FlightScenePlayer.SpawnAircraft` recusa antes de despawnar (não deixa o jogador pela metade). (3) `CanJoinActivity` (Postfix): com vidas, ninguém entra numa partida já iniciada (senão sair e entrar daria vidas novas, porque a contagem é por entrada). O eliminado continua "Playing" e sem veículo (modo espectador é a F6).
- **Checagem de veículo (`Rules/ServerCraftCheck.cs`, `Server/CraftCheck` = Off | Log | Enforce):** Prefix em `NetworkAircraftScript.RpcLogic___RpcLoadRemoteCraftServer___*`: quando o veículo do jogador chega ao servidor (hash SHA-256 do XML comprimido, que o dono manda), compara com `craftHash<N>` do time do jogador na atividade, só com a partida em `Starting/Started` (no saguão cada um ainda voa o seu). Combina → linha `[craft check] ... the hash matches`. Não combina → aviso no log com os dois hashes; em `Enforce`, mensagem ao jogador com o nome do veículo (e o id do site) e `RequestDespawn()` do veículo. **Padrão: `Log`.** O hash do mod (`CraftProvider.HashOf`) agora é o do próprio jogo: grava o veículo num arquivo de sondagem oculto (`__sp2_probe__.xml`) e o lê de volta pela lista do jogo, como acontece com o veículo de qualquer jogador; a conta é a mesma que o dono faz ao mandar. **Não confirmado no jogo:** que o hash do organizador é igual ao que o servidor recebe; por isso `Log` primeiro. O servidor confia no hash que o jogador manda quando já tem o veículo em cache (não é proteção contra trapaça).
- **No painel:** seção "Server rules (when you host)" (Referee, No spawn without lives, Craft check: off / log only / enforce) e a linha "In the match" (vidas e abates do jogador local). Os controles deslizantes "Lives" e "Kill limit" agora valem de verdade.
- **Não feito:** `CompareScores` (placar com o sobrevivente primeiro), espectador (F6), contar mortes do Armor Mod (usam a morte do jogo, deve contar, não verificado), HUD de vidas (F5). Um eliminado que sai e volta antes do início não ganha vidas (a contagem some com a saída, mas o `CanJoinActivity` impede a volta depois do início).

## Feedback na tela (F5)

- **Para todos, com ou sem o mod (`Server/Announce`, padrão ligado; no servidor, em `Rules/MatchReferee.cs`):** mensagens de fábrica (`ShowMessageToAllPlayers`) numa fila, uma a cada 1,5 s para uma rajada de mortes não enterrar as outras: o início ("The match begins: time 5:00, two teams, 3 lives each, first to 10 kills."), cada morte quando a partida tem vidas ("B was shot down by A: 2 lives left."; o nome do abatedor só aparece quando exatamente um jogador morreu e exatamente um jogador pontuou desde a última olhada, senão fica sem), e a eliminação ("B is out of lives (shot down by A). 2 players left." / com times "Alive: team 1 1, team 2 1."). Sem vidas na partida não há mensagem de morte (seria uma por morte). O fim da partida (F4) vem depois do que ainda estava na fila. Os textos são lógica pura (`RefereeLogic`), testada fora do jogo.
- **Para quem tem o mod, em `Feedback/MatchHud.cs` (`Display/MatchDisplay`, padrão ligado):** barras na borda esquerda, no meio da altura (o canto esquerdo de cima é do menu do jogo e o direito de baixo do Armor Mod), feitas como as do Armor Mod (botão `btn btn-flight` com barra que enche, `main-ui`, atrás dos menus do jogo): **Time** (tempo que falta, vermelho nos últimos 30 s), **Lives** (as suas, vermelho com 1 ou menos, "OUT" sem vidas), **Kills** (com o limite, se houver), **Alive** (com vidas: vivos/total, ou "a vs b" com times) e **Teams** (abates de cada time, na cor do seu). Só aparecem com a partida em `Starting/Started/Ending` e você dentro dela; recalculadas a cada 0,25 s e só tocam nos widgets quando algo muda. Lê tudo do que o jogo já compartilha (placar, tempo, dados da atividade): nada novo na rede. Falha → a exibição se desliga sozinha e o log diz.
- **Marcadores no mundo (`Feedback/SpawnMarkers.cs`, `Display/SpawnMarkers`, padrão ligado):** antes da partida começar (`Created/Initialized`), um anel e "Spawn N  1.2 km" nos spawns do seu time (FFA: time 1), lidos dos `StartLocations` da atividade e desenhados com o IMGUI do jogo (`OnGUI` do plugin, `WorldToScreenPoint` no ponto convertido para a origem flutuante). Somem ao começar a partida. Não é o raio vertical do organizador (esse é do painel e só dele).
- **Link do veículo no chat, para quem não tem o mod (`Server/CraftLink`, padrão ligado; `MatchReferee.SendCraftLinks/ChatTo`):** quando um jogador entra num time cujo veículo é do site (`craftSite<N>`), o servidor escreve no chat dele "Team N's craft: https://www.simpleplanes.com/a/<id>  (open it, download it, then Change craft and restart)", uma vez por time; e de novo quando a checagem de veículo (`ServerCraftCheck`) vê um veículo diferente do do time (em `Log` e `Enforce`). O jogo transforma qualquer endereço de simpleplanes.com recebido no chat em link clicável (`ChatMessages.ReplaceUrlsInMessage`: `<link=url>url</link>` azul; conferido fora do jogo com a própria classe do jogo). A mensagem sai pelo método **privado** `FlightSceneNetworkScript.SendChatMessageToTarget(conexão, -1, bytes)` (o que o servidor usa para responder um chat; dono -1 = mensagem do servidor, sem nome), chamado por reflexão (a checagem de compatibilidade confere que existe). **Limites:** o texto do link é o próprio endereço: o chat não aceita rótulo ("[Vehicle](url)" aparece com os colchetes e o endereço inteiro; o jogo ainda escapa `[` e `]`), então não dá para esconder o endereço de quem não tem o mod. O jogador precisa ter o chat ligado. O dono do jogo que hospeda não recebe (é quem manda). Só funciona com `craftSite` (veículo do site). **Quem tem o mod não recebe** (ver "Quem tem o mod" abaixo): um membro novo do time espera `ModHello.GraceSeconds` (7 s) pelo "hello" antes de ser tratado como jogador sem o mod; no caso de veículo diferente na checagem (`ServerCraftCheck`) o link vai a qualquer um.
- **Quem tem o mod (`Net/ModHello.cs`):** o cliente com o mod avisa "eu rodo o mod" (HELLO, tipo 210) a cada 5 s, só quando não é o servidor e há mais de um jogador. Vai pelo canal de mensagens de peça do jogo (`INetworkAircraft.SendPartNetworkMessage`, numa peça do veículo do próprio jogador: precisa de veículo carregado), que o jogo entrega a todos e que ignora tipos desconhecidos (quem não tem o mod nada vê). `HelloMessagePatch` (Prefix em `NetworkAircraftScript.ProcessPartNetworkMessage`) pega o tipo 210 e deixa os outros passarem (convive com o patch do Armor Mod, tipos 200 a 203). O host guarda o hello por 15 s (`ModHello.IsModded(idDoJogador)`; o jogador local conta como tendo o mod). No painel, "Players with the mod: 2 of 3 (counting you)". Padrão copiado do `NetSync` do Armor Mod, que já funciona no jogo; **nesta cópia ainda não testado**.
- **Painel:** seção "Match display (only you)" (os dois interruptores) e "Announce on screen" em "Server rules".
- **Posição das barras:** `MatchHud` usa `anchor=MiddleLeft`, `position=15 90 - 40·i`; se ficar torta (a âncora `MiddleLeft` e os deslocamentos são suposições a confirmar), os números estão no topo do arquivo. Não verificado onde o jogo mostra o seu próprio resumo de placar da atividade.
- **Não feito:** marcadores de companheiros/inimigos, ponto A (objetivos), HUD do espectador (F6).

## Modo espectador (F6)

`Spectator/Spectator.cs`; só para quem tem o mod; só uma câmera, **sem mexer no estado do jogador na atividade** (decisão: `ChangePlayerState(Spectating)` tiraria o jogador da lista de quem está jogando e o árbitro deixaria de ver "todos fora" e de contar os vivos; o eliminado continua "Playing", sem veículo e sem vidas, como o servidor já o trata).
- **Quando liga:** o jogador local está numa atividade `SP2SM.*` em `Started/Ending` e está eliminado (`MatchRules.IsEliminated`). Desliga quando a partida acaba, o jogador sai dela ou o espectador é desligado (`Spectator/Enabled`, painel "Spectator camera"). Ao ligar: mensagem "You are out of lives: you are watching the others (, previous, . next)".
- **A câmera:** a "câmera de impacto" do próprio jogo (`KillCameraController`, via `CameraManagerScript.EnterKillCam(PartScript)`: orbita a peça com o mouse, distância 15 m, centrada no corpo rígido se houver) na `MainCockpit` do alvo. A cada 0,5 s confere se a câmera ainda está no alvo e, se o jogo a tirou (ele põe o jogador a pé quando o veículo some), põe de volta. Ao terminar: `ExitKillCam()` (cai na visão de cabine se a anterior já não existe).
- **Alvos:** jogadores da atividade que não sou eu, não estão eliminados e têm veículo inteiro (`Aircraft` não nulo, não `CriticallyDamaged`, cockpit ativo); o meu time primeiro, depois por id (ordem estável). Se o alvo morre, sai ou é eliminado, passa sozinho para o próximo; sem ninguém: "Out of lives. Nobody left to watch." e a câmera fica onde está.
- **Teclas (`Spectator/NextKey` = `.`, `PreviousKey` = `,`):** próximo e anterior; não valem digitando em campo de texto. A tecla de trocar a vista do jogo (C) e a de vista anterior ficam sem efeito enquanto espectador (`SpectatorViewKeyPatch/SpectatorViewKeyDownPatch`, o mesmo que o Armor Mod faz na mira), senão tirariam a câmera do alvo.
- **Dica:** uma caixa na parte de baixo da tela ("Watching NOME (team 1)      , previous   . next"), desenhada no `OnGUI`; no painel a linha "Spectator" mostra o mesmo.
- **Dúvidas para o jogo:** (1) a câmera de impacto orbitando um veículo de **outro jogador** (cópia de rede): o `KillCameraController` usa o `Rigidbody` pai da peça, se não houver cai na posição da peça, e as cópias de rede podem não ter corpo físico (risco 7 do plano); (2) se o jogo, ao eliminado ficar a pé, reinicializa as câmeras com frequência (`ReinitializeCameras`): a recolocação de 0,5 s cobre, com um piscar; (3) o servidor recusa o spawn do eliminado, então ele fica sem veículo (a pé) o resto da partida.
- **Não feito:** câmera livre sem alvo; trocar de alvo pelo painel de jogadores do jogo; espectador para quem não tem o mod (não há como: o jogo não tem espectador, e sem o mod só há a vista a pé).

## Distribuição (pacote de teste v0.7.0, 2026-11-25)

- **Gerar:** `python tools/make-dist.py` (compila Release, lê a versão de `SP2ServerModPlugin.cs`, preenche `{version}` nos textos de `dist-src/` e monta `dist/SP2ServerMod-<versão>.zip`). Não publica nada.
- **Conteúdo do zip** (descompacta na pasta do jogo): `BepInEx/plugins/SP2ServerMod/SP2ServerMod.dll`, `README.txt` (instalar, fazer uma partida, regras, como reportar, limites) e `TEST-PLAN.txt` (roteiro com host, jogador com o mod e jogador sem o mod; os mesmos casos da F7). Textos em inglês, com fim de linha do Windows.
- **v0.7.0:** número escolhido por mim (o 0.1.0 era das fases iniciais); basta mudar `Version` e rodar o script de novo. O log de partida ganhou uma linha com a versão do jogo e as regras em vigor ("Game ..., Server rules (when this game hosts): ...").
- **Conferido:** compila sem avisos; os 101 testes fora do jogo (arquivo, XML, atividade, árbitro, mensagens, link) e a checagem de compatibilidade contra as DLLs reais do jogo passam rodando contra ESTA DLL de Release (SHA-256 `7fc388cc...`); o zip foi extraído numa pasta temporária e a DLL de dentro tem o mesmo hash.
- **Decisões que ficaram como estavam (o Luciano ainda não respondeu):** id do plugin `com.yourname.sp2servermod` (mudar renomearia o `.cfg`); sem licença; nenhuma marca `RELEASE-STRIP` (nada aqui é só de desenvolvimento: o `OnDestroy`/`Shutdown` também protege a saída do jogo e o `CompatibilityCheck` ajuda nos relatos); `Server/CraftCheck` continua em `Log`.
- **Para o teste:** pôr a DLL em `plugins` (nunca também em `scripts`; a cópia `SP2_ALT` do Luciano tem uma DLL de desenvolvimento em `scripts`, tirar de lá para testar o pacote).
- **Não feito (F7, depois dos testes):** os reparos do item 1 do plano (reenvio do "The match begins" depois do F6, aviso de compatibilidade no painel, limpeza dos arquivos ocultos), levar os testes para `tools/` do repositório, `PATCH_NOTES.md`, repositório git, nome/id definitivo e licença.

## Vários veículos por time (v0.8.0, 2026-11-26)

- **Modelo:** `TeamDefinition.Crafts` = lista de `CraftChoice` (`File`, `Hash`, `Site`, `Label` = o nome do próprio veículo, limpo por `CraftList.CleanLabel`: letras, números, espaço e `- _ . ( ) '`, 24 caracteres), no máximo `CraftList.Max` = 8. `Match/CraftChoice.cs` (sem nada do jogo, testado fora dele): `Upsert` (mesmo arquivo substitui), `IndexOfHash`, `Write/Read` do `<Data>` da atividade, `From`, `Accepts` (regra do servidor) e `DefaultIndex`. No `.match.xml`: `<Craft file= hash= site= label=/>` dentro de `<Team>`; o formato antigo (um veículo em atributos de `<Team>`: `craft`, `craftHash`, `craftSite`) ainda é lido como o primeiro da lista; a v0.7.0 não lê o formato novo (um arquivo gravado pela 0.8.0 perde os veículos se aberto na 0.7.0).
- **Atividade:** `<Data>` leva `<Craft team="1" file= hash= site= label=/>`, um por veículo (substitui os atributos `craft1/craftHash1/craftSite1`; a 0.7.0 não entende os novos). Tudo que chega pela rede é limpo em `CraftList.From` (arquivo só nome, id só letras e números).
- **Painel do host (por time):** "Team crafts" com uma linha e um botão "Remove craft N" por veículo, "Add the craft I'm flying", "Remove all crafts", "Add craft from the website", "Add craft from my list". Arquivos de veículo em voo: `<partida>-team<N>-<k>.xml` (primeiro `k` livre; `CraftProvider.NextFileName`); o mesmo hash não entra duas vezes. Cada veículo guarda o nome do próprio veículo como rótulo.
- **Jogador com o mod (`TeamLoadout`):** escolhe um da lista do seu time; padrão = o primeiro que ele pode usar (`CraftProvider.Available`: arquivo aqui, na lista do jogo, ou com id do site). Escolher: botões "Your team's crafts" no topo da janela (azul = o escolhido; "(missing)" = sem o arquivo) ou clicar na barra "Craft" da tela de voo (escolhe o próximo; `Feedback/MatchHud`, só com mais de um veículo). **Antes da partida começar (`Created/Initialized`) o veículo é trocado na hora; com a partida em andamento a escolha só vale no próximo reaparecimento** (`CraftId` muda, sem `RestartHere`), para ninguém trocar de veículo no ar. Cada veículo tem o seu arquivo oculto `__sp2_<partida>_t<N>_<k>__.xml`. A escolha fica na memória (por partida e time).
- **Servidor:** `CraftList.Accepts`: aceita qualquer veículo da lista; sem veículos no time, ou com algum veículo sem hash (nada para comparar), aceita tudo. A mensagem de recusa lista os nomes e, para os que têm id do site, o chat recebe uma linha por veículo ("Team 2 craft 2 of 3, Heavy Tank: <link>"; com um veículo só, o texto de antes). Quem não tem o mod e entra no time recebe uma linha por veículo com id do site.
- **Testes fora do jogo:** agora no repositório, `tools/model-test` (122 verificações; ver a seção "Distribuição"). **Não testado no jogo:** os botões e a barra de escolha, a troca antes e durante a partida, o servidor aceitando dois veículos de um time.

## Arquivos

```
SP2ServerModPlugin.cs      plugin, configurações (teclas), Update, OnDestroy
CompatibilityCheck.cs      confere por reflexão o que o mod usa do jogo ([COMPAT] no log)
Match/MatchDefinition.cs   modelo da partida e validação
Match/MatchStore.cs        lê e grava os .match.xml (Save atômico, Delete com cópia, FreeId, exemplo)
Match/MatchRegistry.cs     gera o <Activity>, registra/retira da lista do jogo, guarda as partidas lidas e o estado
Match/MatchEditing.cs      o que o painel faz: criar, duplicar, apagar, spawns (adicionar/mover/apagar), ir até o spawn, veículo do time
Loadout/CraftProvider.cs   de onde vem o veículo de um time, o hash, salvar o veículo em voo na pasta crafts
Spectator/Spectator.cs     câmera de espectador para o eliminado (alvo, teclas, dica, bloqueio da troca de vista)
Feedback/MatchHud.cs       barras do estado da partida na tela (tempo, vidas, abates, vivos, times)
Feedback/SpawnMarkers.cs   anéis nos spawns do seu time antes da partida começar
Rules/MatchRules.cs        lê vidas, limite de abates, mortes e abates da atividade; quem está eliminado; texto "In the match"
Rules/RefereeLogic.cs      a decisão de fim de partida, sem nada do jogo (testável fora dele)
Rules/MatchReferee.cs      o árbitro no servidor, recusa de spawn, entrada tardia
Rules/ServerCraftCheck.cs  checagem do veículo (hash) no servidor
Loadout/SiteCrafts.cs      baixa veículos publicados no site (link ou id), salva em crafts\site-<id>.xml
Loadout/TeamLoadout.cs     troca o veículo do jogador local ao entrar num time, bloqueia a troca, devolve o próprio
Notifier.cs                mensagem no log e na tela
SpawnMarker.cs             tecla (e botão) que anota a posição de um spawn
UiFocus.cs                 não disparar teclas do mod enquanto se digita (chat, campos)
Patcher.cs                 aplica cada patch Harmony isolado, id único por cópia, retira no OnDestroy
Beams/SpawnBeams.cs        raios verticais nos spawns
UI/ServerPanel.cs          botão no menu de voo, abrir/fechar, layout XML da janela
UI/ServerPanelScript.cs    a janela: linhas ao vivo e o editor das partidas e dos spawns
UI/PanelBuilder.cs         controles do jogo (portado do Armor Mod)
UI/UiPatches.cs            os três patches da interface
```

## Compilar e testar

- `dotnet build SP2-Server-Mod.csproj` (referências ao jogo em `C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2`; `-p:GameDir=...` se for outro lugar). Copiar a DLL e apertar **F6**: o log só mostra a DLL nova depois do F6.
- Verificação do XML fora do jogo: um programa de console em `net8.0` que referencia `SP2ServerMod.dll`, `Game.dll` e `Jundroo.Common.dll`, chama `MatchStore.Load` e `MatchRegistry.BuildActivityXml` e passa o resultado para `NetworkedActivityData.LoadFromXml` e `new StartLocationData(xml, Temp)`. Só serve para o que é puro XML (não usa nada do Unity que seja nativo: spawns no ar com velocidade usam `Quaternion.Euler`, que só roda dentro do jogo).

## O que testar no jogo (F6: espectador)

Dois ou mais jogadores, partida com **vidas = 1**, o jogador que vai morrer com o mod.
1. Ao morrer: mensagem "You are out of lives ... watching the others", a câmera passa a orbitar o veículo de um jogador vivo (arrastar o mouse gira em volta) e aparece a caixa "Watching NOME ...". Log: `Spectator: you are out of lives; watching the others` e `Spectator: watching NOME`.
2. `.` e `,` trocam de jogador (com 3 ou mais jogadores); `C` (trocar vista) não faz nada.
3. O jogador observado morre: a câmera passa sozinha para o próximo; sem ninguém vivo a caixa diz "Nobody left to watch".
4. A partida termina: a câmera volta ao normal (log `Spectator ends`).
5. Se a câmera ficar parada no lugar onde o veículo observado estava, ou não girar, é a dúvida (1): mandar o log.

## O que testar no jogo (F5: feedback na tela)

00. **Quem tem o mod:** com o host e outro jogador COM o mod, a linha "Players with the mod" do painel do host passa a "2 of 2" em até ~7 s; com um jogador sem o mod fica "1 of 2". O jogador com o mod que entra num time com veículo do site NÃO recebe o link no chat; o sem o mod recebe ~7 s depois de entrar no time.
0. **Link no chat (jogador sem o mod):** com um time cujo veículo veio de "Craft from the website", o jogador sem o mod que entra nesse time recebe no chat (aparece também como prévia embaixo) "Team N's craft: https://www.simpleplanes.com/a/..."; clicar abre a página do veículo. O log do servidor diz `Could not write in the chat of the player` se a chamada falhar.

1. Com a partida (vidas = 3, limite de abates = 5) em andamento, jogador com o mod: aparecem as barras na borda esquerda (Time, Lives, Kills, Alive; Teams em TDM) e mudam ao morrer/abater. Se a posição ou o tamanho estiver errado, mandar uma captura.
2. Antes de iniciar (no saguão), com o time escolhido: anéis "Spawn N ... m" nos spawns do seu time; ao começar a partida somem.
3. Outro jogador **sem o mod** (ou você com `Match bars` desligado): vê as mensagens de fábrica: início, "was shot down ... lives left", "is out of lives ..." e o fim.
4. Uma rajada de mortes: as mensagens saem uma a cada ~1,5 s, na ordem.
5. Desligar "Announce on screen": nada disso aparece na tela dos outros; os "Match bars" e "Spawn markers" desligam sozinhos pelo painel.

## O que testar no jogo (F4: regras no servidor)

Hospedar a partida (você é o servidor) com pelo menos um outro jogador (ou uma IA); o mod só age em partidas `SP2SM.*`.
1. Log ao carregar: `Patch applied:` para `RefereeTickPatch`, `LateJoinPatch`, `ServerRespawnPatch`, `ClientRespawnPatch`, `ServerCraftCheckPatch`; nenhum `Patch ... failed`.
2. **Vidas = 1**, dois jogadores: um mata o outro. O morto tenta reaparecer (Restart, Change craft): mensagem "You are out of lives" e nenhum veículo. Em seguida, a partida termina sozinha com "X is the last one standing!" e o placar.
3. **Limite de abates = 3**: ao 3º abate a partida termina com o vencedor. Com times, a soma do time.
4. **Entrada tardia:** com vidas, um jogador tenta entrar com a partida em andamento: recusado ("This match has lives and it is already running").
5. **Veículo por time e `CraftCheck`:** em `Log`, o log do servidor mostra `[craft check] ... the hash matches` para quem entrou com o veículo do time e o aviso com os dois hashes para quem voa outro. **Mandar essas linhas**: se os hashes do veículo do time não combinarem, não ligar `Enforce`. Em `Enforce` (só depois): quem voa outro veículo recebe a mensagem e o veículo some.
6. Sem vidas e sem limite de abates, nada muda (só o tempo termina a partida).

## O que testar no jogo (F3: veículo por time)

0. **Do site:** colar o link de um veículo seu publicado em "Craft from the website" do time: a mensagem "Downloading craft ..." e depois "Team N ... flies 'site-<id>.xml'"; o arquivo aparece em `crafts`. Apagar esse arquivo e entrar na partida: o mod baixa de novo sozinho (log `Downloading craft '<id>' from the website`).

1. Pilotar um veículo, abrir o painel, numa partida (TDM com os dois times com spawns) apertar **Use the craft I'm flying** no time 1: aparece o arquivo `crafts\<id>-team1.xml` e a linha "Team craft" muda para o nome. Repetir no time 2 com outro veículo.
2. Criar a atividade (Atividades → Combat) e entrar num time: a mensagem "Your team's craft: ..." aparece, o veículo é trocado no lugar e o log mostra `Loadout: team N of the match flies ...`.
3. Largar a partida: cada time sai nos seus spawns com o veículo do seu time. Morrer e reaparecer: o mesmo veículo.
4. Tentar **Change craft** durante a partida: aparece "This match requires your team's craft" e o veículo continua o do time.
5. Sair da partida: o seu veículo volta (o log diz `you left the match`).
6. Apagar o arquivo da pasta `crafts` e entrar: aviso "is not on your computer" (e de novo a cada 30 s); depois de pôr o arquivo, ele é usado.
7. Mandar o log (`SP2 Server Mod`, `Loadout`) se algo falhar: principalmente o que acontece quando se entra no time com o veículo trocado no meio do lobby.

## O que testar no jogo (F2: editor)

1. **New match**: aparece uma partida "New match" no painel e o arquivo `BepInEx\config\sp2servermod\match.match.xml`. Mudar o nome (Enter): o título da seção muda e o arquivo também.
2. Ir a um lugar, **Add spawn here** (time 1): a linha do spawn aparece com a posição certa e um raio liga nesse lugar. Repetir para outros lugares e para o time 2 (ligar "Two teams" antes).
3. Num spawn: desligar "Starts on the ground" mostra "Air speed"; mexer em "Spread"/"Time limit" e conferir o arquivo; **Move here** muda a posição; **Delete spawn** apaga e os raios dos outros ficam nos seus lugares.
4. **Go to spawn** (fora de uma atividade): o veículo é levado ao spawn.
5. **Duplicate** e **Delete**: a cópia aparece; a apagada vai para `sp2servermod\deleted`.
6. **Fechar o jogo, abrir de novo**: a partida está no painel (a leitura é a mesma do arquivo) e aparece em Atividades → Combat; criar e largar: cada time nos seus spawns.
7. Os controles deslizantes (Time limit, Lives, Kill limit, Air speed, Spread) gravam ao **soltar**; digitar o número na caixa ao lado também grava.
8. Se algo falhar, mandar as linhas do `LogOutput.log` com `SP2SM`.

## O que testar no jogo (menu e raios)

1. Depois do F6 (ou ao abrir o jogo), o log mostra `Patch applied: MenuButtonPatch`, `PanelLayoutPatch` e `PanelScriptTypePatch`, e `[COMPAT] everything the mod uses was found`.
2. Em voo, o menu do jogo tem o botão azul "SP2 Server Mod"; ele abre a janela no canto superior direito.
3. A janela mostra "You are at" e "Nearest spawn" mudando enquanto você anda, e a partida de exemplo com os spawns do time 1 e do time 2 (posição, rumo, chão ou ar).
4. O interruptor "Vertical beam" de um spawn liga um raio azul (time 1) ou vermelho (time 2) nesse lugar, visível de longe e do mesmo tamanho aparente de perto e de longe; desligar o apaga. "Beam at every spawn" liga todos; "Beam height" muda a altura na hora.
5. Voar para longe (a floating origin se move) e voltar: o raio continua no mesmo lugar do mapa.
6. Editar o arquivo, apertar o botão "Reload matches": as linhas da janela mudam; um spawn removido perde o raio.
7. Com o Armor Mod carregado, os dois botões e as duas janelas convivem.
8. Se o raio não aparecer, o log diz `Spawn beams use shader '...'` ou `Spawn beams cannot be drawn`.

## O que testar no jogo (F1)

1. O log mostra `SP2 Server Mod 0.1.0 loaded` e `[COMPAT] everything the mod uses was found`.
2. Depois de entrar na fase, o log mostra `Match 'Example match (edit me)' registered as activity SP2SM.example`.
3. Menu de atividades → Combat → a partida aparece; criar e largar: cada time nos seus spawns; o tempo vale o do `timer`.
4. Tecla Insert em voo: aparece a linha na tela e ela é copiada.
5. Editar o arquivo, apertar End, abrir o menu de novo: a mudança vale.
6. Servidor sem o mod (se der): a atividade criada por quem tem o mod roda?

## Regras de trabalho

- Compilar depois de toda mudança; nunca acessar membros do jogo que possam mudar de tipo sem passar por `CompatibilityCheck`/reflexão onde a falha seria silenciosa.
- Nada de push, tag ou release sem o "sim" explícito do usuário.
- Teclas do mod: evitar `[`, `]`, `P`, `G`, `L`, Alt; o Armor Mod usa Backspace, Tab, Shift, C, F8, F9, F10; o SP2Bridge usa F7; F6 é o recarregar do ScriptEngine.
