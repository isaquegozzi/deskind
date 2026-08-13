# Arquitetura do DeskInk

Status: fonte de verdade para M0/M1, revisada em 2026-08-13.

## Decisões de alto nível

- Monorepo com Android nativo em Kotlin e host Windows em C#/.NET 10 LTS.
- Captura da stylus baseada em `MotionEvent`; a UI apenas entrega eventos e
  apresenta snapshots de diagnóstico.
- Protocolo, transporte, estado de input, injeção e overlay são camadas distintas.
- USB/ADB é o primeiro transporte e baseline; LAN continua obrigatório no MVP.
- Nenhum dado ausente do hardware será sintetizado como se fosse capacidade real.

Referências oficiais principais: [Android stylus input](https://developer.android.com/develop/ui/compose/touch-input/stylus-input/advanced-stylus-features),
[AndroidX Ink releases](https://developer.android.com/jetpack/androidx/releases/ink),
[ADB](https://developer.android.com/tools/adb) e
[Windows synthetic pointer input](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-injectsyntheticpointerinput).

## Componentes

```text
Android
  StylusCaptureView
      -> StylusInputEngine
          -> bounded sample batches
              +-> TelemetryPublisher -> debug UI (taxa limitada)
              +-> InputPacketEncoder -> IInputTransport
                                          |-- AdbUsbTransport
                                          `-- LanTransport (M6)

Windows
  Transport listeners
      -> frame/datagram validation
      -> protocol decoder
      -> InputStateMachine + watchdog
          |-- input debugger/metrics
          `-- IInputInjector
                 |-- mouse
                 |-- keyboard
                 `-- synthetic pen

Overlay (M5)
  StrokeModel -> Direct2D/DirectComposition renderer
```

### Android

`StylusCaptureView` recebe o `MotionEvent` no thread de UI e o encaminha sem
reter a instância. `StylusInputEngine` copia primeiro todos os pontos históricos,
na ordem, e por último o ponto atual para buffers primitivos reutilizáveis. Cada
amostra preserva pointer id, action, tool type, timestamp monotônico, X/Y,
pressure, tilt, orientation, distance, hover e buttons quando disponíveis.

`CapabilityDetector` combina `InputDevice.MotionRange` com capacidades observadas
e registra a origem da conclusão. Ausência de range não vira `true`; valor zero
isolado também não prova ausência. `ACTION_CANCEL` é terminal para o stroke.

A UI lê snapshots imutáveis em frequência limitada. Ela não codifica pacotes,
abre sockets nem é fonte do evento original. Jetpack Ink estável 1.0.0 fica
reservado ao renderer local; não integra o hot path de transporte em M1.

### Transporte

`IInputTransport` é pequeno: conectar, desconectar, enviar control, tentar enviar
um batch de input, expor estado/tipo/métricas. Ele não conhece `MotionEvent` nem
injeção Windows.

ADB configura dois reverses TCP centralizados:

```text
tcp:27183 -> Windows control listener tcp:27183
tcp:27184 -> Windows input listener   tcp:27184
```

O app conecta a `127.0.0.1`. A separação de conexões evita que mensagens de
controle compartilhem fila com amostras. TCP/ADB é confiável, mas pode acumular
dados antigos; por isso a fila de MOVE é limitada, `TCP_NODELAY` é usado e batches
obsoletos podem ser substituídos pelo estado mais recente sem descartar transições
DOWN/UP/CANCEL. LAN usa control TCP e input UDP autenticado em M6.

Não existe failover no meio de stroke no MVP. Desconexão encerra a sessão e força
liberação segura do estado.

### Windows

Recepção, decode e injeção não rodam no thread da UI. O decoder só produz dados
validados. A máquina de estado rejeita sessões/sequências inválidas e garante que
disconnect, timeout ou erro fatal provoquem pen UP, mouse buttons UP e liberação
de modificadores.

M4 usará `CreateSyntheticPointerDevice(PT_PEN, 1, ...)` e
`InjectSyntheticPointerInput`; para PT_PEN cada chamada injeta um contato. O
conversor Android->Windows é o único lugar que transforma pressure e os ângulos
em ranges/flags Win32. M3 usa `SendInput` para mouse/teclado. Coordenadas absolutas
são mapeadas contra o monitor selecionado e o virtual desktop, inclusive origens
negativas.

### Overlay

M5 mantém strokes vetoriais separados da injeção. Uma janela topmost por monitor
alterna explicitamente entre interactive e click-through. O renderer planejado é
Direct2D/DirectComposition, sujeito a protótipo e benchmark antes do gate M5.
Anotações pertencem às coordenadas do monitor; ancoragem semântica não é MVP.

## Threading e backpressure

```text
Android UI/capture -> SPSC bounded queue -> encoder/socket worker
Windows socket IO  -> bounded decode queue -> injection worker
```

- O hot path não espera a UI nem faz logging textual por amostra.
- Filas têm limites e contadores de drop/coalescing.
- Transições nunca são deliberadamente coalescidas; MOVE/HOVER antigos podem ser.
- Métricas usam relógios monotônicos e agregação fora do hot path.

## Fluxo de conexão USB

1. Script valida ADB e escolhe exatamente um serial ou exige `-Device`.
2. Script configura os dois `adb reverse`.
3. Windows abre listeners; Android realiza handshake de versão/sessão.
4. Capabilities e configuração trafegam no control plane.
5. Batches de input trafegam na conexão dedicada.
6. Cabo removido fecha sockets; ambos os lados entram em disconnected e o host
   libera qualquer input transitório.

## Evolução permitida

`NativeUsbTransport` e streaming podem ser adicionados sem mudar captura,
protocolo lógico, estado ou injetores. Eles não serão implementados no MVP atual.
