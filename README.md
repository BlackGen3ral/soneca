# Artho Control

Sua central pessoal de ferramentas para Windows. O antigo Soneca reúne **Energia** e **Limpeza**, preservando a instalação e o atualizador.

## Instalar

Baixe [ArthoControl-Setup.exe](https://github.com/BlackGen3ral/soneca/releases/latest/download/ArthoControl-Setup.exe). Windows 10/11 x64, .NET incluído. Quem tem o Soneca recebe a mudança pelo atualizador. Os identificadores internos anteriores permanecem para preservar a atualização e os agendamentos.

## Energia

Escolha atalhos de tempo, minutos personalizados até 7 dias ou um horário HH:mm. Clique em Agendar desligamento. É possível alterar ou cancelar; fechar o app mantém a tarefa no Windows.

Mantenha a sessão conectada. O despertar da suspensão depende do hardware e das configurações do Windows. Horários perdidos não são executados atrasados. Salve seus arquivos: programas abertos podem impedir o desligamento. A desinstalação cancela o agendamento do usuário.

## Limpeza

1. Selecione %temp%, Prefetch e, opcionalmente, Lixeira (desmarcada por padrão).
2. Clique em Analisar selecionados. Essa etapa não remove arquivos.
3. Confira a estimativa e clique em Limpar selecionados. A confirmação lista as categorias; a remoção é permanente.

- **%temp%:** arquivos da pasta temporária do usuário e suas subpastas. Preserva pastas, arquivos em uso, arquivos alterados após a análise e links. A variável Temp deve apontar para uma pasta temporária reconhecível, sem redirecionamentos.
- **Prefetch:** somente arquivos .pf diretamente em Windows/Prefetch; preserva outras extensões e ReadyBoot. Um auxiliar limitado a esse destino solicita autorização de administrador ao limpar. Se não for possível analisar sem elevação, a interface indica prévia parcial; a análise ocorre após autorizar. O Windows recria o cache; limpar não garante mais velocidade.
- **Lixeira:** API do Windows para o conteúdo do usuário em todas as unidades. Só é esvaziada quando selecionada e confirmada.

O resultado mostra quantidade, tamanho e itens ignorados. Nada é limpo ao abrir o aplicativo. Negar a autorização do Prefetch preserva essa categoria e aparece no resultado. A instalação continua por usuário, sem exigir execução permanente como administrador.

## Atualizações

Ao abrir sem agendamento nem limpeza ativa, o app consulta a última versão estável no GitHub, valida origem e SHA-256, instala e reabre. Também há verificação manual. Soneca-Setup.exe permanece publicado para os atualizadores antigos, com o mesmo conteúdo de ArthoControl-Setup.exe.

Melhorias solicitadas são implementadas e publicadas com uma tag vMAJOR.MINOR.PATCH; o workflow compila, testa, empacota e publica instaladores e hashes. Não há IA alterando o app sozinha.

## Desenvolvimento e testes

.NET SDK 10 e Inno Setup 6/7. Execute scripts/build.ps1 -Version 1.1.0 com o caminho do compilador em -InnoCompiler.

Soneca.exe --demo simula todas as operações, sem atualização, desligamento ou exclusão real.

Soneca.exe --self-test valida agendamento, atualizador e limpeza numa pasta isolada. Remove somente arquivos criados pelo teste. Verifica arquivos em uso, alteração após análise, limites de destino, extensão .pf, junctions e consulta somente leitura da lixeira. A tarefa do Windows de teste é inofensiva. Resultado em self-test-result.txt junto do executável.

O instalador ainda não possui assinatura digital comercial. Dados pessoais não são enviados; somente o atualizador consulta o GitHub.
