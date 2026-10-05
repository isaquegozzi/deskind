# DeskInk

Transforma um tablet Android com caneta em uma mesa digital de baixa latência para o Windows. O tablet envia os dados da caneta e o PC desenha o cursor, a pressão, o traço e as ferramentas de marcação.

O MVP oferece cursor, rolagem pela caneta, pressão com caneta sintética, desenho sobre qualquer tela, marca-texto, borracha, múltiplos monitores e atalhos básicos.

## Estado atual

MVP 0.1.0 concluído. As etapas M0 a M9 do plano de implementação estão registradas como concluídas no `docs/mvp-plan.md`.

O MVP ainda tem limitações para uso em produção:

**As funcionalidades de M0 a M9 estão implementadas, mas isso não significa que o aplicativo esteja pronto para produção.** O APK de release usa uma chave de desenvolvimento, o instalador Windows não tem assinatura digital, a conexão USB exige o modo desenvolvedor do Android e a imagem pode ficar esticada quando o tablet e o monitor têm proporções diferentes. Esses pontos estão registrados em `docs/evidence/m9-packaging-2026-08-14.md`.

**A latência medida cobre apenas a captura no tablet e a decodificação no PC.** Ela não mede o tempo até o traço aparecer na tela, chamado de "pen-to-photon". A diferença está explicada em `docs/transport-performance.md`.

Além disso, o M6.5 ficou parcial: foi feito um teste inicial de desempenho, mas o ensaio prolongado de bateria e consumo e a medição pen-to-photon continuam pendentes. A fase 1.5 do plano, que vem depois do MVP, está quase toda pendente.

## Funcionalidades implementadas

### Captura no Android

- **19 arquivos Kotlin** que recebem os eventos da tela e os encaminham.
- A captura copia primeiro todos os pontos históricos do toque e por último o ponto atual, o que reduz a perda de traço em movimento rápido.
- Seis modos de entrada: Cursor, Scroll, Caneta, Overlay, Marca e Apagar.
- Painel de conexão com três caminhos: Conectar USB, conectar por IP com código, ou buscar PCs na rede.
- Atalhos de teclado: copiar, colar, buscar, página acima e abaixo, play e pausa, volume.
- Painel de diagnóstico com latência, transporte e contadores.

### Host no Windows

Cinco projetos C#:

- **Core** — codec do protocolo, transporte, máquina de estados, tradução de mouse, caneta e overlay, mapeamento de coordenadas e pareamento TLS.
- **Overlay** — janela global por monitor, desenhada com GDI+ em superfície ARGB e composta com `UpdateLayeredWindow`.
- **WindowsHost** — o executável, com interface de status e configuração automática do ADB.
- **Core.Tests** — executável de testes próprio.
- **InputSimulator** — cliente de protocolo sem tablet, para exercitar o host.

A interface do host é Windows Forms. Não há nenhum pacote NuGet externo: as únicas dependências são as APIs do Windows chamadas por `DllImport`.

### Protocolo

Protocolo binário, com controle e dados em canais separados. Não há JSON por ponto, que é uma restrição deliberada do projeto.

- Cabeçalho de 32 bytes, com o número mágico `DSKI`, versão, tipo de mensagem, flags, identificador de sessão e número de sequência. Tudo em little-endian, montado campo a campo, sem struct nativo serializado.
- Mensagem `INPUT_BATCH` com até 32 amostras. Cada amostra tem 24 bytes com pressão, inclinação, orientação, distância e flags.
- Oito flags de estado: in-range, contato, pressão válida, distância válida, inclinação válida, orientação válida, ponto histórico e cancelado.
- Canais de controle separados dos canais de input.
- Na LAN, cada quadro lógico recebe uma autenticação HMAC-SHA-256 sobre um segredo de 32 bytes por sessão, com comparação em tempo constante.
- Os atalhos são uma enumeração fixa de oito comandos. O host nunca recebe texto, caminho, processo ou linha de comando.

### Teste com vetor compartilhado

`protocol/test-vectors/input-batch-v1.hex` contém um quadro de 60 bytes. O mesmo arquivo é lido pelos dois lados: o Gradle o expõe como recurso de teste do Android, e o projeto de testes C# o copia para a saída e exige igualdade byte a byte.

## Tecnologias

