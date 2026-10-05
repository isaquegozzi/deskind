# Plano do MVP e gates

Status em 2026-08-14. Um gate só fica concluído com comando/evidência registrado.

## M0 — Foundation (concluído em 2026-08-13)

Entregas: monorepo, projetos Android/Windows, protocolo inicial, testes, scripts,
documentação e inventário da toolchain.

Gate:

- [x] Android builda com wrapper/toolchain fixados.
- [x] Windows builda com zero warnings.
- [x] testes básicos passam.
- [x] arquitetura, protocolo, latência, segurança, riscos e toolchain documentados.

Evidência: `docs/evidence/m0-2026-08-13.md`.

## M1 — Android stylus telemetry (concluído em 2026-08-13)

Critérios de aceitação:

- [x] APK instalado e inicializado no tablet físico autorizado.
- [x] stylus identificada por tool type/device (`sec_e-pen`).
- [x] X/Y variam e respeitam o tamanho da área ativa.
- [x] pressure exercida em sessão guiada; driver reporta range 0..1.
- [x] tilt/orientation variam e foram capturados.
- [x] hover/distance exibidos (distância observada 104).
- [x] side button preservado; eraser permanece UNKNOWN por não ter sido reportado.
- [x] ordem coberta por implementação/testes; 4.111 historical samples contados
  em um gesto físico de dedo.
- [x] cinco cancelamentos foram observados sem estado/crash.
- [x] tela mostra capabilities/ranges e telemetria viva sem logging por amostra.
- [x] build, testes unitários e sessão física parcial têm evidências salvas.

Evidência física: `docs/evidence/m1-2026-08-13.md`.

## M2 — Protocol core (concluído em 2026-08-13)

Formato v1 congelado, encoder/decoder Kotlin e C#, golden vector compartilhado,
validação de malformed frames, sequence tracker, state machine/failsafe e
transporte in-memory implementados. Gate: 100.000 amostras passaram, com 1.896
bytes de crescimento retido após full GC. Evidência: `docs/evidence/m2-2026-08-13.md`.

## M2.5 — USB/ADB transport (concluído em 2026-08-13)

Dois `adb reverse`, handshake autenticado por token de bind, canais TCP separados,
framing, lifecycle, backpressure, diagnósticos e receiver Windows implementados.
No tablet físico: 51.799 amostras (45.365 históricas), 4.184 frames e zero drops;
pressure/tilt/orientation/distance variaram no decoder Windows, sem gaps,
duplicatas ou reorder. Desconectar o cabo durante contato produziu `release=Up`;
reaplicar reverse e usar reconexão manual criou nova sessão funcional. Evidência:
`docs/evidence/m2.5-2026-08-13.md`.

## M3 — Mouse via USB (concluído em 2026-08-13)

Movimento absoluto, monitor mapping, clique, drag, right click e scroll por dedo
implementados com `SendInput`, opt-in por `--enable-mouse`. Gate físico no monitor
principal: 21.811 movimentos, 33/33 left down/up, 8/8 right down/up e scroll,
sem gaps, duplicatas, reorder ou botão preso. Evidência:
`docs/evidence/m3-2026-08-13.md`.

## M4 — Synthetic pen via USB (concluído em 2026-08-14)

PT_PEN DOWN/UPDATE/UP/hover/pressure/tilt/buttons e watchdog. Gate: input correto
em aplicações Windows apropriadas e nenhum estado preso em perda de conexão.
Pressão, hover, contato, reconexão e failsafe foram confirmados fisicamente. O app
detecta a quantidade de monitores no handshake e alterna o alvo sem reiniciar o
host. Evidência: `docs/evidence/m4-2026-08-14.md`.

## M5 — Global overlay (concluído em 2026-08-14)

Pen, highlighter, eraser, undo, redo e clear sobre uma janela transparente global,
sem screenshot e com modelo vetorial.
Gate físico aprovado com seletor de saída, dois monitores, click-through,
highlighter translúcido e controles vetoriais. Evidência:
`docs/evidence/m5-2026-08-14.md`.

## M6 — LAN transport

Discovery, pairing/autenticação, control TCP, input UDP, loss/reorder/jitter. Gate:
as mesmas funções de USB operam via LAN usando o mesmo protocolo/estado.

Estado em 2026-08-14: controle TLS com pareamento manual por fingerprint, input UDP
autenticado, seletor USB/LAN e watchdog implementados e validados em build/testes.
O gate físico de paridade manual foi aprovado, incluindo overlay nos dois
monitores, conforme `docs/evidence/m6-lan-2026-08-14.md`. Discovery automático
e reconexão segura por pin do certificado também foram aprovados. M6 concluído
em 2026-08-14.

## M6.5 — USB versus LAN

