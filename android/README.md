# Artho AI para Android

## Parear com o Windows

No Artho AI para Windows, abra **Celular → Gerar QR Code**. Com os dois aparelhos na mesma rede local, abra **Conectar ao PC → Escanear QR Code** no Android. A partir do Windows 1.11.0 e do Android 0.8.0, um novo pareamento fica salvo e é retomado quando o app do PC voltar a abrir no mesmo endereço da rede, inclusive após atualizações. Instalações antigas da ponte ainda oferecem conexão temporária pelo botão **Usar código da ponte antiga**.

No Windows 1.6.0 ou mais recente, pareie os PCs na aba **Dispositivos**. Na aba **Celular**, marque **Permitir que o celular ligue os PCs e edite os horários** e gere um novo QR Code. Após parear, abra **Dispositivos** no Android para consultar o último contato, enviar Wake-on-LAN ou escolher os horários e dias de ligar e desligar. As rotinas são salvas e executadas no PC de controle.

## Energia deste PC

No Windows 1.8.0 ou mais recente, marque **Permitir que o celular desligue, reinicie e programe este PC** antes de gerar o QR Code. Escaneie e abra **Energia** no Android. O desligamento e a reinicialização imediatos pedem confirmação. O desligamento pode ser único ou semanal. O agendamento fica no Windows.

Para ligar, o Android envia Wake-on-LAN pelo Wi-Fi local usando o endereço salvo do PC. **Programar para ligar** oferece um horário único ou dias da semana e depende da autorização de alarmes exatos. Mantenha o celular ligado no mesmo Wi-Fi no horário desejado. Use **Esquecer este PC** para remover o endereço e cancelar o alarme de ligar no celular.

## Tela remota

No Windows, libere o acesso em **Celular → Tela remota**. Abra **Tela remota** no Android. Incline o joystick para mover o cursor. Incline mais para acelerar. Ao lado dele ficam os cliques, a rolagem e o teclado. Você também pode tocar e arrastar na própria imagem. Gire o celular para colocar os controles ao lado da tela.

## Atualizar o app

O Artho AI consulta o GitHub ao abrir. Toque em **Atualizar** no menu inferior para conferir a versão instalada e a disponível. Use **Baixar atualização** para acompanhar o progresso. Depois de verificado o APK, toque em **Instalar atualização**. Se o Android solicitar permissão para instalar a partir do Artho AI, habilite a permissão, volte ao app e confirme a instalação.

## Publicar uma versão

1. Aumente `versionCode` e atualize `versionName` em `build.gradle`.
2. Compile com `gradle assembleRelease` usando JDK 17 e Android SDK 36. Assine o APK com a mesma chave usada na distribuição anterior e confira a assinatura com `apksigner verify --print-certs`. Guarde a chave fora do repositório.
3. Nomeie o arquivo como `ArthoAI-Android-X.Y.Z.apk`. Crie e envie a tag `android-vX.Y.Z` no commit da versão.
4. Publique o APK como pré-lançamento na tag Android. Por exemplo, `gh release create android-vX.Y.Z ArthoAI-Android-X.Y.Z.apk --verify-tag --prerelease --latest=false --title "Artho AI Android X.Y.Z" --notes "Notas da versão"`. O Windows usa a última versão estável do repositório.
5. Compare o SHA-256 do arquivo baixado com o original. No Android com uma versão anterior do atualizador, abra o app, baixe a atualização e confirme a instalação.

O app consulta apenas versões Android do repositório oficial e verifica o SHA-256 informado pelo GitHub, o identificador do pacote, a versão e a assinatura. A distribuição consulta o GitHub sem autenticação e precisa de releases publicamente acessíveis.
