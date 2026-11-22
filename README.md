# Roblox Multi+

**[Baixar para Windows](https://github.com/jaysloncarvalho/Roblox-Multi-Plus/releases/latest)**

Baixe o ZIP do Windows de 64 bits, extraia todos os arquivos e abra **Roblox Multi+.exe**. Mantenha as DLLs na mesma pasta do executável.

Gerenciador de contas para Windows. Esta edição pública organiza contas, abre o cliente oficial do Roblox, acompanha sessões confirmadas e oferece reconexão opcional e Discord Rich Presence. Projeto independente, sem afiliação com Roblox ou Discord.

A edição pública depende das limitações do cliente oficial para abrir sessões. Ela não inclui mecanismo para forçar várias instâncias simultâneas.

O login integrado requer o [Runtime Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/).

## Contas e privacidade

- Em **Adicionar conta → Entrar com o Roblox**, o launcher abre o login oficial no WebView2 com um perfil novo separado do navegador pessoal. Quando o Roblox cria a sessão, o app verifica a identidade da conta e guarda o cookie autenticado no cofre, com o seu consentimento. O login manual por cookie continua disponível.
- A janela restringe a navegação principal aos domínios de login do Roblox e desativa ferramentas de desenvolvedor, preenchimento automático, salvamento de senhas e extensões. O perfil é apagado após concluir ou cancelar. Fechar o processo à força ou perder energia pode deixar arquivos temporários em `WebViewLogin` na pasta de dados.
- O cookie é uma credencial que dá acesso à sessão. Nunca compartilhe esse valor ou publique arquivos da pasta de dados. Métodos que abrem janelas externas ou exigem navegar para outro domínio podem não funcionar nessa janela.
- O cofre e o backup são criptografados com DPAPI no escopo do usuário do Windows. Isso protege os arquivos em repouso. Programas que rodam com a mesma identidade do Windows podem conseguir acessar esses dados, e o launcher precisa descriptografar o cookie enquanto o usa.
- O login não salva a senha do Roblox. As requisições que levam o cookie usam endpoints HTTPS do Roblox e não seguem redirecionamentos. O Discord Rich Presence é opcional e começa desligado.
- Os dados ficam em `%LOCALAPPDATA%\Roblox Multi by Jayy\Public`. O arquivo `log.txt` pode incluir nomes de contas e identificadores de partidas. Revise e remova dados pessoais antes de compartilhar qualquer trecho.
- Para migrar da edição anterior, coloque o executável público junto do cofre protegido antigo e abra com a mesma conta do Windows. A edição pública grava e verifica o cofre local e o backup, preservando os arquivos de origem. Arquivos JSON antigos sem criptografia não são importados.
- Para ajustar as preferências iniciais manualmente, copie `RAMSettings.example.ini` para a pasta de dados com o nome `RAMSettings.ini`. Você também pode usar a tela de configurações na primeira execução.
- Se suspeitar de exposição do cookie, encerre a sessão afetada nos ajustes de segurança do Roblox. Remover a conta da lista apaga o vínculo no launcher, mas não revoga a sessão no serviço.

## Senha mestra

Em **Configurações → Segurança → Ativar senha mestra**, escolha uma senha exclusiva com pelo menos 12 caracteres. O launcher protege a cópia principal e o backup após verificar o novo formato. O ícone de cadeado permite bloquear na hora. O cofre também bloqueia depois de cinco minutos sem interação na janela principal e pede a senha ao abrir o launcher.

O formato usa DPAPI e, no modo com senha, acrescenta AES-256-CBC autenticado por HMAC-SHA-256. As chaves de cifra e autenticação são derivadas com PBKDF2-HMAC-SHA-256 e sal aleatório. A senha não é gravada. O backup automático continua vinculado ao seu usuário do Windows. Cópias antigas do cofre guardadas em outras pastas conservam a proteção anterior.

O bloqueio limpa as referências às credenciais nas contas e as chaves mantidas pelo cofre. O launcher pausa novos lançamentos e reconexões. Partidas abertas continuam rodando. Um pedido de autenticação que já saiu pode terminar, mas seu resultado não é usado para abrir o jogo depois do bloqueio. O recurso protege os dados em repouso e o acesso casual à interface. Ele não protege contra software malicioso no seu usuário enquanto o cofre está aberto.

A filtragem remove formatos conhecidos de credenciais das novas mensagens e da lista de logs carregada. O arquivo de log antigo em disco não é reescrito; revise-o antes de compartilhar.

### Troca de senha e recuperação

- **Trocar senha** pede a senha mestra atual. A troca invalida o código de recuperação anterior.
- **Gerar código** pede a senha mestra e exibe o código uma única vez. Anote-o em um local seguro separado do cofre. No bloqueio, **Usar código de recuperação** permite definir uma nova senha. O código deixa de funcionar depois que a nova senha é gravada. Esse mecanismo depende do cofre local e da mesma conta do Windows. Sem a senha e o código, não é possível abrir o cofre protegido.
- **Logs → Exportar diagnóstico** exporta os eventos que estão na lista após os filtros, com remoção de linhas sensíveis e anonimização de nomes conhecidos, endereços IP, caminhos pessoais e identificadores de conta ou sessão. A filtragem é por padrões, então revise o relatório antes de compartilhar.

Licença em [LICENSE](LICENSE).