# PROMPT MESTRE — Tablet Android como Mesa Digital de Baixíssima Latência para Windows

Você será o **arquiteto principal, engenheiro Android, engenheiro Windows, engenheiro de sistemas em tempo real e responsável por QA/performance** deste projeto.

Quero que você desenvolva um sistema funcional que transforme um **tablet Android com stylus em uma mesa digital para Windows**, com foco principal em estudo, escrita, marcação e controle do computador.

O produto não deve ser tratado como um simples mouse remoto.

O objetivo é chegar a uma experiência próxima de:

- mesa digital;
- Windows Ink;
- ferramenta de anotação global;
- controle remoto por stylus;
- painel de atalhos;
- futuramente display gráfico com espelhamento do Windows.

## REGRA MAIS IMPORTANTE

**NÃO comece criando dezenas de funcionalidades imediatamente.**

Primeiro:

1. analise o projeto e ambiente;
2. pesquise/valide as APIs atuais nas documentações oficiais;
3. crie a arquitetura;
4. documente decisões;
5. defina métricas;
6. defina gates do MVP;
7. somente depois comece a implementação.

O desenvolvimento deve ser **MVP-oriented, incremental, testável e medido**.

Não pule etapas.

Não considere uma etapa concluída porque o código compilou.

Cada milestone precisa ter critérios objetivos de aceitação.

---

# 1. VISÃO DO PRODUTO

O sistema terá dois componentes principais:

```text
Tablet Android
       │
       │ Stylus / Touch / Commands
       ▼
Low-Latency Transport
       │
       ▼
Windows Host
       │
       ├── Mouse
       ├── Scroll
       ├── Synthetic Pen
       ├── Pressure
       ├── Tilt
       ├── Overlay
       ├── Keyboard shortcuts
       └── futuramente Screen Streaming
```

Nome provisório interno:

`DeskInk`

O nome deve ficar facilmente substituível.

---

# 2. OBJETIVO PRINCIPAL

Permitir que o usuário utilize um tablet Android como dispositivo de entrada avançado para o Windows.

Casos de uso:

### Cursor

Usar stylus ou dedo como mouse.

### Mesa digital

Mapear a superfície do tablet para um monitor do Windows.

### Caneta

Usar stylus como caneta real no Windows.

### Pressão

Transmitir sensibilidade à pressão quando suportada pelo hardware.

### Inclinação

Transmitir tilt/orientation quando suportado.

### Scroll

Usar dedo ou stylus para rolar páginas.

### Marca-texto

Desenhar/grifar sobre qualquer aplicativo através de overlay global.

### Desenho

Desenhar sobre:

- navegador;
- PDFs;
- vídeos;
- PowerPoint;
- Word;
- VS Code;
- desktop;
- qualquer janela.

Sem tirar screenshot da tela para realizar as marcações.

### Borracha

Apagar strokes.

### Atalhos

Executar atalhos configuráveis pelo tablet.

### Estudo

Oferecer um modo de interação onde:

```text
dedo       = scroll
stylus     = grifar/escrever
botão pen  = borracha ou troca temporária
2 dedos    = zoom
```

Esses comportamentos devem ser configuráveis.

---

# 3. PRINCÍPIOS DE ENGENHARIA

Prioridade:

```text
1. Correção
2. Latência
3. Estabilidade
4. Fidelidade do stylus
5. UX
6. Funcionalidades extras
```

Não priorize aparência antes do pipeline de input funcionar corretamente.

Não introduza abstrações pesadas sem necessidade.

Não use arquitetura excessivamente genérica.

Não faça premature optimization, mas construa interfaces que permitam substituir componentes críticos depois.

---

# 4. PLATAFORMAS INICIAIS

## Android

Primeiro alvo:

- Android moderno;
- tablets;
- stylus ativa quando disponível;
- Kotlin;
- Android Studio;
- Gradle;
- APIs Android oficiais.

A interface pode utilizar Jetpack Compose.

Entretanto:

**não deixe a captura crítica da stylus presa à camada de UI.**

Crie um `StylusInputEngine` independente.

Para inking/renderização local, avalie e priorize as APIs atuais oficiais do Android, incluindo Jetpack Ink.

Para captura de dados de stylus, utilize `MotionEvent` e preserve informações relevantes.

---

# 5. DADOS DA STYLUS

O Android deve capturar, quando disponíveis:

```text
x
y

pressure

tilt
orientation

hover

distance/proximity se disponível

tool type

stylus buttons

event type

pointer id

event timestamp

historical samples
```

Antes de implementar, confirme nas APIs oficiais atuais quais campos e ranges realmente estão disponíveis.

Nunca invente dados que o hardware não forneça.

Crie capability detection.

Exemplo:

```text
StylusCapabilities

pressure: true
tilt: true
orientation: true
hover: true
sideButton: true
eraser: false
```

A aplicação deve continuar funcionando em hardware que não possua todos os recursos.

---

# 6. HISTORICAL MOTION SAMPLES

Isso é importante.

Não envie apenas o último ponto do `MotionEvent`.

Verifique `historySize` e os valores históricos disponíveis.

Construa um pipeline capaz de processar:

```text
MotionEvent
├── historical point 1
├── historical point 2
├── historical point 3
└── current point
```

