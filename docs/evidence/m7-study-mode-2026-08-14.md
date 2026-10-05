# Evidência M7 — Study Mode

Data: 2026-08-14

Hardware validado: Samsung SM-X400 com S Pen, conectado ao host Windows DeskInk.

## Resultado físico

- Caneta e marca-texto selecionáveis no tablet: aprovado.
- Dedo como scroll e caneta sem promoção indevida de palma: aprovado nas etapas anteriores.
- Botão lateral como borracha no overlay: aprovado nas etapas anteriores.
- Toque rápido com exatamente dois dedos para desfazer: aprovado.
- Caneta + dedo não arma o gesto de dois dedos: protegido pela implementação.
- Configuração persistente do gesto entre desligado, desfazer e alternar ferramenta: aprovado.
- Toque com dois dedos alternando caneta/marca-texto: aprovado.
- `Limpar` remove o documento e seu histórico; desfazer normal percorre estados anteriores,
  inclusive operações de borracha: comportamento confirmado.

## Validação automatizada

- `assembleDebug`: aprovado.
- `testDebugUnitTest`: aprovado.
- Detector cobre toque rápido, duração excessiva, movimento excessivo, cancelamento e
  desarme após reconhecimento.

O APK foi instalado com sucesso no tablet e o fluxo foi aprovado pelo usuário no
dispositivo físico.
