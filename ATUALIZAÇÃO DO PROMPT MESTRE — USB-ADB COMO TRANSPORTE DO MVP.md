# ATUALIZAÇÃO DO PROMPT MESTRE — USB/ADB COMO TRANSPORTE DO MVP

## ALTERAÇÃO DE ARQUITETURA

O projeto deve suportar desde o MVP dois caminhos de comunicação:

```text
DeskInk Transport Layer

        IInputTransport
              │
      ┌───────┴────────┐
      │                │
      ▼                ▼
ADB USB Transport    LAN Transport
      │                │
      └───────┬────────┘
              ▼
       Windows Receiver
```

O **USB via ADB deve ser implementado primeiro**.

Motivos:

- ambiente mais controlado;
- excelente para desenvolvimento;
- menor interferência de rede;
- facilita diagnóstico;
- permite testar stylus → Windows isolando Wi-Fi;
- permite estabelecer baseline de latência;
- facilita instalação e debugging do APK;
- facilita coleta de logs;
- permite automatização pelo Codex.

O Wi-Fi/LAN continua obrigatório no MVP.

---

# USB DEBUG / ADB TRANSPORT

O primeiro transporte físico do MVP será:

```text
Android Tablet
      │
      │ USB
      ▼
ADB
      │
      ▼
Windows Host
```

Utilizar ADB port forwarding/reverse quando apropriado.

Arquitetura preferida:

```text
Android App
    │
    │ socket local
    ▼
127.0.0.1:<DESKINK_PORT>
    │
    ▼
ADB reverse
    │
    │ USB
    ▼
Windows Host
```

Exemplo conceitual:

```bash
adb reverse tcp:27183 tcp:27183
```

A porta real deve ser centralizada em configuração e não espalhada pelo código.

O Android poderá então se conectar conceitualmente a:

```text
127.0.0.1:27183
```

e o ADB encaminhará a comunicação para o Windows Host.

Antes de implementar, valide a estratégia correta usando a documentação oficial atual do Android/ADB.

---

# ADB NÃO FAZ PARTE DO PROTOCOLO

Muito importante:

O protocolo DeskInk não deve conhecer ADB.

Evite:

```text
StylusInputEngine
   ↓
AdbManager
```

Prefira:

```text
StylusInputEngine
        ↓
InputPacketEncoder
        ↓
IInputTransport
        ↓
┌───────────────────┐
│ AdbUsbTransport   │
│ LanTransport      │
└───────────────────┘
```

Assim:

- protocolo permanece independente;
- podemos comparar transportes;
- podemos futuramente adicionar USB nativo;
- testes podem usar transport fake/in-memory;
- Windows Receiver não precisa saber se o pacote veio por USB ou Wi-Fi.

---

# TRANSPORT ARCHITECTURE

Criar uma abstração pequena e objetiva.

Exemplo conceitual:

```text
IInputTransport

connect()
disconnect()
sendInput()
sendControl()
connectionState
metrics
```

Não crie uma abstração enorme.

Os requisitos comuns são:

- conectar;
- desconectar;
- enviar;
- detectar erro;
- coletar métricas;
- informar tipo de transporte.

Implementações iniciais:

```text
AdbUsbTransport
LanTransport
```

Futuro:

```text
NativeUsbTransport
WebRtcTransport
```

Não implementar os transportes futuros agora.

---

# CONTROL PLANE E INPUT DATA PLANE COM USB

A separação permanece:

```text
CONTROL PLANE
```

para:

- handshake;
- pairing;
- capabilities;
- seleção de monitor;
- configuração;
- versão;
- keepalive;
- comandos.

e:

```text
INPUT DATA PLANE
```

para:

- pen samples;
- touch;
- movement;
- pressure;
- tilt;
- buttons;
- scroll.

Porém o transporte físico poderá ser:

```text
USB/ADB
```

ou:

```text
LAN
```

O protocolo de nível superior deve ser o mesmo sempre que possível.

---

# USB/ADB COMO BASELINE

Depois de implementar ADB USB, utilize-o como referência para investigar performance.

Meça:

```text
Android MotionEvent
        ↓
historical samples
        ↓
packet encode
        ↓
socket
        ↓
ADB USB
        ↓
Windows receive
        ↓
packet decode
        ↓
Windows injection
```

Registrar:

```text
capture rate
packet rate
RTT
jitter
dropped samples
decode time
inject time
p50
p95
p99
```