Isso evita perder amostras geradas pela stylus entre frames.

Não aloque objetos desnecessariamente para cada ponto.

Evite garbage collection no hot path.

---

# 7. ANDROID — ARQUITETURA

Estrutura conceitual:

```text
Android App

App/UI
│
├── Connection UI
├── Toolbar
├── Shortcut Pad
├── Settings
└── Device Selector

Core
│
├── StylusInputEngine
├── TouchGestureEngine
├── CoordinateMapper
├── CapabilityDetector
├── InputPacketEncoder
├── ConnectionManager
└── MetricsCollector

Ink
│
├── LocalInkRenderer
├── Prediction
├── BrushEngine
└── StrokeModel
```

A UI não deve conter regras de protocolo.

O networking não deve conhecer componentes Compose.

O renderer não deve ser a fonte original dos eventos.

---

# 8. JETPACK INK

Avalie a versão estável atual da Jetpack Ink API.

Use-a onde ela realmente beneficiar:

- ink local;
- strokes;
- brushes;
- geometria;
- eraser;
- wet ink;
- low-latency rendering.

Não force a Jetpack Ink a controlar todo o protocolo.

O pipeline conceitual deve continuar sendo:

```text
MotionEvent
     │
     ├────► Local Ink Renderer
     │
     └────► StylusInputEngine
                   │
                   ▼
              Network
```

Isso é importante porque a renderização local e a transmissão são responsabilidades diferentes.

---

# 9. LOCAL INK

Quando futuramente o tablet exibir a tela do Windows, não devemos esperar um round-trip inteiro para mostrar o traço.

Arquitetar desde já:

```text
Stylus
   │
   ├──► tablet desenha wet ink imediatamente
   │
   └──► evento vai para Windows
                   │
                   ▼
             Windows processa
```

O local ink não precisa entrar no primeiro MVP visual completo, mas a arquitetura não deve impedir isso.

---

# 10. WINDOWS HOST

Primeiro target:

```text
Windows 11 x64
```

Utilize tecnologia nativa ou próxima ao sistema.

Sugestão inicial:

```text
C#
.NET atual estável
Win32 interop
```

Não introduza C++ imediatamente apenas por suposta performance.

Primeiro meça.

Mantenha o hot path desacoplado para que seja possível substituir somente componentes críticos por:

```text
C++ DLL
```

caso benchmarks demonstrem necessidade real.

---

# 11. WINDOWS INPUT ENGINE

Crie abstrações independentes:

```text
IInputInjector

├── MouseInputInjector
├── KeyboardInputInjector
└── PenInputInjector
```

Não misture overlay com input injection.

---

# 12. MOUSE

Mouse deve suportar:

- movimento absoluto;
- movimento relativo;
- clique esquerdo;
- clique direito;
- clique do meio;
- wheel vertical;
- wheel horizontal se aplicável;
- drag.

Use APIs Win32 apropriadas.

---

# 13. ABSOLUTE MODE

Mesa digital geralmente funciona melhor em mapeamento absoluto.

Exemplo:

```text
tablet x = 0.00 → monitor esquerda
tablet x = 1.00 → monitor direita

tablet y = 0.00 → monitor topo
tablet y = 1.00 → monitor baixo
```

Crie:

`AbsoluteCoordinateMapper`

Considere corretamente:

- monitor escolhido;
- virtual desktop;
- monitores à esquerda;
- monitores acima;
- DPI scaling;
- diferentes resoluções;
- diferentes aspect ratios;
- rotação;
- área ativa personalizada.

---

# 14. RELATIVE MODE

Também oferecer futuramente:

`RelativeTrackpadMode`

Semelhante ao touchpad de notebook.

Não misture matematicamente os dois modos.

---

# 15. PEN INPUT WINDOWS

Implemente um pipeline específico para pen.

Avalie/valide oficialmente:

```text
CreateSyntheticPointerDevice

PT_PEN

POINTER_TYPE_INFO

POINTER_PEN_INFO

InjectSyntheticPointerInput
```

Mapeie os dados Android para dados Windows corretamente.

Inclua:

- DOWN;
- UPDATE/MOVE;
- UP;
- hover quando aplicável;
- pressure;
- tilt X;
- tilt Y;
- buttons/flags quando suportados.

Não envie pressão falsa caso o dispositivo não suporte.

Faça normalização explicitamente em um único lugar.

Exemplo conceitual:

```text
Android pressure
      ↓
PressureNormalizer
      ↓
Windows pressure range
```

Crie testes para os extremos:

```text
0%
25%
50%
75%
100%
```

---

# 16. FAILSAFE DA CANETA

Nunca permita que a caneta fique travada em estado DOWN porque um pacote foi perdido.

Implemente watchdog.

Por exemplo:

```text
se estado atual = DOWN

e nenhum pacote válido chegar
dentro do timeout configurado

→ force synthetic UP
```

Não fixe valores sem benchmark.

Defina o valor por configuração interna.

---

# 17. NETWORKING

Não use JSON para os eventos de alta frequência da stylus.

Use protocolo binário.

Separe:

```text
CONTROL PLANE
```

de:

```text
INPUT DATA PLANE
```

---

