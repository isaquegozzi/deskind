# Protocolo DeskInk

Status: design inicial de M0; o wire format será congelado e receberá vetores em
M2. ADB não aparece em nenhum campo do protocolo.

## Convenções

- Binário, little-endian, tamanho explícito e versão `major.minor`.
- Inteiros sem sinal não podem ser negativos após decode.
- Timestamps são microssegundos de relógio monotônico do emissor.
- `sessionId` é aleatório por handshake; `sequence` cresce por data plane.
- X/Y viajam como pixels locais `float32` e largura/altura da área acompanham a
  configuração da sessão. Normalização/mapeamento ocorre explicitamente.
- Valores opcionais têm flags de presença; zero não significa “não suportado”.

## Planos

Control plane usa mensagens length-prefixed confiáveis: hello/version negotiation,
session, capabilities, display/config, ping/pong, mode, shortcut e shutdown.

Input data plane usa `InputBatch`. No ADB ele é framed sobre TCP dedicado; em LAN
é um datagrama UDP autenticado. O payload lógico é idêntico. Uma implementação
deve rejeitar payload excedente, truncado, count incoerente, versão major
incompatível, sessão errada e flags reservadas.

## Header v1 proposto (48 bytes)

| Offset | Tipo | Campo |
| ---: | --- | --- |
| 0 | u32 | magic `0x4B4E4944` (`DINK` em bytes) |
| 4 | u16 | major |
| 6 | u16 | minor |
| 8 | u8 | packetType |
| 9 | u8 | flags |
| 10 | u16 | headerSize (=48 em v1) |
| 12 | u32 | payloadLength |
| 16 | u64 | sessionId |
| 24 | u64 | sequence |
| 32 | u64 | baseTimestampUs |
| 40 | u16 | elementCount |
| 42 | u16 | reserved (=0) |
| 44 | u32 | CRC32C de header sem CRC + payload |

CRC32C detecta corrupção/bugs, mas não autentica. LAN acrescentará tag de
autenticação definida no envelope do transporte; não haverá “criptografia caseira”.

Limites iniciais: payload <= 64 KiB, samples por batch <= 256 e frames de control
<= 1 MiB. Limites são validados antes de alocar e serão afinados por benchmark.

## PenSample v1 proposto (40 bytes)

| Campo | Tipo | Semântica |
| --- | --- | --- |
| deltaUs | u32 | diferença para `baseTimestampUs` |
| x, y | 2 x f32 | coordenadas locais |
| pressure | f32 | valor Android bruto válido pela flag |
| tiltRadians | f32 | 0 perpendicular; válido pela flag |
| orientationRadians | f32 | orientação Android; válido pela flag |
| distance | f32 | eixo Android bruto; válido pela flag |
| pointerId | i32 | id no motion set |
| buttonState | u32 | máscara Android preservada |
| action | u8 | down/move/up/hover/cancel |
| toolType | u8 | finger/stylus/eraser/mouse/unknown |
| sampleFlags | u16 | present/current/historical/in-contact |

Na captura, historical samples usam a action semântica MOVE/HOVER e a flag
`historical`; a transição atual mantém a action real. Transformação de tilt
magnitude/orientation em tiltX/tiltY do Windows pertence ao adapter Win32.

## Estado e sequência

DOWN abre um pointer; MOVE/UP precisam referenciar pointer aberto. CANCEL fecha o
pointer sem produzir novo stroke. Duplicatas são ignoradas; regressão/reorder é
contada e só aceita de acordo com a política do transporte. Snapshot periódico
de estado e watchdog impedem input preso. Não se retransmite MOVE antigo em LAN.

## Compatibilidade

- Major diferente: handshake falha.
- Minor mais novo: aceito apenas quando tamanhos/flags desconhecidas podem ser
  ignorados com segurança.
- Campos novos entram por novo packet type ou extensão length-delimited.
- Reserved não zero é rejeitado em v1.

## Vetores obrigatórios de M2

Empty/hello, DOWN mínimo, MOVE com todos os campos, batch com 3 historical +
current, UP, CANCEL, pressure 0/0.25/0.5/0.75/1, limites de tilt/orientation,
bad magic, truncado, length/count inválido, NaN/Infinity e CRC incorreto.