Esses resultados servirão de baseline para comparação com Wi-Fi.

Não assumir que USB terá determinada latência.

Medir.

---

# COMPARAÇÃO AUTOMÁTICA DE TRANSPORTES

Quando LAN também estiver implementado, criar ferramenta que permita comparar:

```text
USB ADB

vs.

LAN Wi-Fi
```

Exemplo de relatório:

```text
Transport Benchmark

                 USB       LAN
RTT p50          X ms      X ms
RTT p95          X ms      X ms
RTT p99          X ms      X ms

Jitter p95       X ms      X ms

Packet loss      X%        X%

Input rate       X/s       X/s
```

Nunca preencher esses valores artificialmente.

---

# DETECÇÃO DO TABLET PELO CODEX

Durante desenvolvimento, se ADB estiver instalado:

executar:

```bash
adb devices
```

Se existir exatamente um dispositivo autorizado:

```text
device
```

o fluxo de desenvolvimento pode utilizá-lo automaticamente para operações não destrutivas.

Exemplos:

```bash
adb devices
adb reverse
adb install -r
adb shell
adb logcat
```

Se houver vários dispositivos, o código/scripts devem suportar seleção por serial.

Não assumir arbitrariamente qual dispositivo usar.

---

# DEVICE SERIAL

Scripts devem suportar:

```text
ANDROID_SERIAL
```

ou argumento explícito.

Exemplo conceitual:

```powershell
./scripts/install-android.ps1 -Device <serial>
```

Isso evita erros quando houver:

- tablet;
- celular;
- emulator;
- vários devices.

---

# AUTOMATIZAÇÃO ADB

Criar scripts simples.

Por exemplo:

```text
scripts/
    adb-check.ps1
    adb-connect.ps1
    adb-reverse.ps1
    install-android.ps1
    run-android.ps1
    android-logcat.ps1
```

Não duplicar comandos desnecessariamente.

Se possível, criar um comando agregado:

```powershell
./scripts/dev-usb.ps1
```

que faça:

```text
verificar ADB
      ↓
verificar device
      ↓
verificar autorização
      ↓
configurar adb reverse
      ↓
build APK
      ↓
install -r
      ↓
iniciar Windows Host
      ↓
iniciar Android App
```

Falhas devem ser reportadas claramente.

---

# USB CONNECTION STATUS

Android UI deve reconhecer o transporte.

Exemplo:

```text
DeskInk

Connected to
Teclas-PC

Transport:
USB Debug

Status:
● Connected

RTT:
3.4 ms
```

O RTT mostrado acima é apenas exemplo visual.

O app real deve mostrar somente valor medido.

---

# CONNECTION SELECTOR

MVP poderá oferecer:

```text
Connection

● Automatic

○ USB Debug
○ Local Network
```

Em `Automatic`:

preferência inicial:

```text
USB disponível
     ↓
usar USB

senão
     ↓
LAN
```

Esse comportamento deve ser configurável.

---

# USB HOTPLUG

Tratar desconexão física do cabo.

Se USB for removido:

```text
Connection Lost
```

Windows deve:

- gerar Pen UP se necessário;
- liberar mouse buttons;
- liberar keyboard modifiers;
- parar injection;
- limpar estados transitórios;
- preservar anotações;
- não crashar.

Android deve:

- sair do estado Connected;
- informar desconexão;
- permitir reconectar;
- opcionalmente oferecer LAN.

---

# TRANSPORT FAILOVER

Não implementar failover transparente complexo inicialmente.

Por exemplo:

```text
USB cai
↓
LAN assume automaticamente no meio de um stroke
```

pode introduzir estados difíceis.

MVP:

```text
USB cai
↓
input é liberado
↓
sessão fica disconnected
↓
usuário reconecta ou seleciona LAN
```

Depois podemos estudar handoff seguro.

---

# SEGURANÇA NO ADB

O fato de USB/ADB estar fisicamente conectado não elimina a necessidade de arquitetura segura.

Control Plane deve continuar tendo:

- session identifier;
- protocol version;
- handshake;
- validation.

Entretanto, durante o transporte via ADB local, não introduza criptografia complexa sem necessidade.

LAN deve receber tratamento de autenticação mais forte porque expõe comunicação na rede.

Compartilhe a camada de sessão sempre que fizer sentido.

---

# ADB MODE É UM DEVELOPMENT/POWER USER MODE

O produto final não deve depender obrigatoriamente de:

