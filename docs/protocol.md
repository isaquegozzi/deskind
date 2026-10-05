# Protocolo DeskInk v1

Status: wire format congelado para M2 em 2026-08-13. ADB não aparece em nenhum
campo. Todos os inteiros são little-endian e nenhuma struct nativa é serializada.

## Canais e envelopes

- Control: confiável/ordenado, frames binários com prefixo `u32 frameLength`.
- Input ADB: TCP dedicado, mesmo prefixo e frames `INPUT_BATCH`.
- Input LAN (M6): um frame por datagrama UDP autenticado, sem prefixo TCP.
- O frame lógico, decoder e máquina de estado são compartilhados; garantias de
  TCP e UDP não são fingidas como equivalentes.

TCP é stream: cada leitura pode conter parte de um frame ou vários frames. Um
length divergente, acima do limite ou inconsistente encerra a sessão; o receiver
não procura magic arbitrariamente no restante do stream.

## Header v1 — 32 bytes

| Offset | Tipo | Campo |
| ---: | --- | --- |
| 0 | 4 bytes | magic ASCII `DSKI` |
| 4 | u8 | major (=1) |
| 5 | u8 | minor (=0) |
| 6 | u8 | messageType |
| 7 | u8 | flags |
| 8 | u16 | headerBytes (=32) |
| 10 | u16 | frameBytes (header + payload + auth tag) |
| 12 | u64 | sessionId |
| 20 | u32 | sequence |
| 24 | u64 | baseMonotonicTimeUs |

Major desconhecido é incompatível. Minor novo só pode acrescentar extensões
trailing explicitamente opcionais. Flags reservadas diferentes de zero são
rejeitadas em M2. `sessionId` é aleatório por handshake; não é autenticação.

Limites:

- input frame sem fragmentação: no máximo 1200 bytes;
- control frame: no máximo 65535 bytes;
- input batch: 1..32 samples;
- validar todos os limites antes de alocar.

## `INPUT_BATCH` — messageType `0x10`

Prefixo do payload (4 bytes):

| Tipo | Campo |
| --- | --- |
| u8 | sampleCount (1..32) |
| u8 | toolKind |
| u16 | pointerId |

Um batch pertence a um pointer/tool. Eventos multipointer produzem batches
independentes; pointer ID é estável no motion set, pointer index não é transmitido.

### `PenSample` — 24 bytes

| Tipo | Campo | Unidade/semântica |
| --- | --- | --- |
| u32 | deltaTimeUs | diferença para `baseMonotonicTimeUs` |
| u32 | stateGeneration | incrementa em transição de contato/range/tool/button |
| u16 | xNormalized | 0..65535 na largura da área ativa |
| u16 | yNormalized | 0..65535 na altura da área ativa |
| u16 | pressureNormalized | 0..65535, válido somente pela flag |
| u16 | distanceNormalized | 0..65535, válido somente pela flag |
| u16 | tiltCentidegrees | 0..9000, válido somente pela flag |
| i16 | orientationCentidegrees | -18000..18000, válido somente pela flag |
| u16 | buttons | máscara de botões preservada |
| u16 | stateFlags | snapshot completo do estado |

`stateFlags` v1:

```text
0x0001 IN_RANGE
0x0002 CONTACT
0x0004 PRESSURE_VALID
0x0008 DISTANCE_VALID
0x0010 TILT_VALID
0x0020 ORIENTATION_VALID
0x0040 HISTORICAL
0x0080 CANCELED
```

Campo opcional ausente fica zero com validity bit desligado. Zero com bit ligado é
um valor real. `CONTACT` sem `IN_RANGE`, tilt > 9000 e orientation fora do range
são inválidos. Cada sample é snapshot, não delta; DOWN/UPDATE/UP são derivados
pela máquina de estado a partir de `CONTACT`, `IN_RANGE` e `stateGeneration`.

Android transmite tilt escalar + orientation. TiltX/TiltY Win32 são derivados uma
única vez no adapter Windows e testados em M4.

## Quantização

```text
normalized = round(clamp((value - min) / (max - min), 0, 1) * 65535)
degrees100 = round(radians * 180/pi * 100)
```

Ranges inválidos/não disponíveis desligam a validity flag. Na decodificação,
inteiros normalizados permanecem inteiros até o mapper que possui os ranges da
sessão, evitando NaN/Infinity no wire.

## Sequência, cancelamento e failsafe

- Duplicata: ignorar e contar.
- Número mais novo com gap: aceitar, contar gap; não retransmitir MOVE antigo.
- Reorder/stale: ignorar e contar.
- `CANCELED`: liberar estado e entrar em `SuppressedUntilPhysicalRelease`.
- Disconnect/watchdog: `ReleaseAll()` idempotente e supressão até observar snapshot
  físico sem contato; pacote atrasado não pode ressuscitar DOWN.
- Só frame válido, autenticado quando aplicável e mais novo alimenta watchdog.

## Envelope de input LAN — M6

O datagrama UDP contém o frame lógico `INPUT_BATCH` v1 seguido por uma tag de
16 bytes:

```text
logicalFrame || HMAC-SHA-256(sessionSecret, logicalFrame)[0..16]
```

