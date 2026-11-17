# Badge Smith

Farma cartas colecionáveis da Steam e desbloqueia ou bloqueia conquistas, para Windows e Linux.

> Em desenvolvimento: o app desktop já faz login, farm de cartas e edição de conquistas; os pacotes de instalação vêm
> nas próximas etapas.

## Como funciona

- **Steam aberta**: o Badge Smith usa a Steam já instalada e logada. Ele carrega o `steamclient` da própria instalação, então não distribui nenhuma biblioteca da Valve.
- **"Jogar" um jogo**: para cada jogo, um processo auxiliar (`BadgeSmith.Agent`) define o `SteamAppId` e se conecta à Steam, que passa a mostrar você jogando e a contar o tempo de jogo.
- **Login**: por QR code aprovado no app Steam do celular, pela API de autenticação da Steam, ou colando o cookie
  `steamLoginSecure` de uma sessão aberta no navegador. A sessão fica no cofre do sistema (Gerenciador de Credenciais no
  Windows, Secret Service no Linux). O QR gera um token que o app renova sozinho; o cookie colado vale por cerca de um
  dia e o app pede outro quando ele expira.
- **Cartas restantes**: lidas da sua página de badges com o cookie da sessão.
- **Fila de farm**: primeiro os jogos com menos de 2 h rodam juntos (até 30) para acumular tempo; depois os demais rodam um por vez, em ciclos de 15 min (5 min na última carta), relendo as badges a cada ciclo.
- **Conquistas**: os nomes, descrições e ícones vêm do schema que a Steam guarda em `appcache/stats`; o estado de cada
  conquista é lido e gravado pela Steam. Conquistas protegidas (que só o servidor do jogo pode alterar) não são
  tocadas.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Steam instalada, aberta e logada na mesma conta do login do Badge Smith
- No Linux, um cofre compatível com o Secret Service (GNOME Keyring ou KWallet) e a `libsecret`

## Rodar

```bash
dotnet build BadgeSmith.slnx
dotnet run --project src/BadgeSmith.Desktop        # app desktop
```

O app tem três abas:

- **Cartas**: jogos com cartas a dropar, horas jogadas e status de cada um; iniciar, pausar, retomar e parar o farm;
  marcar jogos para ignorar.
- **Conquistas**: biblioteca da conta com busca; marcar e desmarcar conquistas e gravar na Steam.
- **Configurações**: horas antes de dropar, jogos ao mesmo tempo e tema claro, escuro ou do sistema. Ficam em
  `settings.json`, na pasta `BadgeSmith` dentro de `~/.config` (Linux) ou `%APPDATA%` (Windows).

A linha de comando continua disponível:

```bash
dotnet run --project src/BadgeSmith.Cli -- login   # QR code no terminal
dotnet run --project src/BadgeSmith.Cli -- scan    # lista os jogos com cartas a dropar
dotnet run --project src/BadgeSmith.Cli -- farm    # farma até acabar (Ctrl+C para parar)
dotnet run --project src/BadgeSmith.Cli -- logout  # encerra a sessão na Steam e apaga do cofre
```

Conquistas de um jogo, pelo agente (a saída é uma linha JSON por conquista):

```bash
dotnet run --project src/BadgeSmith.Agent -- <appId> achievements list --language brazilian
dotnet run --project src/BadgeSmith.Agent -- <appId> achievements unlock <API_NAME> [...]
dotnet run --project src/BadgeSmith.Agent -- <appId> achievements lock <API_NAME> [...]
```

Para desenvolvimento, dá para pular o login e usar um cookie `steamLoginSecure` copiado do navegador: copie o
`.env.example` para `.env` e preencha o valor. Quando ele está definido, o cofre não é usado. O `.env` fica fora do git.

## Desenvolvimento

```bash
dotnet test BadgeSmith.slnx
```

Fluxo de branches:

- `main`: versões entregues. Recebe PR só da `develop`, a cada entrega.
- `develop`: integração. Recebe PR das branches de feature.
- `feature/<nome-da-feature>`: uma branch por feature, criada a partir da `develop`.

## Estrutura do projeto

```
Badge_Smith/
├── src/
│   ├── BadgeSmith.Agent/    # processo auxiliar por jogo: joga o appId e lê ou grava as conquistas
│   ├── BadgeSmith.Core/     # fila de farm, agentes, conquistas e configurações
│   ├── BadgeSmith.Steam/    # login, cofre da sessão e leitura da página de badges
│   ├── BadgeSmith.Desktop/  # app desktop em Avalonia: login, cartas, conquistas e configurações
│   └── BadgeSmith.Cli/      # linha de comando (login, scan e farm)
└── tests/BadgeSmith.Tests/  # testes do farm, do login, das badges, das conquistas e das telas
```

## Aviso

Projeto pessoal, sem afiliação com a Valve. Farmar cartas e alterar conquistas não é autorizado pela Valve e pode
ferir os termos de uso da Steam: use por sua conta e risco. Steam é marca da Valve Corporation.

## Licença

[MIT](LICENSE)
