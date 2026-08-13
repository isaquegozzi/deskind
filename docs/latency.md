# Latência e performance

Nenhum número exibido pelo produto é hardcoded. USB é baseline, não garantia de
ser mais rápido que LAN.

## Marcadores

```text
ANDROID_CAPTURE -> ANDROID_ENCODE -> ANDROID_SEND
WINDOWS_RECEIVE -> WINDOWS_DECODE -> WINDOWS_INJECT -> OVERLAY_DRAW
```

Cada estágio usa clock monotônico local (`elapsedRealtimeNanos` no Android e
`Stopwatch.GetTimestamp`/QPC no Windows). Diferenças entre clocks de dispositivos
não são one-way latency. Ping/pong estima RTT e, quando necessário, offset com
incerteza registrada.

## Métricas

- samples/s, packets/s, batches e tamanho;
- queue depth, coalesced/dropped samples;
- sequence gaps, duplicates e reorder;
- RTT e jitter p50/p95/p99;
- encode/decode/inject duration p50/p95/p99;
- injection failures, reconnects e watchdog releases;
- CPU, memória e bateria em benchmark prolongado.

Histogramas usam armazenamento limitado; logging textual não ocorre no hot path.
O painel publica snapshots em frequência baixa.

## Gate de medição

1. Validar captura local e contagem de historical samples.
2. Rodar carga sintética reprodutível no protocolo.
3. Medir USB/ADB por sessão curta e prolongada.
4. Repetir em LAN no mesmo hardware/condições.
5. Publicar ambiente, duração, versões e percentis em
   `docs/transport-performance.md` (M6.5).

Meta de engenharia anterior ao vídeo: capture->receive->inject p95 perto ou abaixo
de 15 ms em boas condições, sempre acompanhado da metodologia e do resultado real.
