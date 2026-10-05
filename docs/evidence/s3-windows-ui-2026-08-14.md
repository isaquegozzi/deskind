# S3 — Primeira UI Windows — 14/08/2026

## Entrega

- janela WinForms leve ativada por `--ui`, sem Electron e sem dependências novas;
- modo console preservado para testes e diagnóstico automatizado;
- estados `Aguardando conexão` e `Conectado`;
- transporte USB/ADB ou LAN, session ID, monitor e modo de saída;
- última atividade e totais de frames/amostras;
- botão para copiar diagnóstico;
- ocultação para bandeja, restauração por duplo clique/menu e saída explícita;
- saída cancela o host e passa pela neutralização existente dos outputs;
- iniciador do pacote atualizado para abrir a UI com todos os outputs e LAN.

## Isolamento do hot path

A janela consulta um snapshot thread-safe a cada 250 ms. O receiver apenas atualiza
contadores atômicos por frame; não há renderização, evento de UI ou lock por
amostra no caminho de input.

## Validação

- solução Windows compilada em Debug sem erros ou avisos;
- gate do simulador passou com 37 sessões após a integração;
- inspeção visual real em 480x520 confirmou layout, contraste e ausência de
  overflow;
- árvore de acessibilidade expôs títulos, valores e os botões `Copiar
  diagnóstico`, `Ocultar` e `Sair`;
- a UI transitou de desconectada para USB/ADB conectado com session ID e métricas,
  e retornou a desconectada preservando os totais;
- `Ocultar` removeu a janela sem encerrar o processo.

## Próximos incrementos

- latência/perda em tempo real;
- identidade amigável do tablet e pareamento LAN;
- configurações editáveis e inicialização com Windows;
- exportação de logs;
- ícone/identidade visual próprios em vez do ícone de sistema provisório.
