# Artho AI

Assistente pessoal para Windows, evoluído do projeto Artho Control (antigo Soneca). A primeira versão combina uma apresentação cinematográfica curta, um painel com dados reais deste computador e as ferramentas existentes de **Energia**, **Limpeza** e **Nomes da pasta**.

## Abertura

- Abertura em HTML de 5,4 segundos com partículas, anéis de HUD e efeitos sonoros. O HTML e o áudio ficam no executável. O WebView2 Runtime renderiza a abertura dentro do app. Se estiver indisponível, o painel abre diretamente.
- Início com o Windows ativado por padrão no primeiro uso. A preferência pode ser alterada em **Visão geral** e fica no perfil atual do Windows.
- Painel local lê nome do computador, versão do Windows, processadores lógicos, memória física em uso, tempo ligado e presença de uma interface de rede ativa.
- **Dispositivos** monitora até três PCs da rede local e oferece Wake-on-LAN e rotinas semanais. Câmeras Intelbras e Super Print continuam sem integração configurada.
- As ferramentas que já existiam continuam disponíveis. O instalador mantém o identificador, executável e tarefa interna anteriores para migrar as instalações sem perder agendamentos.

Os limites entre os módulos estão em `LocalComputerAgent.cs` e `IntegrationContracts.cs`: agente Windows local, serviço remoto com configuração limitada a três PCs, interface Wake-on-LAN, provedor de briefing e integração de câmeras. Eles formam os pontos de extensão para um painel desktop/mobile compartilhado. As interfaces de rede, briefing e câmeras ainda não têm serviços configurados nesta versão.

## Instalar