| | Versão |
|---|---|
| Kotlin | compilador embutido no Android Gradle Plugin 9.2 |
| Android Gradle Plugin | 9.2.0 |
| Gradle | 9.4.1 |
| `compileSdk` / `targetSdk` | 36 |
| `minSdk` | 26, ou seja Android 8.0 |
| Java | 17 |
| .NET | 10, nos alvos `net10.0` e `net10.0-windows` |
| Interface Windows | Windows Forms |
| Testes Android | JUnit 4 |
| Empacotamento | Inno Setup 6 |

A interface do Android é construída com Views do próprio framework, em código. Não há Jetpack Compose, nem AndroidX, nem Material Components: a única dependência de teste é o JUnit.

As APIs do Windows usadas diretamente são `CreateSyntheticPointerDevice`, `InjectSyntheticPointerInput` e `DestroySyntheticPointerDevice` para a caneta sintética, `SendInput` para mouse e teclado, e um conjunto de funções de GDI com `UpdateLayeredWindow` para o overlay.

## Como executar

### Requisitos

- Windows 10 ou 11, x64
- JDK 17
- Android SDK, com a Platform 36 e as Build Tools 36.0.0
- .NET 10 SDK
- ADB no `PATH`, para o transporte USB
- Inno Setup 6, só para gerar o instalador

O tablet usado nos testes é um **Samsung SM-X400**, com Android 16 e a caneta S Pen reportada como `sec_e-pen`.

### Compilar

```powershell
./scripts/build-android.ps1
./scripts/build-windows.ps1
```

O script do Android procura um JDK 17 do Temurin em `C:\Program Files\Eclipse Adoptium\jdk-17*` quando `JAVA_HOME` está vazio.

### Rodar em desenvolvimento

O caminho mais curto, que faz build, instala e inicia os dois lados:

```powershell
./scripts/dev-usb.ps1 -Device <serial-adb>
```

Para rodar só o host, com todas as saídas:

```powershell
dotnet run --project apps/windows/DeskInk.WindowsHost -- --enable-all --enable-lan
```

As flags do host são `--headless`, `--ui`, `--enable-mouse`, `--enable-pen`, `--enable-overlay`, `--enable-all`, `--monitor N` e `--enable-lan`. Sem argumento nenhum, o host liga a interface, todas as saídas e a LAN.

### Empacotar

```powershell
./scripts/package-mvp.ps1
```

Gera o instalador, o pacote portátil em ZIP e a pasta de distribuição com o APK, o executável e um arquivo de leitura. As saídas vão para `artifacts/`, que é ignorado pelo Git.

## Como usar

### Pelo USB

1. No tablet, ligue **Opções do desenvolvedor** e **Depuração USB**.
2. Conecte o cabo e aceite a chave RSA.
3. Aplique dois túnel reverso. Nunca `adb forward`:

```powershell
adb reverse tcp:27183 tcp:27183
adb reverse tcp:27184 tcp:27184
```

4. Rode `./scripts/dev-usb.ps1 -Device <serial>`, ou, se o APK já estiver instalado, abra o aplicativo e toque em **Conexão > Conectar USB**.

### Pela rede local

1. PC e tablet na mesma rede Wi-Fi.
2. No aplicativo, toque em **Conexão > Buscar PCs DeskInk na rede**.
3. Confira o código `XXXX-XXXX` na janela do host e digite-o no tablet.
4. Toque em **Conectar LAN**. As próximas conexões reutilizam o certificado já registrado e não pedem o código de novo.

As portas são TCP 27185 para controle com TLS, UDP 27186 para input e UDP 27187 para descoberta. A LAN só abre com `--enable-lan`, e o acesso é restrito à rede local.

### Controles

- **Cursor** move, clica e arrasta.
- **Scroll** mantém a rolagem com a caneta.
- **Caneta** usa a caneta sintética, com pressão.
- **Marca** e **Apagar** funcionam no overlay global.
- **Monitor** alterna a tela alvo.
- **Atalhos** cobrem copiar, colar, buscar, páginas e mídia.
- Tocar no status abaixo de "DeskInk" abre o diagnóstico.

**É preciso um tablet com caneta.** O dedo funciona para rolar. Um mouse conectado ao tablet não move o cursor no Windows: o tipo de ferramenta `MOUSE` é descartado na captura, de propósito, para evitar conflito com o mouse físico.

## Organização do projeto

| Pasta | Responsabilidade |
|---|---|
| `apps/android/` | Aplicativo Kotlin: captura, protocolo, transporte e interface |
| `apps/windows/` | Solução C# com cinco projetos: núcleo, overlay, host, testes e simulador |
| `protocol/` | O vetor de teste em hex compartilhado entre as duas plataformas |
| `scripts/` | Scripts PowerShell de build, instalação, execução, teste e empacotamento |
| `docs/` | Documentos de arquitetura, protocolo, plano, ferramentas, segurança e três ADRs |
| `docs/evidence/` | Catorze documentos datados, um por marco, com os resultados medidos |
| `.github/workflows/ci.yml` | Pipeline de build e teste no Windows |

