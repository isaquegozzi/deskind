# Plano do MVP e gates

Status em 2026-08-13. Um gate só fica concluído com comando/evidência registrado.

## M0 — Foundation (em andamento)

Entregas: monorepo, projetos Android/Windows, protocolo inicial, testes, scripts,
documentação e inventário da toolchain.

Gate:

- [ ] Android builda em ambiente limpo.
- [ ] Windows builda.
- [ ] testes básicos passam.
- [x] arquitetura, protocolo, latência, segurança, riscos e toolchain documentados.

Bloqueios encontrados: Android SDK/JDK e .NET SDK/Windows SDK não estão instalados
ou detectáveis. Consulte `docs/toolchain.md`.

## M1 — Android stylus telemetry (em andamento)

Critérios de aceitação:

- [ ] APK instalado no tablet físico autorizado.
- [ ] stylus identificada por tool type/device.
- [ ] X/Y variam e respeitam o tamanho da área ativa.
- [ ] pressure varia quando o hardware reporta.
- [ ] tilt/orientation variam quando suportados.
- [ ] hover/distance são exibidos quando suportados.
- [ ] buttons e eraser tool são preservados.
- [ ] todos os historical samples precedem o current sample e são contados.
- [ ] `ACTION_CANCEL` encerra o estado.
- [ ] tela mostra capabilities/ranges e telemetria viva sem logging por amostra.
- [ ] build, testes unitários e sessão física têm evidências salvas.

## M2 — Protocol core

Congelar formato v1, implementar encoder Kotlin/decoder C#, test vectors gerados
pelo Android, validação/fuzz e máquina de estado. Gate: equivalência de 100% dos
vetores e 100k+ amostras sintéticas sem crash/crescimento anormal.

## M2.5 — USB/ADB transport

Automatizar device selection, dois `adb reverse`, handshake, lifecycle,
diagnósticos e benchmark. Gate físico inclui fidelity de pressure/tilt/history,
sequência, cabo removido sem estado preso e reconexão manual funcional.

## M3 — Mouse via USB

Movimento absoluto, monitor mapping, clique, drag, right click e scroll. Gate:
uma sessão real de controle do Windows usando apenas o tablet via USB.

## M4 — Synthetic pen via USB

PT_PEN DOWN/UPDATE/UP/hover/pressure/tilt/buttons e watchdog. Gate: input correto
em aplicações Windows apropriadas e nenhum estado preso em perda de conexão.

## M5 — Global overlay

Pen, highlighter, eraser, undo, redo e clear sobre uma janela transparente global,
sem screenshot e com modelo vetorial.

## M6 — LAN transport

Discovery, pairing/autenticação, control TCP, input UDP, loss/reorder/jitter. Gate:
as mesmas funções de USB operam via LAN usando o mesmo protocolo/estado.

## M6.5 — USB versus LAN

Benchmark no mesmo hardware e relatório `docs/transport-performance.md` com
latência, jitter, perda, CPU e bateria medidos.

## M7 — Study Mode

Stylus como pen/highlighter, dedo como scroll, side button como eraser e gesto de
dois dedos configurável, nos dois transportes.

## M8 — Performance pass

Otimização guiada por perfis em capture/encode/send/receive/decode/inject/overlay.

## M9 — Packaging

Host Windows e APK distribuíveis, seletor USB/LAN, métricas, monitor selection e
documentação de instalação/troubleshooting.

## Fora do MVP

Screen streaming, WebRTC/vídeo, local ink reconciliation, Native USB e content-
aware annotations.