# 18. CONTROL PLANE

O canal de controle pode ser confiável.

Responsabilidades:

- conexão;
- pairing;
- versão do protocolo;
- capabilities;
- seleção de monitor;
- configurações;
- modo atual;
- atalhos;
- ping;
- métricas;
- encerramento limpo.

Pode utilizar uma solução confiável simples.

Não otimize prematuramente esse canal.

---

# 19. INPUT DATA PLANE

Eventos de movimento da stylus são dados em tempo real.

Um evento MOVE antigo perde valor rapidamente.

Portanto não crie um protocolo onde movimentos antigos bloqueiem movimentos novos.

Primeiro MVP:

avalie uma implementação UDP binária na LAN.

Requisitos:

- sequence number;
- monotonic timestamp;
- session identifier;
- protocolo versionado;
- packet validation;
- latest-state semantics;
- perda de pacote tolerável;
- watchdog.

DOWN e UP são mais importantes que MOVE.

Planeje redundância/state snapshots para evitar estado preso.

---

# 20. SEGURANÇA DO INPUT

Nunca aceite input de qualquer dispositivo da rede local sem autenticação.

Um computador aceitando pacotes arbitrários que injetam teclado/mouse/caneta seria perigoso.

Implemente pairing.

Mínimo:

```text
Windows gera session secret aleatório
        │
        ▼
Tablet recebe durante pairing
        │
        ▼
Input packets são autenticados
```

Pode utilizar:

- session token criptograficamente forte;
- MAC/HMAC;
- outra abordagem segura apropriada.

Não invente criptografia própria.

Use primitivas padrão.

---

# 21. DISCOVERY

Desejo uma UX simples.

Ideal:

```text
Abrir Windows Host

PC aparece no tablet

Teclas-PC
192.168.x.x

[Conectar]
```

Avalie:

- mDNS;
- NSD;
- descoberta LAN adequada.

Também oferecer fallback:

```text
IP manual
```

e futuramente:

```text
QR pairing
```

---

# 22. PROTOCOLO BINÁRIO

Crie um documento formal:

`docs/protocol.md`

O protocolo deve ser explicitamente versionado.

Exemplo apenas conceitual:

```text
PacketHeader

magic
protocolVersion

sessionId

sequence

packetType

sampleCount

timestamp
```

Depois:

```text
PenSample

x
y

pressure

tiltX
tiltY

orientation

buttons

flags

timeDelta
```

Não copie cegamente essa estrutura.

Projete com alinhamento, tamanho e extensibilidade em mente.

---

# 23. BATCHING

Se um MotionEvent trouxer vários historical samples, não necessariamente envie um pacote UDP para cada ponto.

Avalie:

```text
1 network packet

contendo:

sample 1
sample 2
sample 3
sample atual
```

Isso reduz syscalls sem descartar informações.

Meça.

---

# 24. SEQUENCE NUMBERS

Todo pacote de input deve permitir detectar:

- perda;
- duplicação;
- reorder.

Não faça retransmissão automática de MOVE antigo.

Registre métricas.

---

# 25. TIMESTAMPS

Utilize clocks monotônicos.

Não use horário do sistema para medir latência.

Crie timestamps para etapas como:

```text
Android capture

Android encode

Android send

Windows receive

Windows decode

Windows inject

Windows render
```

Lembre-se:

os clocks dos dois dispositivos não são automaticamente sincronizados.

Não calcule ingenuamente:

```text
WindowsTime - AndroidTime
```

como one-way latency.

Implemente mecanismo de estimativa de clock offset/RTT se for necessário medir latência cross-device.

---

# 26. MÉTRICAS

Crie um painel de diagnóstico.

Exibir:

```text
Connection
Wi-Fi

Packet rate
xxx/s

Packet loss
x.xx%

Reordered
x

RTT
x.xx ms

Jitter
x.xx ms

Stylus samples/s
xxx

Injection failures
0
```

E percentis:

```text
p50
p95
p99
```

Não avaliar performance apenas pela média.

---

# 27. META DE LATÊNCIA

Trate como objetivos de engenharia, não promessa comercial.

Desejo buscar, em boas condições de LAN:

```text
captura stylus
→ recepção Windows
→ input injection
```

com latência extremamente baixa.

Meta inicial:

```text
p95 abaixo de aproximadamente 15 ms
```

antes do pipeline de vídeo.

O resultado real deve ser medido.

Não falsifique números.

Não coloque números hardcoded na UI apenas para parecer rápido.

---

# 28. OVERLAY WINDOWS

Depois que input estiver estável, implementar overlay global.

Overlay:

```text
topmost
borderless
transparent
monitor-sized
```

Ele deve poder alternar entre:

```text
INTERACTIVE
```

e:

```text
CLICK-THROUGH
```

Quando não estiver desenhando, não deve impedir interação com aplicativos abaixo.

---

# 29. OVERLAY RENDERER

Não renderize strokes usando controles comuns de UI individualmente.

Crie renderer apropriado para desenho contínuo.

Avalie APIs Windows adequadas como:

- Direct2D;
- DirectComposition;
- outras APIs nativas atuais justificadas.

Não introduza motor gráfico pesado.

---

# 30. FERRAMENTAS DE OVERLAY

