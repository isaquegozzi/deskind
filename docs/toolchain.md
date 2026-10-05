# Toolchain

Inventário executado em Windows x64 em 2026-08-13.

## Requerido

- Git 2.x.
- JDK 17 (compatível com o Android Gradle Plugin selecionado).
- Android SDK Platform 36, Build Tools 36.x e Platform Tools atuais.
- Gradle Wrapper 9.4.1 e Android Gradle Plugin 9.2.0.
- Kotlin integrado ao Android Gradle Plugin 9.2 (não aplicar o plugin Kotlin
  Android legado).
- .NET 10 LTS SDK x64 (feature band suportada).
- Windows 11 SDK 10.0.26100 ou mais recente disponível no canal estável.
- ADB com device serial explícito quando houver mais de um alvo.

AGP 9.2 requer no mínimo Gradle 9.4.1 segundo a
[documentação oficial](https://developer.android.com/build/releases/about-agp).
Android 16 usa compile/target SDK 36 conforme o
[guia oficial](https://developer.android.com/about/versions/16/setup-sdk).
.NET 10 é LTS até novembro de 2028 conforme a
[política oficial](https://learn.microsoft.com/dotnet/core/releases-and-support).

## Detectado nesta máquina

| Item | Resultado |
| --- | --- |
| Git | 2.55.0.windows.3 |
| Repositório Git | ausente no início da tarefa |
| ADB | 1.0.41 / 31.0.3 em `C:\ADB\adb.exe` |
| Device | `RX2Y9004VHF`, `SM_X400`, estado `device` |
| Android SDK root | Platform 36 r2, Build Tools 36.0.0, Platform Tools e Command-line Tools 15859902 instalados |
| JDK | Temurin 17.0.20.8 instalado; script descobre a instalação se `JAVA_HOME` estiver vazio |
| Gradle | wrapper 9.4.1 versionado; Gradle global desnecessário |
| .NET | SDK 10.0.400 e runtime 10 instalados; runtime 9.0.10 também presente |
| Windows SDK | standalone não detectado; o targeting pack do .NET foi suficiente para o build M0 |

Os builds M0 foram executados com sucesso. Um Windows SDK standalone só será
obrigatório se o protótipo Win32 de M3/M4 demonstrar necessidade de headers/tools
fora do targeting pack do .NET.
