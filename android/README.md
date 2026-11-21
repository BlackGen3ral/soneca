# Artho AI para Android

## Parear com o Windows

No Artho AI para Windows 1.5.1 ou mais recente, abra **Celular → Gerar QR Code**. Com os dois aparelhos na mesma rede local, abra **Conectar ao PC → Escanear QR Code** no Android. Permita o uso da câmera e aponte para o QR da janela do Windows. O Android consulta o estado do PC enquanto a sessão estiver aberta. Instalações antigas da ponte ainda podem ser usadas pelo botão **Usar código da ponte antiga**.

## Atualizar o app

Instale a versão 0.3.0 uma vez para habilitar o atualizador. Ao abrir o app, ele verifica as versões do Android publicadas no GitHub. Se encontrar uma atualização, mostra **Baixar e instalar** em **Atualizações**. Na primeira vez, o Android pode pedir que você permita instalações a partir do Artho AI. Depois, volte ao app, toque no botão novamente e confirme a instalação.

## Publicar uma versão

1. Aumente `versionCode` e atualize `versionName` em `build.gradle`.
2. Compile com `gradle assembleRelease` usando JDK 17 e Android SDK 36. Assine o APK com a mesma chave usada na distribuição anterior e confira a assinatura com `apksigner verify --print-certs`. Guarde a chave fora do repositório.
3. Nomeie o arquivo como `ArthoAI-Android-X.Y.Z.apk`. Crie e envie a tag `android-vX.Y.Z` no commit da versão.
4. Publique o APK como pré-lançamento na tag Android. Por exemplo, `gh release create android-vX.Y.Z ArthoAI-Android-X.Y.Z.apk --verify-tag --prerelease --latest=false --title "Artho AI Android X.Y.Z" --notes "Notas da versão"`. O Windows usa a última versão estável do repositório.
5. Compare o SHA-256 do arquivo baixado com o original. No Android com uma versão anterior do atualizador, abra o app, baixe a atualização e confirme a instalação.

O app consulta apenas versões Android do repositório oficial e verifica o SHA-256 informado pelo GitHub, o identificador do pacote, a versão e a assinatura. A distribuição consulta o GitHub sem autenticação e precisa de releases publicamente acessíveis.
