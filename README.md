# Artho AI

Assistente pessoal para Windows, evoluído do projeto Artho Control (antigo Soneca). A primeira versão combina uma apresentação cinematográfica curta, um painel com dados reais deste computador e as ferramentas existentes de **Energia**, **Limpeza** e **Nomes da pasta**.

## Primeira versão

- Apresentação de cinco segundos, com animação própria, efeitos sonoros sintetizados pelo app e fala pelo serviço de voz do Windows quando disponível. O app procura uma voz masculina em português do Brasil; se não encontrar, usa uma voz masculina instalada. A apresentação pode ser pulada pelo botão.
- Início com o Windows ativado por padrão no primeiro uso. A preferência pode ser alterada em **Visão geral** e fica no perfil atual do Windows.
- Painel local lê nome do computador, versão do Windows, processadores lógicos, memória física em uso, tempo ligado e presença de uma interface de rede ativa.
- Computadores remotos, câmeras Intelbras e serviços da Super Print aparecem como **não configurados** até haver uma integração real. Esta versão ainda não os conecta, não envia Wake-on-LAN e não gera briefing comercial.
- As ferramentas que já existiam continuam disponíveis. O instalador mantém o identificador, executável e tarefa interna anteriores para migrar as instalações sem perder agendamentos.

Os limites entre os módulos estão em `LocalComputerAgent.cs` e `IntegrationContracts.cs`: agente Windows local, serviço remoto com configuração limitada a três PCs, interface Wake-on-LAN, provedor de briefing e integração de câmeras. Eles formam os pontos de extensão para um painel desktop/mobile compartilhado. As interfaces de rede, briefing e câmeras ainda não têm serviços configurados nesta versão.

## Instalar

Baixe [ArthoAI-Setup.exe](https://github.com/BlackGen3ral/soneca/releases/latest/download/ArthoAI-Setup.exe). Windows 10/11 x64; o instalador inclui o runtime. Instaladores legados continuam publicados para atualizar versões antigas.

## Como testar

- Abra o aplicativo instalado para ver a apresentação completa; clique em **Pular apresentação** para dispensá-la imediatamente.
- Em **Visão geral**, confira os dados deste computador, o estado de rede observado e os serviços marcados como não configurados. Ative ou desative **Iniciar o Artho AI com o Windows** para conferir a preferência do usuário atual.
- Para abrir sem animação pelo terminal: `Soneca.exe --skip-intro`.
- `Soneca.exe --demo` simula as ferramentas destrutivas; não executa atualização, desligamento nem exclusão real.
- `Soneca.exe --self-test` valida agendamento, atualizador e limpeza com dados isolados. Não aponta para as pastas reais Temp, Prefetch ou Lixeira.

## Ferramentas existentes

### Energia

Escolha atalhos de tempo, minutos personalizados até 7 dias ou um horário HH:mm. Clique em **Agendar desligamento**. É possível alterar ou cancelar; fechar o app mantém a tarefa no Windows.

Mantenha a sessão conectada. O despertar da suspensão depende do hardware e das configurações do Windows. Horários perdidos não são executados atrasados. Salve seus arquivos: programas abertos podem impedir o desligamento.

### Limpeza

1. Selecione %temp%, Prefetch e, opcionalmente, Lixeira (desmarcada por padrão).
2. Clique em **Analisar selecionados**. Essa etapa não remove arquivos.
3. Confira a estimativa e clique em **Limpar selecionados**. A confirmação lista as categorias; a remoção é permanente.

- **%temp%:** arquivos da pasta temporária do usuário e suas subpastas. Preserva pastas, arquivos em uso, arquivos alterados após a análise e links.
- **Prefetch:** somente arquivos .pf diretamente em Windows/Prefetch; preserva outras extensões e ReadyBoot. Um auxiliar limitado a esse destino solicita autorização de administrador ao limpar.
- **Lixeira:** API do Windows para o conteúdo do usuário em todas as unidades. Só é esvaziada quando selecionada e confirmada.

Nada é limpo ao abrir o aplicativo. A instalação continua por usuário, sem exigir execução permanente como administrador.

### Nomes da pasta

Escolha uma pasta ou cole seu caminho e clique em **Ler pasta**. A prévia mostra os nomes completos sem corte visual. A leitura não renomeia nem modifica arquivos e não envia a lista à internet.

## Atualizações e compatibilidade

Ao abrir sem agendamento nem limpeza ativa, o app consulta a última versão estável no GitHub, valida origem e SHA-256, instala e reabre. O novo ativo é `ArthoAI-Setup.exe`; `ArthoControl-Setup.exe` e `Soneca-Setup.exe` permanecem publicados para compatibilidade com instaladores e atualizadores anteriores.

## Desenvolvimento

Requer .NET SDK 10 e Inno Setup 6/7. Gere o instalador com `./scripts/build.ps1 -Version 1.3.0 -InnoCompiler "C:\caminho\ISCC.exe"`. O script publica para Windows x64, executa o autoteste isolado e grava os hashes dos três instaladores equivalentes.

O repositório e os identificadores técnicos ainda se chamam `soneca` para preservar atualizações existentes. A marca visível no produto é Artho AI.
