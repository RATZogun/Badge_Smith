# Badge Smith

Farma cartas colecionáveis da Steam e desbloqueia ou bloqueia conquistas, para Windows e Linux.

> Em desenvolvimento: hoje existe o protótipo do farm de cartas pela linha de comando. A interface gráfica e as
> conquistas vêm nas próximas etapas.

## Como funciona

- **Steam aberta**: o Badge Smith usa a Steam já instalada e logada. Ele carrega o `steamclient` da própria instalação, então não distribui nenhuma biblioteca da Valve nem pede a sua senha.
- **"Jogar" um jogo**: para cada jogo, um processo auxiliar (`BadgeSmith.Agent`) define o `SteamAppId` e se conecta à Steam, que passa a mostrar você jogando e a contar o tempo de jogo.
- **Cartas restantes**: lidas da sua página de badges com o cookie `steamLoginSecure` da sua sessão.
- **Fila de farm**: jogos com menos de 2 h rodam juntos (até 30) para acumular tempo; os demais rodam um por vez, em ciclos de 15 min (5 min na última carta), relendo as badges a cada ciclo.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Steam instalada, aberta e logada

## Rodar o protótipo

```bash
cp .env.example .env    # e preencha BADGESMITH_STEAM_LOGIN_SECURE
dotnet build BadgeSmith.slnx
dotnet run --project src/BadgeSmith.Cli -- scan    # lista os jogos com cartas a dropar
dotnet run --project src/BadgeSmith.Cli -- farm    # farma até acabar (Ctrl+C para parar)
```

O valor do cookie `steamLoginSecure` fica nas ferramentas de desenvolvedor do navegador, em steamcommunity.com, logado
na sua conta. O `.env` fica fora do git.

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
│   ├── BadgeSmith.Agent/    # processo auxiliar por jogo: conecta à Steam com o SteamAppId
│   ├── BadgeSmith.Core/     # fila de farm, planejamento e controle dos agentes
│   ├── BadgeSmith.Steam/    # sessão web e leitura da página de badges
│   └── BadgeSmith.Cli/      # linha de comando provisória (scan e farm)
└── tests/BadgeSmith.Tests/  # testes do planejamento e da leitura das badges
```

## Aviso

Projeto pessoal, sem afiliação com a Valve. Farmar cartas e alterar conquistas não é autorizado pela Valve e pode
ferir os termos de uso da Steam: use por sua conta e risco. Steam é marca da Valve Corporation.

## Licença

[MIT](LICENSE)
