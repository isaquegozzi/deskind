# M9 — Packaging e interface final — 2026-08-14

## Entregas

- Interface Android tablet-first com área ativa em tela inteira, controles nas
  bordas/cantos e painéis flutuantes recolhíveis.
- Modos diretos e separados: cursor, scroll persistente, caneta Windows, caneta
  overlay, marca-texto e borracha.
- Seleção de monitor, overlay undo/redo/clear e diagnóstico no app.
- Atalhos allowlisted: copiar, colar, buscar, página, play/pause e volume.
- Host Windows 10/11 x64 self-contained em arquivo único.
- APK 0.1.0 assinado para sideload e guia offline de instalação.
- `scripts/package-mvp.ps1` produz pasta, ZIP e instalador EXE reproduzíveis.

## Validação automatizada

`scripts/test-all.ps1` passou integralmente:

- build Android Debug;
- testes unitários Android e vetores do protocolo;
- build Windows sem avisos ou erros;
- framing, handshake, TLS, autenticação UDP e discovery LAN;
- mouse, caneta sintética, overlay e desktop mapping;
- carga sintética de 100 mil amostras, com 1.896 bytes de crescimento retido.

O build Android Release também passou `lintVitalRelease`. O APK final foi aceito
por `apksigner` com APK Signature Scheme v2. O pacote portátil contém somente os
três itens esperados: APK, EXE e `LEIA-ME.md`; nenhum CMD ou PowerShell é necessário
para iniciar o produto.

## Smoke físico e do pacote

- O APK extraído do pacote foi instalado com sucesso no tablet físico
  `SM_X400`/S Pen.
- A interface foi aberta e inspecionada em retrato; insets de status/navigation
  foram aplicados para evitar controles sob as barras do sistema.
- Atalhos foram aprovados fisicamente em USB (`PageDown`) e LAN (`PageUp`) sem
  encerrar a sessão. O layout nativo `INPUT` foi corrigido para os 40 bytes
  exigidos no Windows x64; falha futura de `SendInput` não derruba mais o canal.
- O executável self-contained enumerou os dois monitores físicos.
- A instância empacotada abriu listeners em `127.0.0.1:27183/TCP`,
  `0.0.0.0:27185/TCP` e `0.0.0.0:27186-27187/UDP`; depois foi encerrada pelo
  caminho exato do processo.

## Hashes SHA-256

```text
DeskInk.WindowsHost.exe
6D62298257D0040877ABC7A770C8E0A40974C271FD343A92EF1D00D2715DD451

DeskInk-Android-0.1.0.apk
E6C5FC04BB9F20D84080A929A032101FAF0D84B6C4B97A05E4602BA8EF98A4AE

DeskInk-0.1.0-win-x64.zip
36CC75477414E275B70C6621D06F6DD3FF135A89C2D3D93C9BB0C4823FCCC19B

DeskInk-Setup-0.1.0-win-x64.exe
409E42E27998259E6CA39BE88B8C7FB642FF8D9F132108AFCCDF1D313B3EC786
```

O host, o ZIP e o instalador foram republicados após o gate S2 para incluir o
watchdog USB/TCP de 750 ms e novamente após o gate S3 para incluir a UI Windows
sem console. O APK não mudou e preservou o hash e a assinatura v2.

## Limites declarados

O APK usa a chave Android de desenvolvimento para sideload do MVP, não para loja.
O instalador Windows ainda não possui assinatura Authenticode e pode exibir o
aviso de editor desconhecido. O transporte USB ainda depende de ADB/Developer
Options. O mapeamento usa toda a tela escolhida e pode estirar coordenadas quando
tablet e monitor têm proporções diferentes.