MVP:

```text
Pen
Highlighter
Eraser

Undo
Redo
Clear
```

Depois:

```text
Line
Arrow
Rectangle
Ellipse
Laser pointer
```

---

# 31. HIGH­LIGHTER

Highlighter deve possuir:

- cor;
- opacity;
- thickness;
- pressure behavior configurável.

Por padrão, talvez seja melhor que pressão não altere exageradamente a espessura de um marca-texto.

Isso deve ser configuração de brush.

---

# 32. STROKE MODEL

Não salve simplesmente bitmap do overlay.

Mantenha modelo vetorial de strokes.

Exemplo conceitual:

```text
Stroke

id
tool

color
opacity
baseWidth

points[]
timestamp
```

Isso permite:

- undo;
- redo;
- eraser;
- save;
- restore;
- future transformations.

---

# 33. IMPORTANTE: OVERLAY NÃO É SCREENSHOT

O MVP de anotação deve funcionar diretamente sobre a tela em tempo real.

NÃO:

```text
captura screenshot
abre imagem
desenha sobre imagem
```

Isso não atende ao produto.

O overlay precisa permanecer separado do aplicativo abaixo.

---

# 34. LIMITAÇÃO CONHECIDA DO MVP

Uma anotação global baseada em overlay inicialmente pertence às coordenadas do monitor.

Exemplo:

```text
Chrome
parágrafo
████████ highlight
```

Se a página rolar, o overlay não sabe automaticamente que o highlight pertencia ao parágrafo.

Documente isso claramente.

Não tente resolver semantic anchoring no MVP.

---

# 35. CONTENT-AWARE ANNOTATIONS — PÓS-MVP

Futuramente poderemos criar:

```text
Browser Adapter
PDF Adapter
Document Adapter
```

para anexar annotations a conteúdo.

Possível arquitetura:

```text
Annotation
├── screen coordinates
├── document identifier
├── page/scroll state
└── content anchor
```

Mas isso é fase posterior.

---

# 36. MODOS DE INTERAÇÃO

Criar sistema central:

```text
InteractionMode

CURSOR
SCROLL
PEN
HIGHLIGHTER
ERASER
LASER
```

Um único `ModeController`.

Não espalhe booleanos como:

```text
isPen
isEraser
isScroll
```

pela aplicação.

---

# 37. STUDY MODE

Criar preset:

```text
Study Mode
```

Sugestão:

```text
Stylus              = highlighter
Stylus side button  = eraser
1 finger            = scroll
2 finger pinch      = zoom
2 finger tap        = undo
```

Tudo configurável.

---

# 38. TOUCH VS STYLUS

Distinguir claramente:

```text
TOOL_TYPE_STYLUS

TOOL_TYPE_ERASER

TOOL_TYPE_FINGER

TOOL_TYPE_MOUSE
```

quando disponíveis.

Não trate todos como touch genérico.

---

# 39. PALM REJECTION

Implementar de acordo com APIs atuais do Android.

Não simplesmente ignorar todo touch quando stylus estiver presente.

Criar:

`PalmRejectionPolicy`

Deve permitir futuramente gestos intencionais de dois dedos enquanto a stylus está sendo usada, se a plataforma/hardware permitir de forma confiável.

---

# 40. SHORTCUT ENGINE

O tablet terá botões configuráveis.

Exemplos:

```text
Undo
Redo

Ctrl+C
Ctrl+V

Ctrl+F

Page Up
Page Down

Play/Pause

Volume

← 10s
+ 10s
```

Arquitetura:

```text
ShortcutButton
      │
      ▼
ShortcutCommand
      │
      ▼
Windows ShortcutExecutor
```

---

# 41. SEGURANÇA DE ATALHOS

No MVP:

permitir combinações de teclas conhecidas.

Não crie botão que execute arbitrariamente:

```text
cmd.exe
PowerShell
shell scripts
```

Isso pode entrar futuramente com mecanismos explícitos de segurança.

---

# 42. PERFIS

Pós-MVP:

```text
Chrome
PDF Reader
PowerPoint
VS Code
Photoshop
OneNote
```

Cada perfil pode definir:

- toolbar;
- atalhos;
- modo inicial;
- brushes.

Não implementar auto-detection antes do MVP funcionar.

---

# 43. MULTI-MONITOR

Desde cedo, a arquitetura deve conhecer monitores.

Windows Host:

```text
DisplayManager

Display 1
1920x1080
Primary

Display 2
1920x1080
Left
```

Tablet permite selecionar:

```text
Controlar:

[ Monitor 1 ▼ ]
```

Mapeamento precisa respeitar coordenadas negativas do virtual desktop.

---

# 44. CALIBRAÇÃO

Adicionar futuramente:

```text
Calibration

┌───────────────────┐
│ •             •   │
│                   │
│                   │
│ •             •   │
└───────────────────┘
```

MVP pode começar com mapeamento normalizado correto.

---

# 45. SCREEN STREAMING NÃO FAZ PARTE DO CORE MVP

NÃO implemente streaming de tela antes de:

- cursor funcionar;
- stylus funcionar;
- pressure funcionar;
- tilt funcionar;
- networking estar estável;
- overlay funcionar;
- métricas existirem.

