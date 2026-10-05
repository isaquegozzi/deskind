# Evidência M8 — Performance pass

Data: 2026-08-14

Hardware: Samsung SM-X400 com S Pen e host Windows, mesmos equipamentos das
medições M6.5.

## Perfil que guiou as mudanças

Primeiro perfil USB, com batching e framing original:

- Android prepare p50/p95/p99: 402/631/790 µs.
- Android encode: 65/111/149 µs.
- Android queue: 243/2.640/5.874 µs.
- Android send: 2.192/4.556/6.190 µs.
- Host decode: aproximadamente 5/8/9 µs.
- Host synthetic-pen dispatch: aproximadamente 515/726/898 µs.
- 87% das amostras eram históricas.

O perfil mostrou que decode não era gargalo; envio USB, fila e batching eram.

## Otimizações confirmadas

1. O stream USB passou a usar `BufferedOutputStream`, reunindo prefixo e payload
   em uma escrita efetiva por frame.
2. A injeção sintética Windows reutiliza o buffer de um ponteiro em vez de criar
   um array por amostra.
3. `requestUnbufferedDispatch` passou a ser o padrão, com opção persistente para
   voltar ao modo compatível com batching.
4. O painel publica percentis limitados para prepare/encode/queue/send e o host
   para decode/dispatch.

### Efeito isolado do framing USB

- Send p50: 2,19 → 0,99 ms (−55%).
- Send p95: 4,56 → 1,83 ms (−60%).
- Queue p95: 2,64 → 1,02 ms (−62%).
- Latência captura→decode p50/p95/p99: aproximadamente
  8,3/11,5/13,9 → 6,52/9,13/11,26 ms.

### Efeito adicional do dispatch sem buffering

- Historical samples: 87% → 56% no USB.
- USB captura→decode: 3,11/7,93/10,00 ms.
- Android USB prepare: 175/473/583 µs.
- Android USB queue: 182/625/1.481 µs.
- Android USB send: 700/1.397/2.012 µs.

## LAN low-latency

Em trecho contínuo após aquecimento:

- captura→decode p50/p95/p99: aproximadamente 2,62/6,98/10,40 ms;
- Android prepare: 168/353/478 µs;
- Android queue: 160/396/723 µs;
- Android send: 357/1.083/1.575 µs;
- host decode: aproximadamente 10/16/22 µs;
- zero gaps, duplicações ou reordenações.

## Regressão descoberta e corrigida

Durante o ensaio LAN, uma exceção não-Protocol dentro do processamento encerrava
o único listener UDP sem encerrar o TLS. O tablet continuava exibindo CONNECTED,
mas não havia entrada. A fronteira foi corrigida para:

- registrar falhas de socket e tentar novamente com backoff;
- registrar falhas de processamento;
- liberar as saídas, recriar somente o pipeline e aguardar amostra neutra;
- manter o listener UDP global ativo.

Teste físico de recuperação aprovado: desenho, pausa acima do watchdog, retomada,
nova pausa e troca entre os dois monitores. O log manteve `INPUT LAN`, registrou
os watchdogs e não voltou a emitir `LAN INPUT CLOSED`.

## Limites

Os valores são captura Android→decode/dispatch no host, não pen-to-photon. Ensaio
energético prolongado, composição da tela e câmera/fotodiodo continuam fora deste
gate e estão documentados como trabalho posterior.
