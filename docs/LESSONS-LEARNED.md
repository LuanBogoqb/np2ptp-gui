# Lições aprendidas — build do np2ptp-gui

Guia rápido de "não faz isso" / "sempre faz isso", baseado no que aconteceu de verdade
construindo essa GUI. Serve tanto pra manutenção futura desse projeto quanto de referência
pra próximos projetos WPF/.NET nessa máquina.

## Ambiente

- **`dotnet` nunca tá no PATH** nessa sessão de terminal (Bash nem PowerShell), mesmo depois
  de instalar o SDK via winget. Sempre chama pelo caminho completo:
  `"C:\Program Files\dotnet\dotnet.exe"` (PowerShell) ou `"/c/Program Files/dotnet/dotnet.exe"`
  (Bash). Se um dia isso for corrigido (PATH atualizado), essa nota fica obsoleta — mas até lá,
  não perde tempo tentando `dotnet build` pelado.

- **PowerShell 5.1 + `2>&1` em comando nativo = falso alarme.** Redirecionar stderr de um `.exe`
  nativo dentro do PowerShell 5.1 embrulha cada linha como `NativeCommandError` e derruba `$?`
  pra `$false` mesmo quando o processo saiu com código 0. Já me confundiu uma vez achando que um
  teste tinha falhado quando não tinha. Não usa `2>&1` em chamada de exe nativo no PowerShell.

## `UseWPF` + `UseWindowsForms` juntos (tray icon)

Botar os dois `true` no `.csproj` (necessário pra `NotifyIcon` de bandeja) causa `CS0104`
(referência ambígua) em **qualquer tipo que existe nos dois namespaces**: `Application`,
`MessageBox`, `Timer`, `Cursor`, etc. Sempre que usar um desses nomes num arquivo que também
mexe com `System.Windows.Forms`, qualifica: `System.Windows.MessageBox.Show(...)` ou usa
`using Application = System.Windows.Application;` no topo do arquivo. Isso vai continuar
acontecendo pra qualquer código novo que mexer nos dois — não é bug, é o preço de ter tray icon
em WPF sem lib de terceiro.

## Sinal de parada graciosa (CTRL_C pro processo filho)

`np2ptp` (lado Rust) usa `tokio::signal::ctrl_c()`, que no Windows só reage a `CTRL_C_EVENT`,
não a `CTRL_BREAK_EVENT`. E `GenerateConsoleCtrlEvent` só consegue mirar `CTRL_C_EVENT` em
"todo processo no console do chamador" — não dá pra mirar um processo específico com CTRL_C
(diferente do CTRL_BREAK, que aceita process group). Por isso o mecanismo
(`Interop/ConsoleCtrl.cs`) faz: `AttachConsole` no console do filho → `SetConsoleCtrlHandler`
(ignora no processo próprio) → dispara o evento → espera 200ms → desfaz o ignore → `FreeConsole`.

Dois detalhes que NÃO são só frescura de teste, são coisa de produção:
- **O `Thread.Sleep(200)` antes de desfazer o "ignorar CTRL_C" é obrigatório.** O evento é só
  enfileirado, não entregue na hora. Se desfizer o "ignorar" cedo demais, o próprio processo GUI
  pode se matar com o próprio CTRL_C que mandou pro filho. Não encolhe nem remove esse sleep.
- Qualquer chamada a `AttachConsole` deixa o processo chamador exposto ao broadcast de CTRL_C
  daquele console, seja app console ou GUI. Por isso a serialização (`SemaphoreSlim`) em volta de
  tudo isso é necessária mesmo com um usuário só clicando dois botões "Stop" quase ao mesmo tempo.

**Testar isso só funciona via PowerShell, nunca via Bash.** O pseudo-console do Git
Bash/MinTTY não propaga `GenerateConsoleCtrlEvent` pro processo filho — confirmado
isoladamente, inclusive com sandbox desligado e fora do VSTest. Se um teste envolvendo
`ConsoleCtrl`/`StopGracefullyAsync` falhar rodando via Bash, isso não é bug de código, é
limitação do terminal. Roda de novo via PowerShell antes de investigar.

**Flake conhecido e aceito:** `ProcessRunnerTests.StopGracefullyAsync_SendsCtrlCAndWaitsForCleanExit`
falha ocasionalmente (~1 em 8 execuções) com `STATUS_CONTROL_C_EXIT` (-1073741510) **só na
primeira execução de `dotnet test` logo após um build novo** — é timing de cold-start/JIT, não é
defeito real (reproduz até no baseline sem nenhuma mudança). Sempre passa de novo. Não confundir
com bug real — se aparecer, roda de novo antes de investigar.