Só então começar Phase 2.

---

# 46. PHASE 2 — SCREEN STREAMING

Arquitetar futuramente:

```text
Windows
   │
GPU screen capture
   │
hardware encode
   │
real-time video
   │
Tablet
```

Avalie APIs atuais oficiais no momento da implementação.

Preferir inicialmente:

`Windows.Graphics.Capture`

Avaliar Desktop Duplication quando houver motivo técnico.

---

# 47. CODEC

Quando chegar streaming:

priorizar codec com hardware acceleration amplamente disponível.

Provável ponto inicial:

```text
H.264
```

Mas valide hardware/OS/client antes de fixar decisão.

Não implementar software encoding lento se hardware encoder estiver disponível.

---

# 48. WEBRTC

WebRTC NÃO precisa estar no MVP input-only.

Considere WebRTC quando entrarmos em:

- vídeo;
- encrypted real-time media;
- NAT traversal futuramente;
- DataChannel integrado com sessão de streaming.

Não introduzir WebRTC apenas para mandar X/Y no primeiro protótipo.

---

# 49. USB MODE

Pós-MVP.

Queremos posteriormente:

```text
Wi-Fi
USB
```

Arquitetar `Transport`:

```text
IInputTransport

├── LanTransport
└── UsbTransport
```

Mas não implemente abstrações inúteis antes de haver segunda implementação.

Interfaces devem existir apenas se ajudarem testes e isolamento.

---

# 50. REPOSITÓRIO

Use monorepo.

Estrutura sugerida:

```text
deskink/

apps/
    android/
    windows/

protocol/
    spec/
    test-vectors/

tests/
    integration/
    performance/

tools/
    latency/
    packet-inspector/

docs/
    architecture.md
    mvp-plan.md
    protocol.md
    latency.md
    security.md
    risk-register.md

README.md
```

Adapte se houver motivo técnico.

---

# 51. DOCUMENTAÇÃO ANTES DO CÓDIGO

Antes da implementação principal crie:

## `docs/architecture.md`

Contendo:

- componentes;
- responsabilidades;
- data flow;
- threads;
- networking;
- input pipeline;
- overlay pipeline.

## `docs/mvp-plan.md`

Milestones e gates.

## `docs/protocol.md`

Wire protocol.

## `docs/latency.md`

Como medir performance.

## `docs/security.md`

Threat model básico.

## `docs/risk-register.md`

Riscos conhecidos.

---

# 52. ADRs

Crie Architecture Decision Records somente para decisões realmente importantes.

Exemplos:

```text
ADR-001 Android native instead of PWA

ADR-002 Input transport

ADR-003 Windows pen injection

ADR-004 Overlay renderer
```

Não crie dezenas de ADRs irrelevantes.

---

# 53. THREADING

Hot path não pode disputar locks de UI.

Android:

```text
Input capture
    │
Input queue
    │
Encoder/network
```

Windows:

```text
Network receive
    │
Packet decode
    │
Input injection
```

Evite bloquear:

```text
UI thread
```

---

# 54. ALLOCATION POLICY

Em caminhos de alta frequência:

evitar:

- JSON;
- LINQ pesado;
- boxing;
- criação de objeto por sample;
- logs síncronos;
- locks grandes;
- string formatting.

Meça antes/depois.

---

# 55. LOGGING

Logging deve ter níveis.

```text
ERROR
WARN
INFO
DEBUG
TRACE
```

Nunca logar cada stylus point em produção por padrão.

Isso destruiria performance.

Permitir tracing temporário.

---

# 56. BENCHMARK MODE

Criar modo específico de diagnóstico:

```text
Benchmark Mode

Stylus samples: 241 Hz

Packets:
Sent 15,032
Received 15,028

Loss:
0.027%

RTT:
3.8 ms

p50:
...

p95:
...

p99:
...
```

Esses números devem vir de dados reais.

---

# 57. PROTOCOL TEST VECTORS

Criar packets conhecidos e seus bytes esperados.

Exemplo:

```text
input data
      ↓
encoder
      ↓
known bytes
      ↓
decoder
      ↓
same data
```

Isso reduz bugs Android↔Windows.

---

# 58. HARDWARE CAPABILITY SCREEN

No Android criar uma tela/debug screen:

```text
Stylus detected

Pressure:
YES

Tilt:
YES

Orientation:
YES

Hover:
YES

Historical samples:
YES

Buttons:
1
```

Mostrar também ranges reportados quando aplicável.

Essa tela será essencial durante desenvolvimento.

---

# 59. WINDOWS INPUT DEBUGGER

Criar ferramenta/debug panel:

```text
Last Pen Packet

X:
0.438

Y:
0.712

Pressure:
0.63

Tilt X:
-17

Tilt Y:
21

Sequence:
10482
```

E:

```text
Injection:
SUCCESS
```

ou erro real.

---

# 60. MVP ROADMAP

O projeto será desenvolvido exatamente por gates.

---

# M0 — FOUNDATION

Objetivo:

projeto estruturado e decisões registradas.

Entregas:

- repositório;
- Android project;
- Windows project;
- docs;
- CI/build básico;
- protocolo inicial;
- scripts.

Gate:

```text
Android builds
Windows builds
tests básicos passam
arquitetura documentada
```

Não implementar features grandes aqui.

---

# M1 — ANDROID STYLUS TELEMETRY

Objetivo:

provar que conseguimos capturar corretamente a stylus.

Implementar:

- MotionEvent;
- X/Y;
- pressure;
- tilt;
- orientation;
- hover;
- buttons;
- tool type;
- historical samples;
- capabilities.

Tela de debug deve mostrar valores ao vivo.

Gate:

com tablet físico:

```text
stylus detectada
x/y variam corretamente
pressure responde
tilt responde se hardware suportar
hover responde se suportado
histórico não é descartado
```

Salvar evidências/logs dos testes.

---

# M2 — TABLET ↔ WINDOWS NETWORK

Objetivo:

input chegar ao computador.

Implementar:

- discovery simples;
- pairing;
- control channel;
- binary input packet;
- UDP input path;
- sequences;
- timestamps;
- authentication;
- metrics.

Ainda NÃO controlar mouse.

Windows somente recebe e exibe.

Gate:

```text
100k+ samples synthetic test
sem crash
sem memory growth anormal
packet loss medido
reorder medido
RTT medido
```

---

# M3 — MOUSE MODE

Objetivo:

tablet controlar Windows.

Implementar:

- absolute mode;
- relative mode básico se necessário;
- click;
- drag;
- right click;
- scroll;
- monitor selection.

Gate:

usuário consegue:

```text
abrir navegador
mover cursor
clicar
arrastar
rolar página
```

sem comportamento preso.

---

# M4 — TRUE PEN MODE

Objetivo:

Windows receber uma synthetic pen.

Implementar:

- synthetic pen device;
- DOWN;
- MOVE;
- UP;
- pressure;
- tilt;
- hover se suportável;
- buttons apropriados;
- failsafe.

Criar tester.

Gate:

validar em ao menos aplicações diferentes que reconheçam input de caneta.

Pressure deve variar realmente.

Não declarar sucesso apenas porque `InjectSyntheticPointerInput` retornou sucesso.

---

# M5 — GLOBAL OVERLAY

Objetivo:

desenhar/grifar sobre qualquer tela.

Implementar:

```text
Pen
Highlighter
Eraser
Undo
Redo
Clear
```

Overlay por monitor.

Gate:

testar sobre:

```text
Desktop
Browser
PDF/application
Video
```

Overlay deve permanecer fluido.

Aplicativo abaixo deve continuar interagível quando overlay estiver click-through.

---

# M6 — STUDY MODE

Objetivo:

criar primeira experiência realmente útil.

Implementar:

```text
Finger → scroll

Stylus → highlight/pen

Side button → eraser ou modo configurado

Toolbar

Undo
Redo
Clear
```

Atalhos configuráveis básicos.

Gate:

realizar sessão real de estudo/navegação por alguns minutos sem precisar tocar no mouse físico.

---

# M7 — PERFORMANCE PASS

Antes de adicionar novas funcionalidades:

medir tudo.

Investigar:

- allocations;
- CPU Android;
- CPU Windows;
- GPU;
- packet rates;
- network jitter;
- frame pacing;
- input injection;
- overlay renderer.

Somente substituir componentes por código nativo se benchmark justificar.

Gate:

gerar:

`docs/performance-report.md`

com resultados reais.

---

# M8 — MVP PACKAGING

Entregáveis:

```text
Android APK

Windows executable

README

pairing instructions

troubleshooting
```

Criar experiência simples:

```text
1. Abrir DeskInk Host no Windows
2. Abrir DeskInk no tablet
3. Selecionar PC
4. Conectar
5. Selecionar monitor
6. Usar
```

---

# 61. DEFINIÇÃO DE MVP COMPLETO

MVP será considerado pronto somente quando:

- tablet conecta ao Windows;
- conexão é autenticada;
- stylus é reconhecida;
- cursor funciona;
- scroll funciona;
- pen Windows funciona;
- pressure funciona quando disponível;
- tilt é transmitido quando disponível;
- overlay funciona;
- highlighter funciona;
- eraser funciona;
- undo/redo funciona;
- atalhos básicos funcionam;
- monitor pode ser selecionado;
- falha de rede não deixa mouse/pen preso;
- métricas existem;
- APK e Windows Host podem ser instalados.

Não é necessário no MVP:

- screen streaming;
- USB;
- cloud;
- account system;
- browser extension;
- PDF semantic anchors;
- collaboration;
- internet remote access.

---

# 62. POST-MVP — PHASE 2

Depois do MVP validado:

## P1 — Screen Capture

Capturar monitor.

## P2 — Hardware Encode

Encoder de baixa latência.

## P3 — Tablet Video Decoder

Renderização de vídeo.

## P4 — WebRTC

Session transport apropriado para vídeo.

## P5 — Local Ink Prediction

Traço local imediato no tablet.

## P6 — Reconciliation

Evitar duplicação entre wet ink local e frame retornado.

## P7 — USB Transport

Opção cabeada.

---

# 63. POST-MVP — PHASE 3

Features avançadas:

- saved annotation sessions;
- multiple brush presets;
- profiles por aplicativo;
- auto-profile switching;
- browser extension;
- PDF anchoring;
- shapes;
- lasso;
- laser pointer;
- clipboard integration;
- screenshots intencionais/export;
- PDF annotation export.

---

# 64. NÃO FAZER

Não faça:

- Electron no Windows apenas por conveniência;
- PWA para stylus core;
- React Native como input engine principal;
- Flutter como hot path sem necessidade;
- servidor cloud;
- Firebase;
- login;
- database remoto;
- microservices;
- Kubernetes;
- backend web;
- screenshot-based drawing;
- JSON para cada pen point;
- WebSocket para tudo sem análise;
- polling;
- salvar cada ponto no banco;
- renderizar stroke como centenas de componentes UI;
- executar comandos shell arbitrários por shortcut.

Não adicione dependências porque são populares.

Justifique cada dependência crítica.

---

# 65. DEPENDÊNCIAS

Antes de adicionar biblioteca:

responda internamente:

```text
Por que precisamos?

API nativa resolve?

Impacto na latência?

Impacto no tamanho?

É mantida?

É necessária no MVP?
```

Prefira APIs oficiais.

---

# 66. VERSIONAMENTO

No início:

detecte as versões atuais instaladas de:

- JDK;
- Kotlin;
- Gradle;
- Android Gradle Plugin;
- Android SDK;
- .NET SDK;
- Windows SDK.

Depois compare com versões estáveis atuais oficiais.

Não faça upgrade cego.

Escolha versões estáveis compatíveis.

Documente em:

`docs/toolchain.md`

---

# 67. CODEX — MODO DE TRABALHO

Você é um agente autônomo, mas deve trabalhar de maneira disciplinada.

Antes de cada milestone:

1. leia arquitetura;
2. leia milestone;
3. liste critérios de aceitação;
4. implemente;
5. compile;
6. teste;
7. investigue erros;
8. corrija;
9. execute novamente;
10. atualize documentação.

Nunca marque tarefa como concluída sem teste.

---

# 68. SUBAGENTS

Use subagentes quando melhorarem claramente o trabalho.

Exemplos:

```text
agent Android API research

agent Windows API research

agent protocol review

agent performance review
```

Não deixe vários agentes alterarem simultaneamente os mesmos arquivos centrais.

Agentes de pesquisa devem retornar conclusões ao agente principal.

O agente principal mantém a arquitetura coerente.

---

# 69. WORKTREES

Worktrees podem ser usados para tarefas realmente independentes.

Exemplo:

```text
Android capability debugger

Windows protocol decoder
```

Não crie worktree para cada pequena tarefa.

Antes de merge:

- review;
- tests;
- build;
- conflict check.

---

# 70. SOURCE OF TRUTH

Arquitetura:

`docs/architecture.md`

Wire protocol:

`docs/protocol.md`

Roadmap:

`docs/mvp-plan.md`

Não permitir que código e documentação contradigam esses arquivos silenciosamente.

Se uma decisão mudar:

atualize documentação.

---

# 71. TESTING

## Android

- unit tests;
- packet encoding;
- coordinate mapping;
- pressure mapping;
- capability fallbacks.

## Windows

- packet decoding;
- coordinate mapping;
- state machine;
- pen lifecycle;
- watchdog;
- shortcuts.

## Integration

Criar simulador capaz de gerar:

```text
DOWN
MOVE x1000
UP
```

sem tablet.

Isso permite testar Windows Host automaticamente.

---

# 72. FUZZ / MALFORMED PACKETS

Windows Host nunca deve confiar no pacote recebido.

Teste:

- pacote pequeno;
- pacote grande;
- version inválida;
- sampleCount inválido;
- NaN;
- infinity;
- coordinates fora do range;
- pressure inválida;
- sequence absurda;
- session errada.

Nunca crashar.

---

# 73. STATE MACHINE

Pen deve possuir state machine explícita.

Por exemplo:

```text
IDLE
HOVER
CONTACT
```

Transições inválidas devem ser tratadas.

Nunca deixar booleanos independentes criarem estados impossíveis.

---

# 74. CONNECTION LOSS

Se conexão cair:

Windows deve imediatamente:

- liberar pen;
- liberar mouse buttons;
- limpar estados pressionados;
- interromper injection;
- manter overlay salvo;
- mostrar disconnected.

Tablet:

- mostrar disconnected;
- tentar reconnect de forma controlada;
- nunca perder UI inteira.

---

# 75. APP WINDOWS

No MVP, Windows Host deve possuir pelo menos:

```text
Status

Tablet:
Connected/Disconnected

IP

Latency

Selected monitor

Input mode

Start with Windows [later]

Open Settings

Exit
```

Pode começar simples.

Não gastar tempo excessivo com design.

---

# 76. APP ANDROID

MVP UI:

```text
Connection screen

Main control screen

Toolbar

Shortcut buttons

Settings

Diagnostics
```

Main control:

```text
┌─────────────────────────────────────────┐
│ DeskInk       Teclas-PC       ● 4 ms   │
├─────────────────────────────────────────┤
│                                         │
│                                         │
│             ACTIVE AREA                 │
│                                         │
│                                         │
├─────────────────────────────────────────┤
│ Cursor Scroll Pen Highlight Erase       │
├─────────────────────────────────────────┤
│ Undo   Redo   Clear   Shortcut  Settings│
└─────────────────────────────────────────┘
```

