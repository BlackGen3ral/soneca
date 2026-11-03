# Soneca

Um app simples, em português, para desligar o Windows na hora certa.

## Instalar

Baixe **Soneca-Setup.exe** em [Releases](https://github.com/BlackGen3ral/soneca/releases/latest). Windows 10/11, x64. Instalação por usuário, sem precisar instalar o .NET separadamente.

## Usar

- Escolha 15 min, 30 min, 1 hora, 2 horas ou um tempo de 1 minuto a 7 dias.
- Também é possível informar um horário de 24 horas (HH:mm); se ele já passou, vale para amanhã.
- Clique em **Agendar desligamento**. A contagem mostra o tempo restante.
- Para desistir, abra o app e clique em **Cancelar desligamento** antes do horário.
- Fechar o app mantém o agendamento no Agendador de Tarefas do Windows.

Mantenha a sessão do Windows conectada. O despertar da suspensão depende dos temporizadores de ativação e do hardware; o app não liga um computador desligado. Se o computador perder o horário, o desligamento não será executado atrasado. Salve seus arquivos: o app não força o fechamento de outros programas, que podem bloquear o desligamento. Ao desinstalar, o agendamento deste usuário é cancelado.

## Atualizações automáticas

Ao abrir sem agendamento ativo, o app consulta a versão estável mais recente deste repositório, baixa o instalador oficial, confere SHA-256, instala e reabre. Sem internet, o agendamento continua funcionando. Não há atualização durante uma contagem ativa. Há também um botão para verificar manualmente. A verificação de hash detecta corrupção; a confiança da atualização depende da segurança deste repositório e do HTTPS.

Melhorias futuras são distribuídas ao publicar uma nova tag `vMAJOR.MINOR.PATCH`. O workflow compila, executa testes sem desligar o PC, gera o instalador e publica os arquivos. Não há serviço de IA alterando o aplicativo sozinho: cada melhoria solicitada precisa ser implementada e publicada.

## Desenvolvimento

.NET SDK 10 e Inno Setup 6/7. `./scripts/build.ps1 -Version 1.0.0 -InnoCompiler 'C:\caminho\ISCC.exe'`

`Soneca.exe --demo` abre uma demonstração sem tarefas reais ou atualização. `--self-test` testa validação, contagem, origem e hashes do atualizador, e cria/remove uma tarefa inofensiva isolada (nunca executa shutdown). Resultado em `self-test-result.txt` junto do executável.

Os agendamentos usam uma tarefa por usuário e não alteram outras tarefas nem cancelam desligamentos de terceiros. O app funciona localmente; apenas consulta o GitHub para atualizar. O instalador ainda não possui assinatura digital comercial.