## Bugs de concorrência achados (e como foram achados)

Todos esses só apareceram com teste de verdade batendo em processo real / thread real — nenhum
foi pego só lendo o código.

1. **`StopAsync` gravava "Completed" em vez de "Stopped"** — o handler `Exited` do
   `ProcessRunner` dispara síncrono e corria na frente da continuação retomada de `StopAsync`.
   Corrigido invertendo a ordem: marca `Stopped` **antes** de esperar `StopGracefullyAsync`, não
   depois. Root cause, não patch de timing.
2. **`vm.Status` (bind de UI) nunca era atualizado ao parar** — só `entry.Status` (histórico) era.
   Bug real, visível pro usuário ("clica Stop, linha continua dizendo Running pra sempre").
   Corrigido junto com o fix acima.
3. **`ConsoleCtrl.TrySendCtrlC` sem serialização** — estado Win32 process-wide
   (`AttachConsole`/`FreeConsole`/`SetConsoleCtrlHandler`) sem lock nenhum, alcançável em produção
   (`TaskManager` podia disparar 2+ `StopGracefullyAsync` concorrentes). Corrigido com
   `SemaphoreSlim` estático.
4. **`ConfigStore`/`HistoryStore.Save()` com race na escrita atômica** — a correção de "escrita
   atômica" (escreve em `.tmp`, depois `File.Move`) resolveu um problema (arquivo truncado num
   crash no meio da escrita) mas **criou outro**: nome de arquivo temporário fixo, então duas
   chamadas concorrentes de `Save()` na mesma instância colidiam no mesmo `.tmp` e explodiam com
   `UnauthorizedAccessException`. Isso é alcançável em produção porque os callbacks
   `EventReceived`/`Exited` do `TaskManager` podem disparar em threads de ThreadPool diferentes
   pra mesma operação, a poucos microssegundos de distância. Corrigido com lock de instância
   (`_saveLock`) em volta do corpo inteiro de `Save()`.
5. **Callbacks de evento do `TaskManager` sem try/catch nenhum** — qualquer exceção lá dentro
   escapava de `Dispatcher.Invoke` de volta pra thread de fundo que chamou, derrubando o processo
   inteiro (não só em teste — em produção também). Corrigido envolvendo o corpo dos handlers
   `EventReceived`/`Exited` em `lock(entry) { try { ... } catch { marca operação como Error } }`.

**Lição principal:** uma correção de segurança pode introduzir outro bug de segurança. O item 4
e 5 só foram achados porque rodei a suíte de teste de novo, manualmente, depois que DUAS revisões
completas de branch já tinham aprovado o código como "Ready to merge: Yes". Revisão de código não
substitui rodar de verdade — principalmente pra bug de concorrência, que é não-determinístico por
natureza.

## O que ficou de fora de propósito (v1)

Essas coisas estavam no design original mas foram deliberadamente cortadas do escopo pra sair
mais rápido — não é esquecimento, é decisão consciente registrada aqui pra não se perder:

- Botão "copiar link" no resultado de um pack.
- Atalho "Seed agora" (a partir de um resultado de pack, ir direto pro Serve).
- Botão "Abrir pasta" (abrir a pasta de saída no Explorer).
- Toggles de UI pra `--fec` / `--no-copy` (hoje só configuráveis via linha de comando do próprio
  np2ptp, não expostos na GUI).
- Botão "Tentar de novo" pro download do binário (hoje, se o auto-download falhar, o app mostra
  erro e fecha — não tem retry manual).
- Linhas de histórico restauradas de sessão anterior não suportam Stop/Retry (são só leitura —
  fazem sentido porque não têm `ProcessRunner` vivo por trás).
- Link de resultado de pack não é persistido no `TaskHistoryEntry` (some se reiniciar o app antes
  de copiar).
- Sem verificação de checksum do binário baixado (aceito dado o modelo de confiança: vem direto
  do GitHub Releases do próprio repo).

## Tema/dark mode

Pedido, mas **ainda não iniciado** — precisa de brainstorm próprio antes de desenhar (múltiplos
temas + dark mode com auto-detecção baseada no tema do Windows). Não é dívida técnica, é feature
nova ainda não escopada.