Benchmark no mesmo hardware e relatório `docs/transport-performance.md` com
latência, jitter, perda, CPU e bateria medidos.

Smoke benchmark físico concluído em 2026-08-14: clocks monotônicos sincronizados,
RTT, latência captura→decode p50/p95/p99, integridade de sequência, CPU e memória
registrados para LAN e USB. Os dois transportes atenderam p95 < 15 ms e não
tiveram perdas observadas. O ensaio prolongado de consumo/bateria e a medição
pen-to-photon permanecem para o passe de performance; metodologia e limitações
estão em `docs/transport-performance.md`.

## M7 — Study Mode

Stylus como pen/highlighter, dedo como scroll, side button como eraser e gesto de
dois dedos configurável, nos dois transportes.

Concluído em 2026-08-14. O gesto de toque com exatamente dois dedos pode ficar
desligado, desfazer no overlay ou alternar caneta/marca-texto; a preferência fica
salva. Caneta + dedo não aciona o gesto. Validação automatizada e teste no tablet
físico aprovados; evidência em `docs/evidence/m7-study-mode-2026-08-14.md`.

## M8 — Performance pass

Otimização guiada por perfis em capture/encode/send/receive/decode/inject/overlay.

Concluído em 2026-08-14. O perfil separou prepare/encode/queue/send no Android e
decode/dispatch no host. Framing USB agrupado e dispatch Android sem buffering
reduziram captura→decode para p95 de 7,93 ms no USB e 6,98 ms na LAN nos trechos
contínuos medidos, sem perda. O modo Android compatível permanece alternável. Uma
regressão que encerrava silenciosamente o listener UDP foi encontrada no ensaio e
corrigida com recuperação do pipeline; watchdog, retomada e troca de monitor foram
aprovados fisicamente. Evidência em `docs/evidence/m8-performance-2026-08-14.md`.

## M9 — Packaging

Host Windows e APK distribuíveis, seletor USB/LAN, métricas, monitor selection e
documentação de instalação/troubleshooting.

Estado em 2026-08-14: interface Android tablet-first com controles nas bordas,
modos Caneta/Overlay/Marca separados, seleção de monitor, painéis flutuantes de
conexão/ajustes/diagnóstico e atalhos allowlisted foram implementados. O layout
nativo dos atalhos foi validado em USB e LAN sem encerrar a sessão. O
empacotamento produz host Windows x64 self-contained, APK assinado para sideload,
iniciador e guia offline de instalação/troubleshooting.

## MVP 0.1.0 concluído

O MVP foi aprovado no tablet e no Windows físicos em 2026-08-14. USB/ADB e LAN
autenticada operam com cursor, scroll, caneta Windows com pressão, overlay,
marca-texto, borracha, undo/redo/clear, múltiplos monitores e atalhos. Perda de
conexão neutraliza os estados de input. APK, host self-contained, documentação,
métricas e evidências estão disponíveis.

O desenvolvimento seguinte não deve reabrir o escopo do MVP. A versão 0.1.0 é a
linha de base funcional e deve permanecer reproduzível enquanto o produto avança.

# Roadmap pós-MVP

A ordem continua obedecendo à prioridade absoluta do Prompt Mestre:

1. estabilidade e menor latência;
2. experiência de uso diário;
3. novos transportes e plataformas;
4. vídeo/tela remota;
5. recursos avançados de anotação.

Mais features nunca justificam piorar latência, segurança ou recuperação após
falhas.

## Fase 1.5 — Estabilização da série 0.1.x

### S1 — Ensaios prolongados

- sessões contínuas de 1 h, 4 h e 8 h em USB e LAN;
- desconexão/reconexão repetida durante hover, contato e drag;
- consumo de CPU, memória, Wi-Fi e bateria;
- temperatura do tablet e impacto de tela ligada;
- latência pen-to-photon medida com câmera de alta velocidade;
- p50/p95/p99 e jitter separados por captura, transporte, decode e injection;
- confirmar ausência de mouse, tecla ou caneta presos após cada cenário.

### S2 — Automação, fuzz e robustez

- simulador de input capaz de emitir `DOWN`, `MOVE x1000`, `UP` sem tablet —
  **concluído em 14/08/2026**, integrado ao handshake e framing USB/TCP reais;
- testes de lifecycle completo de caneta, mouse e atalhos;
- frame truncado e versão inválida — **concluídos em 14/08/2026**, com rejeição
  isolada e recuperação confirmada;
- bind token, session, `sampleCount` e sequence inválidos — **concluídos em
  14/08/2026**;
- coordenadas e pressão v1 usam todo o domínio `ushort` (`0..65535`), portanto
  não possuem representação binária fora da faixa; extremos e round-trip foram
  validados em 14/08/2026, e presença continua governada pelos flags;
