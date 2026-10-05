# S2 — Simulador de input sem tablet — 14/08/2026

## Escopo validado

- cliente real do protocolo USB/TCP em `DeskInk.InputSimulator`;
- `HELLO` / `HELLO_ACK` no canal de controle;
- `INPUT_BIND` autenticado no canal de input;
- framing de produção com lotes de no máximo 32 amostras;
- stroke determinístico com `DOWN`, `MOVE x1000` e `UP`;
- cenários determinísticos normal, duplicata, reordenação, lacuna de sequência
  e encerramento sem `UP`;
- rejeição de bind token inválido, session falsa, versão incompatível, frame
  truncado e `sampleCount=0`;
- conexão válida de recuperação depois das rejeições;
- storm sequencial de 25 reconexões válidas;
- pausa de 1,2 s durante contato para acionar o watchdog USB/TCP de 750 ms;
- supressão de contato tardio, rearme neutro e novo `DOWN/UP` após timeout;
- encerramento do canal somente depois que o host consumiu todas as amostras;
- host iniciado com outputs desabilitados, sem injetar mouse ou caneta reais.

## Comando reproduzível

```powershell
dotnet build apps/windows/DeskInk.sln --configuration Debug
./scripts/test-input-simulator.ps1
```

O teste recusa executar se as portas `27183` ou `27184` já estiverem ocupadas.
Ele inicia e encerra somente o host que criou para o ensaio.

## Resultado observado

```text
SIMULATOR PASS scenario=normal ... framesSent=32 samplesGenerated=1002 ... up=1
SIMULATOR PASS scenario=duplicate ... framesSent=33 samplesGenerated=1002 ... up=1
SIMULATOR PASS scenario=reorder ... framesSent=33 samplesGenerated=1002 ... up=1
SIMULATOR PASS scenario=gap ... framesSent=32 samplesGenerated=1002 ... up=1
SIMULATOR PASS scenario=missing-up ... framesSent=32 samplesGenerated=1001 ... up=0
SIMULATOR REJECTION PASS scenario=bad-bind
SIMULATOR REJECTION PASS scenario=wrong-session
SIMULATOR REJECTION PASS scenario=bad-version
SIMULATOR REJECTION PASS scenario=truncated
SIMULATOR REJECTION PASS scenario=invalid-sample-count
SIMULATOR PASS scenario=timeout framesSent=5 contactDuringPause=1 neutralRecovery=1
HOST PASS lifecycle faults=5 watchdog=1 reconnects=25 sessions=37
```

No cenário normal, `release=None` confirma que o `UP` chegou. No cenário
`missing-up`, `release=Up` confirma que o fechamento do canal neutralizou o
contato preso. O host também confirmou `dup=1`, `reorder=1` e `gaps=1` nos
respectivos cenários. Todos passam pelo decoder, controle de sessão,
`SequenceTracker` e `PenStateMachine` reais.

As cinco conexões malformadas foram fechadas isoladamente com `ProtocolException`.
Uma conexão normal imediatamente posterior passou, seguida por 25 sessões
rápidas. Isso confirma que os erros não encerram os listeners nem contaminam a
sessão seguinte.

No cenário `timeout`, o host liberou os outputs após 750 ms, ignorou uma amostra
ainda em contato, aceitou a amostra neutra de rearme e processou um novo ciclo
`DOWN/UP`. O resumo confirmou quatro frames aceitos, uma lacuna correspondente
ao contato suprimido e fechamento final neutro.

Coordenadas e pressão do protocolo v1 são inteiros normalizados sem sinal de 16
bits. Logo, todos os padrões binários entre `0` e `65535` são válidos; não existe
NaN, infinito ou valor wire fora dessa faixa. A suíte Core confirmou round-trip
dos dois extremos. A presença/ausência de pressão é expressa por
`PressureValid`, não pelo valor numérico.

## Integração contínua local

`scripts/test-all.ps1` executa esse gate depois do build e dos testes Windows.
Os próximos itens de S2 continuam sendo perda física controlada em ensaio
prolongado e ampliação do lifecycle de mouse/atalhos.
