# USB versus LAN — benchmark físico curto

Data: 2026-08-14

## Resultado

| Transporte | Frames | Amostras | Gaps / duplicadas / reordenadas | RTT de controle | Incerteza do relógio | Latência p50 | Latência p95 | Latência p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| LAN autenticada | 4.123 | 22.155 | 0 / 0 / 0 | ~1,93 ms | ±0,97 ms | 6,79 ms | 13,24 ms | 16,19 ms |
| USB via `adb reverse` | 4.732 | 25.661 | 0 / 0 / 0 | 1,34 ms | ±0,67 ms | 7,78 ms | 10,99 ms | 13,08 ms |

Nesta execução, a LAN teve mediana 0,99 ms menor, enquanto o USB teve cauda
melhor: p95 2,25 ms menor e p99 3,11 ms menor. Os dois transportes ficaram sem
perda, duplicação ou reordenação observável.

O resultado não sustenta a afirmação simplista de que USB sempre terá a menor
mediana. Ele sustenta que, neste ambiente, USB foi mais previsível na cauda. É
necessário repetir várias rodadas antes de tratar a diferença de mediana como
característica do transporte.

## Metodologia

- Mesmo tablet, PC, aplicativo, host, protocolo e sessão de uso.
- Carga manual: movimentos contínuos de S Pen por aproximadamente 20 segundos,
  variando velocidade e pressão.
- O Android e o Windows sincronizaram seus relógios monotônicos com 8 trocas
  ping/pong; foi usada a amostra de menor RTT.
- A latência é estimada da captura Android até o fim da decodificação no host.
- Cada frame contribui com a amostra temporal mais recente. Os percentis finais
  usam uma janela limitada às 4.096 observações mais recentes.
- RTT LAN é derivado de `2 * uncertainty` porque a versão desta instrumentação
  registrou o RTT exato somente na interface Android; pode haver arredondamento
  de 1 µs. O RTT USB foi conferido na interface: `1,34 ms ±0,67 ms`.

Esta medição não inclui tempo de apresentação do aplicativo Windows, composição
da tela, resposta do painel ou fotodiodo/câmera. Portanto, não é uma medição
pen-to-photon e não deve ser apresentada como tal.

## Ambiente

- Tablet: Samsung SM-X400, Android 16 / API 36, S Pen `sec_e-pen`.
- Host: Windows NT 10.0.26200.0, AMD64 Family 25 Model 80.
- Runtime Windows: .NET SDK 10.0.400.
- ADB: 1.0.41.
- LAN: controle TLS/TCP em 27185, entrada UDP autenticada em 27186.
- USB: controle e entrada TCP em 27183/27184 via `adb reverse`.
- Saídas exercitadas durante as sessões: caneta sintética e overlay.

## Recursos e bateria

- CPU do host nos snapshots LAN: aproximadamente 0–6,3%.
- CPU do host nos snapshots USB: aproximadamente 0–6,9%.
- Working set observado: 67,6 MiB inicialmente e 92,8 MiB depois que o overlay
  já havia sido exercitado. Esse valor é cumulativo da mesma instância do host e
  não permite atribuir a diferença ao transporte.
- A bateria estava conectada por USB durante parte da sessão e o teste foi curto;
  a variação de nível não é uma medida válida de consumo.

CPU, memória e bateria precisam de uma rodada automatizada longa, com reinício do
host entre transportes, workload repetível e estado de carga controlado. Os
números acima servem como smoke benchmark físico, não como ensaio energético.

## Critério atual

A meta provisória de engenharia de p95 próximo ou abaixo de 15 ms para
captura→host foi atendida pelos dois transportes nesta rodada. O próximo passe de
performance deve acrescentar duração de injeção, jitter de RTT, múltiplas rodadas
e, posteriormente, medição pen-to-photon.

## Passe M8 — resultado otimizado

O perfil por estágio identificou envio USB/fila e batching Android como os custos
dominantes; decode do host ficou na ordem de microssegundos. Após agrupar as
escritas do framing USB e habilitar dispatch sem buffering com fallback manual:

| Transporte otimizado | Latência p50 | Latência p95 | Latência p99 | Perdas observadas |
| --- | ---: | ---: | ---: | ---: |
| USB/ADB low-latency | 3,11 ms | 7,93 ms | 10,00 ms | 0 |
| LAN autenticada low-latency | 2,62 ms | 6,98 ms | 10,40 ms | 0 |

Os números são trechos contínuos após aquecimento e usam a mesma definição
captura→decode das rodadas anteriores. Detalhes por estágio, A/B e recuperação
do listener LAN estão em `docs/evidence/m8-performance-2026-08-14.md`.