- NaN/infinity onde formatos futuros permitirem ponto flutuante;
- duplicação, reordenação, lacuna de sequência e encerramento sem `UP` —
  **concluídos em 14/08/2026**, com neutralização automática confirmada;
- reconnect storm com 25 sessões rápidas — **concluído em 14/08/2026**;
- watchdog USB/TCP de 750 ms durante contato — **concluído em 14/08/2026**, com
  `UP` forçado, supressão até amostra neutra e retomada posterior confirmados;
- perda física controlada em ensaio prolongado;
- garantir que input inválido seja rejeitado sem crashar ou derrubar outra sessão.

Gate parcial registrado em `docs/evidence/s2-input-simulator-2026-08-14.md`.

### S3 — Aplicativo Windows utilizável

Evoluir o host de console para uma aplicação Windows leve, sem Electron, contendo:

- status Connected/Disconnected — **concluído em 14/08/2026**;
- transporte e sessão ativos — **concluídos em 14/08/2026**; identidade amigável
  do tablet continua pendente de evolução do HELLO;
- IP/identidade pareada;
- latência e perda atuais;
- monitor, modo de input e contadores de frames/amostras — **concluídos em
  14/08/2026**;
- abrir configurações e diagnóstico;
- iniciar com o Windows;
- ocultar para bandeja e sair liberando todos os estados de input — **concluídos
  em 14/08/2026**;
- logs exportáveis para suporte.

Aplicativo `WinExe`, execução direta sem CMD, configuração automática do ADB e
instalador por usuário com atalhos e desinstalador — **concluídos em
14/08/2026**. A assinatura Authenticode permanece no escopo de S6.

Gate visual registrado em `docs/evidence/s3-windows-ui-2026-08-14.md` e gate do
instalador em `docs/evidence/s3-windows-installer-2026-08-14.md`.

### S4 — Calibração da área ativa

- tablet inteiro;
- área 16:9;
- área personalizada;
- preservar aspect ratio sem distorcer coordenadas;
- escolher entre letterbox, crop e stretch de forma explícita;
- calibração por monitor e orientação;
- perfis salvos para setups com múltiplas telas.

### S5 — Pressão e sensação da caneta

- abstração `PressureCurve` compartilhada pelo pipeline;
- presets Soft, Linear e Firm;
- curva personalizada e pressão mínima/máxima;
- dead zone opcional somente quando medida no hardware;
- smoothing configurável e desligado por padrão;
- comparação de jitter versus latência antes de ativar qualquer filtro;
- nunca injetar prediction destrutiva como input autoritativo no Windows.

### S6 — Distribuição e manutenção

- chave Android privada de release;
- assinatura do executável e instalador Windows;
- instalador/desinstalador preservando configurações de forma segura;
- versionamento semântico e changelog;
- migração versionada de preferências;
- atualização manual verificável inicialmente;
- rollback para a última versão estável;
- política clara de logs, certificados e dados locais.

Gate da Fase 1.5: concluir os ensaios prolongados, possuir reprodução automatizada
dos estados críticos e distribuir uma atualização sem depender do ambiente de
desenvolvimento.

## Fase 2 — Tela remota e tinta local

Esta fase corresponde à Phase 2 do Prompt Mestre e deve ser implementada em gates
independentes.

### P1 — Screen Capture

- capturar somente o monitor selecionado;
- suportar escala, rotação e DPI;
- excluir conteúdo protegido quando exigido pelo Windows;
- medir custo de captura antes de escolher a API definitiva;
- manter captura de vídeo separada do input autoritativo.

### P2 — Hardware Encode

- encoder de baixa latência por hardware;
- filas pequenas e descarte consciente de frames atrasados;
- bitrate, resolução e FPS adaptáveis;
- medir encode p50/p95/p99 e uso de GPU;
- fallback explícito, sem esconder software encode lento.

### P3 — Tablet Video Decoder

- decode por hardware no Android;
- renderização sincronizada e com fila mínima;
- adaptação a orientação e proporção da área ativa;
- métricas de receive, decode, render e dropped frames;
- vídeo nunca deve bloquear a thread de captura da stylus.

### P4 — WebRTC

- transporte apropriado para mídia, congestion control e mudança de rede;
- DTLS/SRTP e autenticação ligada à identidade DeskInk;
- canais de input e vídeo permanecem logicamente separados;
- LAN direta continua funcionando sem cloud, login ou servidor remoto;
- medir o custo do WebRTC contra o transporte LAN atual antes da adoção.

### P5 — Local Ink Prediction