Visual simples, moderno e tablet-first.

Não sacrificar área ativa da stylus.

---

# 77. ACTIVE AREA

Permitir futuramente:

```text
Full tablet

16:9 area

Custom area
```

Para evitar distorção tablet→monitor.

MVP deve ao menos corrigir aspect ratio ou documentar claramente o comportamento escolhido.

---

# 78. CONFIGURAÇÃO DE PRESSÃO

Criar arquitetura para pressure curve.

Exemplo futuro:

```text
Soft

Linear

Firm
```

Internamente:

`PressureCurve`

MVP pode usar linear.

Mas não espalhar fórmula diretamente pelo código.

---

# 79. SMOOTHING

Não aplicar smoothing exagerado.

Smoothing adiciona latência.

Comece com input praticamente cru.

Depois teste filtros.

Se adicionar:

- tornar configurável;
- medir impacto;
- nunca sacrificar responsividade para esconder jitter.

---

# 80. PREDICTION

Prediction deve atuar principalmente na visualização local.

Nunca injete movimentos previstos destrutivamente no Windows sem estratégia de correção.

Predição visual e input autoritativo são conceitos diferentes.

---

# 81. MONITORAMENTO DE PERFORMANCE

Crie markers/profiling em:

```text
ANDROID_CAPTURE

ANDROID_ENCODE

ANDROID_SEND

WINDOWS_RECEIVE

WINDOWS_DECODE

WINDOWS_INJECT

OVERLAY_DRAW
```

Precisamos saber exatamente onde está o atraso.

---

# 82. README

README final deve possuir:

- objetivo;
- screenshots futuramente;
- requisitos;
- build Android;
- build Windows;
- pairing;
- running;
- troubleshooting;
- arquitetura resumida;
- limitações atuais.

---

# 83. DESENVOLVIMENTO NO HARDWARE REAL

Emulador Android pode ajudar em UI.

Porém stylus pressure/tilt/hover precisam ser validados em dispositivo físico.

Prepare o projeto para uso com:

```text
adb devices

adb install

adb logcat
```

Não declare hardware support com base apenas em emulator.

---

# 84. AUTOMATIZAÇÃO LOCAL

Crie scripts simples quando úteis:

Windows:

```text
scripts/build-windows.ps1
scripts/run-windows.ps1
```

Android:

```text
scripts/build-android.ps1
scripts/install-android.ps1
```

E:

```text
scripts/test-all.ps1
```

Não criar sistema de scripts excessivamente complexo.

---

# 85. PRIMEIRO PASSO AGORA

Ao receber este prompt:

**NÃO implemente imediatamente o MVP inteiro.**

Faça primeiro:

### Step 1

Inspecione:

- diretório;
- Git;
- ferramentas instaladas;
- Android SDK;
- JDK;
- .NET;
- Windows SDK.

### Step 2

Pesquise documentação oficial atual relevante.

Priorize:

- Android Developers;
- Microsoft Learn;
- OpenAI/Codex docs quando necessário.

### Step 3

Crie ou refine:

```text
docs/architecture.md
docs/mvp-plan.md
docs/protocol.md
docs/latency.md
docs/security.md
docs/risk-register.md
docs/toolchain.md
```

### Step 4

Apresente um resumo conciso da arquitetura escolhida.

### Step 5

Implemente M0.

### Step 6

Compile tudo.

### Step 7

Implemente M1.

### Step 8

Compile/teste no máximo possível automaticamente.

### Step 9

Se houver um tablet Android conectado por ADB:

detecte-o e utilize-o para testes não destrutivos.

### Step 10

Continue gate por gate.

---

# 86. AUTONOMIA

Não pare para me perguntar coisas pequenas.

Quando houver diversas soluções razoáveis:

- pesquise;
- compare;
- escolha;
- documente.

Pergunte somente quando existir decisão de produto que realmente não possa ser inferida com segurança.

Se um teste falhar:

não siga para a próxima milestone.

Investigue e corrija.

---

# 87. QUALIDADE

Não produza código fake.

Não coloque:

```text
TODO implementar depois
```

em funcionalidades consideradas concluídas.

Não simule sucesso.

Não crie mocks no caminho de produção para fingir que pressure ou tilt funciona.

Mocks podem existir em testes.

---

# 88. CRITÉRIO FINAL

Quero um sistema real.

Não quero uma demonstração visual.

O sucesso será quando eu puder:

```text
pegar o tablet

abrir DeskInk

conectar ao Windows

usar a stylus

mover o cursor

rolar uma página

escrever com pressão

grifar conteúdo

apagar

desfazer

usar atalhos

e continuar estudando
```

sem precisar recorrer constantemente ao mouse.

---

# 89. PRIORIDADE ABSOLUTA

Se precisar escolher entre:

```text
mais features
```

ou:

```text
menor latência + maior estabilidade
```

escolha:

**menor latência + maior estabilidade.**

Construa primeiro a melhor fundação possível para o MVP.

Comece agora pela análise da arquitetura e do ambiente, depois avance para M0 e M1 seguindo os gates descritos.