# Instalação e uso do DeskInk 0.1.0

Este é um pacote MVP para Windows 10/11 x64 e Android 8 ou mais recente. O APK é
assinado para instalação direta e ainda não é uma versão de loja.

## Instalar

1. Extraia todo o ZIP para uma pasta no PC.
2. No tablet, abra `DeskInk-Android-0.1.0.apk` e autorize a instalação desta fonte
   quando o Android solicitar.
3. No PC, abra `Start-DeskInk.cmd` e mantenha a janela aberta.
4. Se o Firewall do Windows perguntar, permita o DeskInk em **redes privadas**.

## Conectar por LAN

1. Deixe PC e tablet na mesma rede Wi-Fi.
2. No app, toque em **Conexão** e em **Buscar PCs DeskInk na rede**.
3. Escolha o PC, confira o código exibido na janela do host e digite-o no tablet.
4. Toque em **Conectar LAN**. Nas próximas conexões o certificado confiável fica
   salvo e o código não é solicitado novamente.

Se a busca não encontrar o PC, informe manualmente o IPv4 do PC. O DeskInk usa
TCP 27185 e UDP 27186–27187 somente na rede local.

## Conectar por USB/ADB

O USB do MVP ainda usa o modo de desenvolvedor do Android:

1. Instale o Android SDK Platform Tools e confirme que `adb` está no `PATH`.
2. Ative **Opções do desenvolvedor > Depuração USB** no tablet.
3. Conecte o cabo, aceite a autorização RSA e abra `Start-DeskInk.cmd` novamente.
4. No app, toque em **Conexão > Conectar USB**.

O iniciador aplica automaticamente os dois `adb reverse` quando há exatamente um
tablet autorizado. Não use `adb forward`: a direção correta é do Android para os
listeners locais do Windows.

## Controles

- **Cursor** move, clica e arrasta; **Scroll** mantém a rolagem pela caneta ativa.
- **Caneta** usa a caneta sintética do Windows com pressão.
- **Marca** e **Apagar** operam no overlay global.
- **Monitor** alterna o alvo quando o Windows tem mais de uma tela.
- **Atalhos** oferece copiar, colar, buscar, página e mídia.
- Toque no status abaixo de “DeskInk” para abrir o diagnóstico.

## Solução de problemas

**Conecta, mas a caneta não move nada**

- Feche e abra `Start-DeskInk.cmd`, depois reconecte no app.
- Em LAN, desligue temporariamente VPN e confirme que ambos estão na mesma rede.
- Se o watchdog tiver pausado a entrada, levantar a caneta e reconectar neutraliza
  o estado antes de retomar.

**Erro `failed to connect to /IP (port 27185)`**

- O host não está acessível: confira o IP, mantenha a janela aberta e permita o
  executável no Firewall para rede privada.

**USB não aparece**

- Execute `adb devices -l`; o estado deve ser `device`, não `unauthorized`.
- Reconecte o cabo e aceite novamente a chave RSA no tablet.

**O ponteiro foi para a tela errada**

- Use **Monitor** no app. O mapeamento cobre toda a tela escolhida; proporções
  diferentes entre tablet e monitor podem produzir leve estiramento neste MVP.

**Overlay antigo volta ao desfazer**

- **Limpar** cria uma operação que pode ser desfeita. Para iniciar uma sessão sem
  histórico, reinicie o host.