- wet ink imediato no tablet;
- prediction somente visual;
- caneta autoritativa continua vindo das amostras reais;
- qualidade e ganho de latência medidos por brush e dispositivo;
- possibilidade de desligar prediction para diagnóstico.

### P6 — Reconciliation

- reconciliar wet ink local com o frame retornado pelo Windows;
- impedir traço duplicado, salto ou ghosting;
- corrigir prediction sem apagar conteúdo confirmado;
- definir identidade estável de stroke e timestamps comparáveis;
- sobreviver a frame perdido, reorder e reconexão no meio do traço.

### P7 — Native USB

O MVP já possui transporte cabeado via `adb reverse`. O item P7 passa a significar
USB nativo para usuário final:

- funcionar sem Developer Options e sem depuração USB;
- discovery e autorização próprios;
- framing DeskInk reutilizado sobre o novo transporte;
- reconnect e watchdog equivalentes a ADB/LAN;
- instalador de driver somente se APIs nativas realmente exigirem;
- benchmark de latência contra USB/ADB e LAN antes de substituir o padrão.

Gate da Fase 2: vídeo utilizável sem degradar o input, wet ink reconciliado sem
duplicação e USB nativo igual ou melhor que a linha de base medida.

## Trilha de plataforma — DeskInk para Linux

O suporte Linux reutilizará app Android, protocolo, autenticação, LAN e framing.
Será desenvolvido como host separado, sem contaminar o hot path do Windows.

### L1 — Foundation Linux

- build e testes em distribuição alvo documentada;
- receiver do protocolo e session/watchdog compartilháveis;
- discovery, pareamento e transporte LAN;
- simulador `DOWN/MOVE/UP` executando sem hardware.

### L2 — Input desktop

- mouse absoluto, clique, drag, scroll e atalhos;
- caneta/pressão quando a stack do compositor permitir;
- backend Wayland prioritário e backend X11 isolado;
- detecção explícita de permissões/portais, sem fallbacks silenciosos;
- neutralização de input em crash ou perda de conexão.

### L3 — Overlay Linux

- overlay por monitor;
- pen, marca-texto, borracha, undo/redo/clear;
- click-through quando suportado pelo compositor;
- compatibilidade testada separadamente em GNOME, KDE e compositores alvo;
- documentação clara das limitações de Wayland.

Gate Linux: paridade funcional básica com cursor/scroll/overlay do Windows em ao
menos um ambiente Wayland suportado, com testes automatizados e pacote instalável.

## Fase 3 — Ferramentas avançadas

### Sessões e desenho

- sessões de anotação salvas;
- múltiplos presets de brush;
- cores, espessuras e opacidade configuráveis;
- shapes e reconhecimento opcional de formas;
- seleção por lasso, mover, redimensionar e excluir;
- laser pointer sem persistência;
- histórico persistente com limites de memória definidos.

### Perfis e automação local

- perfis por aplicativo;
- troca automática de perfil;
- modos, atalhos, monitor, área ativa e pressão por perfil;
- import/export local de configurações;
- nenhuma automação pode executar shell arbitrário.

### Browser, documentos e estudo

- extensão de navegador opcional;
- PDF semantic anchoring;
- annotations ancoradas a página/conteúdo;
- clipboard integration com comandos allowlisted;
- screenshots intencionais e exportáveis;
- exportação de anotações para PDF;
- restauração de sessão sem depender de screenshot como modelo de desenho.

### Colaboração e acesso remoto — somente depois

Colaboração, internet remote access, contas e cloud não fazem parte das Fases 1.5
ou 2. Só podem entrar após threat model, criptografia ponta a ponta, política de
privacidade, custos operacionais e uma necessidade real de produto. Não criar
Firebase, microservices, Kubernetes ou backend web preventivamente.

## Restrições permanentes

- não usar Electron apenas por conveniência;
- não trocar o input engine por PWA, React Native ou Flutter no hot path;
- não serializar cada ponto como JSON;
- não usar polling ou salvar cada ponto em banco;
- não renderizar strokes como centenas de componentes UI;
- não desenhar globalmente por screenshots;
- não executar comandos shell arbitrários por atalhos;
- justificar dependências críticas por necessidade, manutenção, tamanho e latência;
- preferir APIs oficiais e versões estáveis compatíveis;
- atualizar arquitetura, protocolo e roadmap sempre que uma decisão mudar.

## Próxima decisão recomendada

Iniciar pela Fase 1.5, não por screen streaming. O primeiro pacote pós-MVP deve
combinar ensaio prolongado, simulador/fuzz, calibração de área ativa e uma UI
Windows simples. Depois disso, Native USB e a fundação Linux podem avançar em
paralelo por serem trilhas relativamente independentes. Screen capture/vídeo só
deve começar quando a linha de base de latência e estabilidade estiver protegida.