```text
Developer Options
USB Debugging
ADB
```

ADB é inicialmente:

```text
MVP transport
development transport
diagnostic transport
performance baseline
```

No futuro, investigar:

```text
NativeUsbTransport
```

que permita modo USB amigável ao usuário sem exigir depuração USB.

---

# FUTURO — NATIVE USB

Planejar, mas NÃO implementar no MVP.

Arquitetura futura:

```text
IInputTransport
│
├── AdbUsbTransport
├── LanTransport
└── NativeUsbTransport
```

Investigar futuramente mecanismos Android/USB apropriados.

Critérios:

- sem Developer Mode;
- sem ADB;
- fácil pareamento;
- baixa latência;
- boa compatibilidade;
- segurança;
- instalação simples.

---

# NOVA ORDEM DOS MILESTONES

A roadmap anterior deve ser modificada.

---

# M0 — FOUNDATION

Objetivo:

estrutura inicial.

Entregas:

```text
Android project
Windows project
protocol
docs
tests
scripts
```

Também detectar:

```text
ADB
Android SDK
JDK
.NET
Windows SDK
```

Gate:

```text
Android builds
Windows builds
basic tests pass
```

---

# M1 — ANDROID STYLUS TELEMETRY

Sem alteração conceitual.

Capturar:

```text
X/Y
pressure
tilt
orientation
hover
buttons
tool type
historical samples
timestamps
```

Validar em tablet físico.

---

# M2 — PROTOCOL CORE

Antes da rede real:

criar:

```text
packet format
encoder Android
decoder Windows
test vectors
state machine
sequence numbers
timestamps
validation
```

Criar transport de teste/in-memory.

Gate:

```text
Android-generated test vectors
        ↓
Windows decoder
        ↓
100% equivalent values
```

---

# M2.5 — USB ADB TRANSPORT

Essa milestone agora faz parte oficialmente do MVP.

Objetivo:

estabelecer comunicação física tablet → Windows através do cabo USB usando ADB.

Implementar:

- detecção ADB;
- device selection;
- ADB authorization detection;
- port reverse/forward;
- `AdbUsbTransport`;
- connection lifecycle;
- reconnect manual;
- diagnostics;
- benchmark;
- connection loss handling.

Fluxo esperado:

```text
Tablet
   ↓
StylusInputEngine
   ↓
PacketEncoder
   ↓
AdbUsbTransport
   ↓
USB cable
   ↓
Windows Receiver
```

Gate:

em tablet físico:

```text
stylus packets chegam continuamente ao Windows

pressure chega corretamente

tilt chega corretamente quando suportado

historical samples não são descartados

sequence funciona

sem memory leak evidente

desconectar cabo não deixa estado preso

reconectar funciona
```

Executar benchmark prolongado.

---

# M3 — MOUSE OVER USB

Primeiro controle real do Windows deverá utilizar USB/ADB.

Implementar:

```text
absolute cursor
click
drag
right click
scroll
monitor mapping
```

Gate:

controlar o Windows durante uma sessão real usando somente o tablet conectado via USB.

---

# M4 — TRUE PEN OVER USB

Implementar:

```text
PT_PEN
DOWN
UPDATE
UP
pressure
tilt
hover
buttons
watchdog
```

Primeiro validar tudo sobre USB.

Isso evita confundir problemas de:

```text
Windows Pen Injection
```

com:

```text
Wi-Fi jitter/loss
```

Gate:

input reconhecido corretamente em aplicativos Windows apropriados.

---

# M5 — GLOBAL OVERLAY

Implementar overlay enquanto USB permanece como transporte de referência.

Features:

```text
Pen
Highlighter
Eraser
Undo
Redo
Clear
```

---

# M6 — LAN TRANSPORT

Somente depois que o pipeline funcionar muito bem via USB/ADB.

Implementar:

`LanTransport`

Reutilizando:

```text
StylusInputEngine
PacketEncoder
Protocol
Windows Receiver
InputStateMachine
InputInjector
```

O comportamento do produto não deve mudar.

Apenas o transporte.

LAN deve incluir:

- discovery;
- pairing;
- autenticação;
- session secret;
- packet validation;
- loss metrics;
- jitter metrics.

Gate:

mesmas funcionalidades do USB funcionando via LAN.

---

# M6.5 — USB VS LAN PERFORMANCE

Executar comparação objetiva.

Testar os dois transportes no mesmo hardware.