Os dois documentos `PROMPT MESTRE` da raiz são os textos de requisitos originais que definiram o escopo do projeto.

## Latência

Os números abaixo vêm de `docs/transport-performance.md`. Vale ler a ressalva antes de citá-los: **medem captura até decodificação, não pen-to-photon**, e o próprio documento se recusa a dizer que o USB é sempre o mais rápido, já que na rodada de base a mediana da LAN ficou 0,99 ms menor que a do USB.

Medição de base, antes da otimização:

| Transporte | Frames | Amostras | p50 | p95 | p99 |
|---|---|---|---|---|---|
| USB via `adb reverse` | 4.732 | 25.661 | 7,78 ms | 10,99 ms | 13,08 ms |
| LAN autenticada | 4.123 | 22.155 | 6,79 ms | 13,24 ms | 16,19 ms |

Medição depois da otimização:

| Transporte | p50 | p95 | p99 | Perdas |
|---|---|---|---|---|
| USB/ADB | 3,11 ms | 7,93 ms | 10,00 ms | 0 |
| LAN autenticada | 2,62 ms | 6,98 ms | 10,40 ms | 0 |

Como a medição foi feita:

- mesmo tablet, PC, aplicativo, host, protocolo e sessão;
- carga manual de movimentos contínuos de caneta por cerca de 20 segundos, não um benchmark sintético;
- relógios monotônicos sincronizados por oito trocas de ping e pong, usando a amostra de menor tempo de ida e volta;
- janela de percentis calculada sobre as 4.096 observações mais recentes;
- uma amostra por quadro, a última do lote.

Duas coisas que precisam ser ditas com honestidade:

1. **Os números só existem na documentação.** Não há arquivo de log versionado que permita a um terceiro reproduzi-los. Existe um log real de sessão LAN em `artifacts/test-logs/`, com 8.452 amostras e sem perdas, mas ele fica na faixa dos 6,98 ms sem reproduzir o valor exato.
2. **A ausência de perdas vale para as rodadas de benchmark, não para toda sessão.** Uma sessão LAN do marco M6 registrou 29 lacunas de UDP, toleradas sem retransmitir o movimento antigo.

## Limitações e pendências

### Defeitos e restrições conhecidos

- **O APK de release é assinado com a chave de debug**, com um comentário no arquivo de build dizendo que deve ser trocada por uma chave privada antes de qualquer distribuição em loja.
- **O instalador Windows não tem assinatura Authenticode**, então o Windows mostra um aviso de editor desconhecido.
- **O caminho USB exige o modo desenvolvedor do Android.** Sem ADB e sem depuração USB, o transporte USB não funciona. O registro de riscos do projeto classifica isso como item de adoção, referindo-se ao USB nativo como algo posterior ao MVP.
- **O mapeamento de coordenadas pode esticar a imagem.** A área ativa cobre a tela inteira escolhida, e proporções diferentes entre tablet e monitor produzem distorção. Não há calibração de área ativa, que é um item pendente da fase 1.5.
- **Não há failover no meio de um traço.** Se a conexão cair, é preciso reconectar manualmente e reaplicar o `adb reverse`.
- **A reativação do overlay antigo pode aparecer ao desfazer.** A limpeza é uma operação que o próprio Windows desfaz; reiniciar o host resolve.
- **Não há ancoragem semântica.** O overlay pertence às coordenadas do monitor, não ao conteúdo que está na tela.
- **A minificação está desligada no release do Android**, o que deixa o APK maior.
- **Não existe `global.json`**, então o build não é fixado numa faixa exata de SDK do .NET. A CI usa `10.0.x`.
- **`docs/security.md` e `docs/architecture.md` estão atrás do estado real**, ainda descrevendo coisas que as etapas M2 e M6 já fizeram.
- **`docs/install-and-troubleshooting.md` manda abrir `Start-DeskInk.cmd`**, mas o pacote final de 0.1.0 não inclui esse arquivo: ele traz só o APK, o executável e o arquivo de leitura.
- **A documentação de arquitetura cita Direct2D e DirectComposition para o overlay**, mas o que o MVP usa é GDI+ com `UpdateLayeredWindow`. O Direct2D aparece só como candidato.
- **Há um problema de ambiente registrado**: a chamada `SendInput` falha dentro de um sandbox, embora o mesmo executável funcione na área de trabalho interativa.