Baixe [ArthoAI-Setup.exe](https://github.com/BlackGen3ral/soneca/releases/latest/download/ArthoAI-Setup.exe). Windows 10/11 x64; o instalador inclui o runtime. Instaladores legados continuam publicados para atualizar versões antigas.

## Como testar

- Abra o aplicativo instalado para ver a abertura. Use **Pular abertura** ou **Esc** para ir ao painel. Em **Visão geral**, use **Rever abertura** para repeti-la.
- Em **Visão geral**, confira os dados do computador e da rede, atualizados enquanto o painel estiver visível. Em **Preferências**, ajuste o início com o Windows ou verifique atualizações.
- Para abrir sem animação pelo terminal: `Soneca.exe --skip-intro`.
- `Soneca.exe --demo` simula as ferramentas destrutivas; não executa atualização, desligamento nem exclusão real.
- `Soneca.exe --self-test` valida agendamento, atualizador e limpeza com dados isolados. Não aponta para as pastas reais Temp, Prefetch ou Lixeira.

## Ferramentas existentes

### Celular

Conecte o PC e o celular à mesma rede local. No Windows, abra **Celular**, selecione o endereço do PC nessa rede e clique em **Gerar QR Code**. No Artho AI para Android, abra **Conectar ao PC → Escanear QR Code** e aponte para o código.

Se o Windows solicitar acesso pelo firewall, permita somente na sua rede privada. O QR Code expira em três minutos se o celular ainda não tiver concluído o pareamento. O Android pode consultar os dados do PC até que você encerre a conexão ou feche o Artho AI para Windows.

Para gerenciar outros PCs do Android, marque **Permitir que o celular ligue os PCs e edite os horários** antes de gerar o QR Code. Depois do pareamento, abra **Dispositivos** no Android. Essa permissão vale apenas para a sessão atual.

Para o celular controlar a energia deste PC, marque **Permitir que o celular desligue, reinicie e programe este PC** antes de gerar outro QR Code. Em **Energia** no Android, desligue ou reinicie com confirmação e programe um desligamento único ou semanal. As rotinas de desligamento ficam no Windows e o usuário precisa estar conectado no horário escolhido. O painel do Windows pode estar fechado depois que o horário for salvo.

O Android guarda o endereço do equipamento para que **Ligar agora** e **Programar para ligar** funcionem também com o PC desligado. Para programar, autorize alarmes exatos no Android. O celular precisa estar ligado e no mesmo Wi-Fi no momento do disparo. O PC precisa aceitar Wake-on-LAN. Depois de fechar ou reiniciar o Artho AI no Windows, gere outro QR Code para uma nova sessão de controle.

### Dispositivos

1. Instale o Artho AI 1.6.0 em cada PC e escolha qual ficará como PC de controle.
2. No PC que será controlado, abra **Dispositivos → Permitir controle deste PC**. Selecione o endereço da rede local e clique em **Gerar e copiar código**. Transfira o código ao PC de controle por um meio de confiança. O código vale por três minutos e só pode ser usado uma vez.
3. No PC de controle, cole o código em **Dispositivos → Adicionar outro PC** e clique em **Parear PC**. Se o Windows solicitar acesso pelo firewall no PC controlado, permita na rede privada. **Sem resposta** significa que o serviço daquele PC não respondeu. A tela preserva a hora da última resposta bem-sucedida.
4. Use **Ligar agora** para enviar o sinal Wake-on-LAN ou **Editar horários** para marcar os dias e as horas de ligar e desligar. Cada ação tem seus próprios dias. Para cancelar, desmarque a ação e salve.

As rotinas rodam pelo Agendador do Windows no **PC de controle**, no horário local dele. Ele precisa estar ligado, acordado, com o usuário conectado e na mesma rede. O painel pode estar fechado. O desligamento depende de o Artho AI estar aberto no PC controlado. O Wake-on-LAN precisa estar habilitado e funcionar na placa de rede do PC controlado. Horários perdidos são ignorados. Para revogar o controle, use **Desativar controle** no PC controlado. Se o IP do PC mudar, selecione o endereço atual e pareie novamente.

### Energia

Escolha atalhos de tempo, minutos personalizados até 7 dias ou um horário HH:mm. Clique em **Agendar desligamento**. É possível alterar ou cancelar; fechar o app mantém a tarefa no Windows.

Mantenha a sessão conectada. O despertar da suspensão depende do hardware e das configurações do Windows. Horários perdidos não são executados atrasados. Salve seus arquivos: programas abertos podem impedir o desligamento.

### Limpeza

1. Selecione as categorias que deseja conferir. Os temporários do usuário com mais de um dia vêm selecionados por padrão.
2. Clique em **Analisar**. Confira os totais por categoria e expanda a lista para ver até 15 dos maiores arquivos elegíveis.
3. Clique em **Limpar** e confirme a remoção. O resultado informa quantos arquivos foram removidos ou ignorados. A análise e a limpeza de arquivos locais podem ser interrompidas entre arquivos.

- **%temp%** inclui arquivos da pasta temporária do usuário e de suas subpastas. O filtro aceita 1, 7 ou 30 dias ou qualquer idade. A limpeza preserva pastas, links e arquivos em uso, modificados ou substituídos depois da análise.
- **CrashDumps** inclui apenas arquivos .dmp diretamente na pasta padrão de dumps do usuário. Mantenha esses arquivos se estiver investigando uma falha.
- **Prefetch** inclui apenas arquivos .pf diretamente em Windows/Prefetch. Quando necessário, o auxiliar solicita autorização separada para analisar e limpar, usando a prévia na limpeza.
- **Lixeira** usa o esvaziamento do Windows para todas as unidades e só entra na limpeza quando selecionada e confirmada. O app refaz a consulta e pede uma nova análise se a contagem ou o volume mudar.

Nada é limpo ao abrir o aplicativo. A instalação continua por usuário, sem exigir execução permanente como administrador.

### Nomes da pasta

Escolha uma pasta ou cole seu caminho e clique em **Ler pasta**. A prévia mostra os nomes completos sem corte visual. A leitura não renomeia nem modifica arquivos e não envia a lista à internet.

## Atualizações e compatibilidade

O app verifica a última versão estável no GitHub ao abrir. Em **Atualizações**, confira a versão instalada, o tamanho da versão disponível e a página do GitHub. Clique em **Baixar atualização** e, depois da verificação de integridade, em **Instalar e reabrir**. A instalação espera se houver um desligamento agendado ou uma ferramenta em uso.

O ativo principal é `ArthoAI-Setup.exe`. `ArthoControl-Setup.exe` e `Soneca-Setup.exe` permanecem publicados para compatibilidade com instaladores e atualizadores anteriores.

## Desenvolvimento

Requer .NET SDK 10 e Inno Setup 6/7. Gere o instalador com `./scripts/build.ps1 -Version 1.10.0 -InnoCompiler "C:\caminho\ISCC.exe"`. O script publica para Windows x64, executa o autoteste isolado e grava os hashes dos três instaladores equivalentes.

O repositório e os identificadores técnicos ainda se chamam `soneca` para preservar atualizações existentes. A marca visível no produto é Artho AI.