`sessionSecret` possui exatamente 32 bytes e é aleatório por sessão pareada. A
tag é verificada em tempo constante antes do decode do frame. Session ID,
sequence, timestamp, limites e versão continuam no header lógico compartilhado
com USB. Datagramas truncados, alterados ou com chave errada são rejeitados.

O host abre LAN apenas com `--enable-lan`: `27185/TCP` para controle TLS e
`27186/UDP` para input. Ele cria um certificado ECDSA P-256 efêmero e exibe o
fingerprint SHA-256 e o código de comparação `XXXX-XXXX`. O Android conecta por
TLS, confirma que o certificado, o desafio e o código digitado representam o
mesmo fingerprint e somente então envia `LAN_PAIR_CONFIRM`. `LAN_PAIR_ACK`, ainda
dentro do TLS, entrega `sessionId`, segredo aleatório de 32 bytes, monitores e a
porta UDP. O segredo nunca é derivado do código curto.

Depois do primeiro datagrama autenticado, o host fixa o endpoint UDP da sessão.
Duplicatas e pacotes antigos são descartados. Se nenhum frame válido chegar por
750 ms, o host libera caneta/botões e exige uma amostra sem contato antes de
reativar o input, impedindo um DOWN atrasado de ressuscitar.

Discovery usa `27187/UDP`: o Android transmite uma query com nonce aleatório e o
host responde diretamente ao remetente com o nonce, nome, porta de controle e
fingerprint público. A resposta não contém segredo e não autentica o host; ela
serve apenas para localizar e exibir o código que o usuário compara. A confiança
continua sendo estabelecida somente pelo certificado TLS confirmado.

Após o primeiro pareamento, o host mantém sua identidade no certificate store
`CurrentUser/My` do Windows e o Android persiste somente o fingerprint público.
Uma resposta de discovery com o mesmo fingerprint pode reconectar sem código;
qualquer mudança de identidade bloqueia o pin e exige nova comparação manual.
O `sessionSecret` não é persistido nem derivado do certificado: cada handshake
TLS entrega um segredo UDP aleatório novo.

## Segurança

CRC não é autenticação e não faz parte do header v1. TCP já detecta corrupção de
transporte; validação estrutural detecta bugs de framing. LAN acrescenta tag
HMAC-SHA-256 truncada a 16 bytes sobre o frame lógico, com chave de
sessão entregue dentro do control channel autenticado. A política ADB/loopback
nunca pode ser selecionada por um peer LAN.

## Golden vector M2

`protocol/test-vectors/input-batch-v1.hex` contém um frame de 60 bytes com:

```text
sessionId 0x0102030405060708, sequence 42, base 1,000,000 us
tool STYLUS, pointer 7
delta 250 us, generation 9
x 32768, y 16384, pressure 49151, distance 1234
tilt 567, orientation -9000, button 32, flags 0x003F
```

O encoder Kotlin e o encoder/decoder C# devem produzir/consumir exatamente os
mesmos bytes. Vetores inválidos cobrem magic, versão, frame length, sample count,
flags/estados impossíveis e truncamento.

## Handshake USB/ADB — M2.5

O control channel usa frames com o mesmo header v1 e framing TCP `u32 length`:

```text
HELLO      (0x01): payload clientNonce u64; sessionId=0
HELLO_ACK  (0x02): sessionId no header + bindToken[16] + monitorCount u8 + selectedMonitorIndex u8
INPUT_BIND (0x03): sessionId no header + o mesmo bindToken de 16 bytes
SET_MOUSE_MODE (0x20): sessionId no header + u8 (0=click/drag, 1=pen scroll)
SET_MONITOR (0x21): sessionId no header + monitorIndex u8
SET_OVERLAY_TOOL (0x22): sessionId + u8 (0=pen, 1=highlighter, 2=eraser)
OVERLAY_COMMAND (0x23): sessionId + u8 (0=undo, 1=redo, 2=clear)
SET_OVERLAY_MODE (0x24): sessionId + u8 (0=hidden, 1=click-through, 2=interactive)
SET_OUTPUT_MODE (0x25): sessionId + u8 (0=mouse, 1=synthetic pen, 2=overlay)
EXECUTE_SHORTCUT (0x26): sessionId + u8 allowlisted (copy, paste, find, page up,
page down, media play/pause, volume down ou volume up)
```

O receiver aceita input somente após comparar o token em tempo constante. Ambos
os listeners usam apenas `127.0.0.1`; ADB fornece os sockets através de
`reverse tcp:27183 tcp:27183` e `reverse tcp:27184 tcp:27184`. O mapping é
reaplicado após reconectar o cabo; não existe reconexão implícita no meio do stroke.
O modo de mouse é latched no Android, persistido localmente e reenviado depois de
cada handshake; não é inferido de bits reservados dos samples.
A seleção de monitor também é persistida no Android. O host enumera os monitores,
anuncia quantidade/índice no `HELLO_ACK` e valida cada `SET_MONITOR` antes de
trocar o mapeamento de coordenadas.
`EXECUTE_SHORTCUT` aceita somente a enumeração fixa validada nos dois lados. O
host não recebe texto, caminho, processo ou linha de comando por esse frame.
