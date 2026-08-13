# Segurança

## Ativos e fronteiras

O host pode injetar mouse, teclado e caneta, portanto input não autorizado equivale
a controle local. Fronteiras: app Android, cabo/ADB, LAN, listeners Windows,
configurações e logs.

## Ameaças principais

- emissor LAN não pareado injeta input;
- replay/duplicação deixa botões ou pen presos;
- pacote malformado causa alocação, crash ou estado inválido;
- serviço escuta interfaces indevidas no modo ADB;
- segredo aparece em log/configuração;
- shortcut executa processo/script arbitrário;
- downgrade de versão remove validações.

## Controles

- ADB listeners fazem bind somente em loopback por padrão; o tunnel é configurado
  para serial explícito.
- Handshake sempre negocia versão e session id aleatório. O modo ADB confia na
  autorização do ADB para o transporte, mas mantém validação e lifecycle.
- LAN (M6) exige pairing explícito, segredo aleatório do SO e mensagens
  autenticadas com primitiva padrão (candidato: HMAC-SHA-256 com tag truncada
  somente após revisão). Nonce/session/sequence impedem replay entre sessões.
- Parser aplica limites antes de alocar, rejeita NaN/Infinity/flags reservadas,
  valida CRC/tag em tempo constante quando aplicável e nunca injeta pacote inválido.
- Disconnect/timeout/cancel libera pen, mouse buttons e modifiers.
- Segredos não entram em logs; logs de coordenadas são opt-in de diagnóstico.
- Atalhos são combinações declarativas allowlisted; sem shell/process execution.
- Host sem pairing não abre listener LAN para input.

## Pendências por gate

M2 fará fuzz/malformed packet tests e formalizará a state machine. M6 escolherá o
fluxo de pairing, armazenamento seguro e autenticação LAN após threat-model review.
Criptografia de transporte no ADB não será adicionada sem ameaça demonstrada.
