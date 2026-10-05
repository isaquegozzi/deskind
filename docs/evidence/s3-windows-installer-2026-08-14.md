# S3 — Aplicativo e instalador Windows — 2026-08-14

## Entrega

- host publicado como aplicativo Windows `WinExe`, sem console associado;
- execução direta sem argumentos habilita UI, USB/ADB, LAN e todas as saídas;
- instância única impede dois hosts concorrentes;
- configuração automática de `adb reverse` para as portas 27183 e 27184;
- instalador Inno Setup por usuário, sem exigir privilégios administrativos;
- atalhos `DeskInk` na Área de Trabalho e no menu Iniciar;
- desinstalador normal do Windows;
- ZIP portátil preservado como alternativa.

## Validação

- instalação/atualização silenciosa do pacote final concluída com sucesso;
- executável iniciado diretamente de
  `%LOCALAPPDATA%\Programs\DeskInk\DeskInk.exe`, sem argumentos;
- janela e ícone de bandeja inspecionados, sem console do DeskInk;
- processo instalado abriu `127.0.0.1:27183/TCP`, `0.0.0.0:27185/TCP` e
  `0.0.0.0:27186-27187/UDP`;
- atalhos da Área de Trabalho e menu Iniciar encontrados;
- `unins000.exe` encontrado na pasta instalada;
- gate do simulador repetido após mudar para `WinExe`: 37 sessões aprovadas,
  incluindo faults, watchdog e 25 reconexões rápidas;
- APK verificado novamente com APK Signature Scheme v2.

## Limite conhecido

O instalador 0.1.0 não tem assinatura Authenticode. Até existir um certificado de
code signing, o Windows pode mostrar “Editor desconhecido”; isso não altera o hash
SHA-256 registrado na evidência de packaging.
