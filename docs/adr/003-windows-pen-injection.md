# ADR 003 — Synthetic pointer para caneta

Aceito para protótipo M4. O host usará `CreateSyntheticPointerDevice` com PT_PEN
e `InjectSyntheticPointerInput`. `SendInput` permanece para mouse/teclado. Ranges,
flags e lifecycle serão validados em Windows 11 e apps reais antes de fechar M4.