### Funcionalidade ainda não implementada

Da fase 1.5 do plano:

- ensaios prolongados de 1, 4 e 8 horas, com medição de consumo, temperatura e pen-to-photon por câmera;
- perda física controlada em ensaio prolongado, eAmpliação do ciclo de vida de mouse e atalhos;
- latência e perda em tempo real na interface do host;
- identificador de IP do pareado, tela de configurações e diagnóstico, iniciar com o Windows, logs exportáveis e ícone próprio;
- calibração de área ativa, com perfis por monitor e orientação;
- curva de pressão, com modos suave, linear e firme;
- chave de release do Android, assinatura, changelog e versionamento.

Das fases seguintes: captura de tela, codificação por hardware, streaming de vídeo, WebRTC, previsão de tinta local, reconciliação e USB nativo sem ADB. Também está fora de escopo o suporte a Linux, registrado como trilha própria no plano.

O plano também deixa registrado, como restrição permanente, que colaboração em nuvem e backend web devem ser adiadas.

## Próximos passos

O `docs/mvp-plan.md` tem uma recomendação explícita, na ordem de prioridade: estabilidade e menor latência, depois experiência de uso diário, depois novos transportes e plataformas, depois vídeo e tela remota, e por fim recursos avançados de anotação. A regra escrita é que mais funcionalidade nunca justifica piorar latência, segurança ou recuperação após falhas.

A recomendação literal do projeto é começar pela fase 1.5, **não** por streaming de tela. O primeiro pacote pós-MVP deveria combinar ensaio prolongado, simulador e testes de formato, calibração de área ativa e uma interface Windows simples. Só depois disso o USB nativo e a fundação para Linux avançam em paralelo, e a captura de tela só deveria começar quando a linha de base de latência e estabilidade estiver protegida.

## Testes

Existe suíte automatizada, em três camadas. O comando que roda tudo:

```powershell
./scripts/test-all.ps1
```

Ele compila o Android, roda os testes unitários, compila o Windows, roda o executável de testes e por fim o simulador de entrada.

### Camada 1 — Android, JUnit 4

Vinte e sete testes em nove arquivos, cobrindo: detecção de toque com dois dedos, o motor de entrada da caneta, o publicador de pacotes, a janela móvel de tempo, o codec do protocolo v1, o protocolo de controle, a descoberta de LAN, o datagram autenticado da LAN e o transporte de entrada em memória.

### Camada 2 — Windows, executável próprio

Dezoito verificações em `DeskInk.Core.Tests`, sem xUnit nem NUnit. Cobrem configuração da fundação, decodificação e codificação do vetor de teste, quadros malformados, rastreamento de sequência, o *failsafe* de estado da caneta, o aperto de mão de controle, o enquadramento de fluxo TCP, o datagram autenticado da LAN, a identidade de pareamento e de servidor TLS, o discovery, a tradução de mouse, a de caneta sintética, o modelo de traço do overlay, a tradução de entrada do overlay, o mapeamento de coordenadas para a área de trabalho e uma carga sintética de 100 mil amostras.

### Camada 3 — Simulador de entrada ponta a ponta

Roda sem tablet. Reproduz onze cenários: normal, duplicata, reordenação, lacuna, evento ausente, vínculo inválido, sessão errada, versão inválida, quadro truncado, contagem de amostras inválida e timeout, mais 25 reconexões. Verifica linhas exatas da saída do host e exige exatamente 37 sessões fechadas.

```powershell
dotnet build apps/windows/DeskInk.sln --configuration Debug
./scripts/test-input-simulator.ps1
```

### Integração contínua

`.github/workflows/ci.yml` roda em `windows-latest`, em push e pull request, usando JDK 17, .NET 10 e o SDK Android com a Platform 36. A execução inteira é um passo: `./scripts/test-all.ps1`.

**Não há registro versionado do resultado da CI nem badge de status.**

Sobre o estado dos testes: o único resultado arquivado no projeto é de 14 de agosto de 2026, com zero falhas em nove suítes. As afirmações de sucesso nos documentos são registros do autor naquela data, e os arquivos de log ficam em pastas ignoradas pelo Git. Não é possível confirmar aqui que a suíte passa hoje.

## Licença

O projeto ainda **não tem arquivo de licença**. Os documentos não mencionam licença em lugar nenhum. Se for usar ou distribuir o código, é preciso escolher uma.
