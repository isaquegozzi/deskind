# ADR 002 — Transporte USB/ADB primeiro

Aceito em 2026-08-13. ADB reverse sobre duas conexões TCP será o baseline de
desenvolvimento. O protocolo lógico não conhece ADB. LAN reutilizará mensagens,
decoder e state machine, com control TCP e input UDP autenticado. Native USB é
pós-MVP.
