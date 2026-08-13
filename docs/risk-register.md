# Registro de riscos

| ID | Risco | Impacto | Mitigação/gate |
| --- | --- | --- | --- |
| R1 | Hardware não reporta pressure/tilt/hover | fidelity | capability + ranges observados; fallback explícito |
| R2 | Historical samples perdidos na UI | traço irregular | copiar history antes do current; contador/teste físico M1 |
| R3 | TCP/ADB acumula MOVE antigo | latência | canal dedicado, bounded queue, coalescing e TCP_NODELAY |
| R4 | Cabo/rede cai com input DOWN | input preso | state machine + watchdog + release-all em disconnect |
| R5 | Android tilt não mapeia diretamente a tiltX/Y Win32 | caneta incorreta | adapter único, vetores e validação em apps reais M4 |
| R6 | DPI/multi-monitor/origens negativas | cursor deslocado | DisplayManager + testes de mapping M3 |
| R7 | Listener LAN permite injeção não autorizada | crítico | pairing, HMAC, sessão/sequence e bind seguro M6 |
| R8 | Pacote malformado causa DoS/crash | alto | limites antes de alocar, fuzz e reject metrics M2 |
| R9 | UI/logging causa GC no hot path | latência/jitter | buffers primitivos, snapshot limitado, profiling markers |
| R10 | Overlay captura clicks indevidamente | UX/segurança | modos interactive/click-through explícitos M5 |
| R11 | Toolchain ausente/incompatível | bloqueia build | scripts fail-fast e versões fixadas em toolchain.md |
| R12 | Métrica cross-device calculada com clocks não sincronizados | conclusão falsa | RTT/offset com incerteza; separar durações locais |
| R13 | ADB exige Developer Mode | adoção | documentar power-user mode; Native USB só pós-MVP |
| R14 | LAN diverge funcionalmente do USB | manutenção | mesmo protocolo/decoder/state; testes de conformidade M6 |

O registro é atualizado ao fechar cada gate; risco não é marcado resolvido sem
evidência física quando depende do hardware.