Gerar:

```text
docs/transport-performance.md
```

Comparar:

- latency;
- jitter;
- packet loss;
- CPU;
- battery;
- responsiveness.

Não otimizar com base em sensação apenas.

---

# M7 — STUDY MODE

Agora combinar:

```text
finger → scroll

stylus → pen/highlighter

side button → eraser

2 finger gesture → configurable
```

Deve funcionar tanto em:

```text
USB
```

quanto:

```text
LAN
```

---

# M8 — PERFORMANCE PASS

Otimizar:

- capture;
- encode;
- transport;
- receive;
- decode;
- injection;
- overlay.

Usar USB como baseline.

---

# M9 — MVP PACKAGING

MVP deverá ser distribuível com:

```text
Windows Host

Android APK
```

Conexões:

```text
USB Debug
Local Network
```

Documentar claramente que USB Debug requer ADB/Developer Options nesta fase.

---

# NOVA DEFINIÇÃO DE MVP COMPLETO

MVP somente é considerado concluído quando:

- Android APK instala;
- Windows Host executa;
- tablet físico é reconhecido;
- stylus funciona;
- pressão funciona quando hardware oferece;
- tilt funciona quando hardware oferece;
- cursor funciona;
- scroll funciona;
- synthetic pen funciona;
- overlay funciona;
- highlighter funciona;
- eraser funciona;
- undo/redo funciona;
- shortcuts básicos funcionam;
- USB via ADB funciona;
- Wi-Fi/LAN funciona;
- usuário pode escolher transporte;
- métricas existem;
- desconectar USB não deixa input preso;
- perda da LAN não deixa input preso;
- monitor selection funciona;
- transportes compartilham o mesmo protocolo central.

---

# SCREEN STREAMING CONTINUA FORA DO MVP

Mesmo com USB disponível:

NÃO começar streaming antes do DeskInk input-only estar validado.

Sequência continua:

```text
Input
   ↓
Pen
   ↓
Overlay
   ↓
Study Mode
   ↓
USB/LAN estáveis
   ↓
Performance
   ↓
MVP
   ↓
Screen Streaming
```

Depois poderemos avaliar usar USB também para transportar vídeo.

---

# REGRA DE DESENVOLVIMENTO USB-FIRST

Durante implementação do pipeline crítico:

```text
Stylus
↓
Protocol
↓
Windows Input
```

a ordem deve ser:

```text
1. Validar localmente
2. Validar via USB/ADB
3. Medir
4. Só depois validar LAN
5. Comparar
6. Otimizar
```

Quando surgir um bug de input:

tente reproduzir via USB.

Se existir no USB:

provavelmente está no:

```text
capture
protocol
decode
state machine
injection
```

Se aparecer apenas na LAN:

investigar:

```text
packet loss
reorder
jitter
discovery
networking
```

Use essa separação para acelerar debugging.

---

# PRIMEIRO FLUXO REAL DO PRODUTO

Antes de considerar features avançadas, quero conseguir:

```text
1. conectar tablet via USB;

2. adb devices reconhecer tablet;

3. DeskInk configurar tunnel automaticamente;

4. abrir Android app;

5. Android detectar DeskInk Host;

6. tocar Connect;

7. mover stylus;

8. Windows receber coordenadas;

9. controlar cursor;

10. ativar Pen;

11. Windows receber pressure/tilt;

12. ativar Highlight;

13. grifar sobre qualquer aplicativo;

14. desconectar cabo;

15. nenhum botão/caneta ficar preso;

16. conectar novamente;

17. continuar usando.
```

Só depois dessa experiência estar estável, considerar o transporte USB/ADB concluído.

---

# REGRA FINAL

Considere USB/ADB uma peça central da estratégia de desenvolvimento.

Porém mantenha claramente a separação entre:

```text
PROTOCOL
```

```text
TRANSPORT
```

```text
INPUT ENGINE
```

```text
WINDOWS INJECTION
```

```text
OVERLAY
```

para que futuramente seja possível substituir:

```text
ADB USB
```

por:

```text
Native USB
```

sem reescrever o restante do DeskInk.

A prioridade do MVP passa a ser:

```text
Stylus Fidelity
      ↓
USB Baseline
      ↓
Windows Input
      ↓
Overlay
      ↓
LAN
      ↓
Study Mode
      ↓
Performance
```

Continue seguindo todos os demais requisitos do prompt mestre original.