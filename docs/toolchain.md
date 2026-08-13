# Toolchain

Inventário executado em Windows x64 em 2026-08-13.

## Requerido

- Git 2.x.
- JDK 17 (compatível com o Android Gradle Plugin selecionado).
- Android SDK Platform 36, Build Tools 36.x e Platform Tools atuais.
- Gradle Wrapper 9.4.1 e Android Gradle Plugin 9.2.0.
- Kotlin 2.3.21.
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
| Android SDK root | diretório existe em `C:\Users\Isaque\AppData\Local\Android\Sdk`, sem pacotes detectados |
| JDK / JAVA_HOME | não detectado |
| Gradle global | não detectado; o projeto usará wrapper |
| .NET | runtime 9.0.10 x64; nenhum SDK detectado |
| Windows SDK | não detectado por diretório/registro |

Enquanto os itens ausentes permanecerem, builds Android/Windows não podem ser
considerados gates satisfeitos. Os scripts devem falhar com mensagens acionáveis.
