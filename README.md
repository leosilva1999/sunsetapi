# Sunsetsss API

API .NET para o **Sunsetsss** — plataforma onde usuários pesquisam locais com as mais bonitas
visões de pôr do sol, postam fotos marcando o local, curtem, comentam e avaliam os locais para
gerar rankings. Inclui um sistema de moderação completo (papéis, denúncias, exclusão de
conteúdo, termos de uso e política de privacidade versionados).

## Stack

- **.NET 9** (ASP.NET Core Web API)
- **Entity Framework Core** + **MySQL** (via [Pomelo.EntityFrameworkCore.MySql](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql))
- **FluentValidation** para validação de requests
- **JWT** (Bearer token) para autenticação
- **S3** (ou compatível) para armazenamento de imagens, via upload direto com URL pré-assinada — emulado localmente com **LocalStack**
- **MailKit** para envio de e-mail via SMTP (redefinição de senha) — emulado localmente com **Mailpit**
- **xUnit** + **Moq** para testes unitários, **Testcontainers** para testes de integração contra um MySQL real
- **Docker** (LocalStack + Mailpit em dev, MySQL descartável nos testes de integração)

## Arquitetura

Clean Architecture em camadas, com uma regra de dependência estrita: `Domain` não depende de
nada; `Application` depende só de `Domain`; `Infrastructure` implementa as interfaces definidas
em `Application`; `API` depende de `Application` e `Infrastructure` via injeção de dependência.

```
Sunset.sln
├── src/
│   ├── Sunset.API/                  # Controllers, Middlewares, Program.cs
│   ├── Sunset.Application/          # Services, Interfaces, DTOs, Validators
│   ├── Sunset.Domain/               # Entities, Enums — sem dependência de framework
│   └── Sunset.Infrastructure/       # EF Core, Repositories, Storage (S3), JWT
└── tests/
    ├── Sunset.UnitTests/            # Services, com dependências mockadas (Moq)
    └── Sunset.IntegrationTests/     # API + MySQL real via Testcontainers
```

Mais detalhes de convenções de código e decisões de design em [`CLAUDE.md`](CLAUDE.md).

## Funcionalidades

- **Autenticação**: registro/login com JWT (access + refresh token rotativo), login com Google,
  redefinição de senha por e-mail, exclusão de conta (anonimização, LGPD).
- **Locais**: busca com FULLTEXT + geolocalização, ranking por período (semana/mês/todos),
  horário estimado de pôr do sol.
- **Fotos**: upload direto ao storage via URL pré-assinada, feed paginado (recentes/mais
  curtidas), curtidas, comentários com um nível de resposta.
- **Avaliações**: nota + comentário por local, atualiza a média desnormalizada.
- **Moderação**: papéis de usuário (`User`/`Moderator`/`Admin`), denúncia de fotos/comentários,
  fila de denúncias, exclusão de conteúdo por moderador (soft delete, preserva auditoria),
  termos de uso e política de privacidade versionados, log de ações de moderação.

Referência completa de endpoints, formatos de request/response e comportamentos específicos:
[`docs/API.md`](docs/API.md).

## Pré-requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- MySQL 8 rodando localmente (ou acessível via connection string)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — necessário para:
  - **LocalStack**, que emula o S3 em desenvolvimento (upload de fotos/avatares)
  - **Mailpit**, que captura os e-mails enviados em desenvolvimento (redefinição de senha) sem precisar de um servidor SMTP real
  - **Testcontainers**, que sobe um MySQL descartável para os testes de integração

## Como rodar localmente

1. Clone o repositório e restaure as dependências:
   ```bash
   git clone https://github.com/leosilva1999/sunsetapi.git
   cd sunsetapi
   dotnet restore
   ```

2. Configure a connection string do MySQL e o segredo do JWT. `appsettings.Development.json`
   já traz valores padrão (`Server=localhost;...;User=root;Password=root;`); se o seu MySQL
   local usar outra senha, sobrescreva via `dotnet user-secrets` (não versionado) em vez de
   editar o arquivo:
   ```bash
   cd src/Sunset.API
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Port=3306;Database=sunset;User=root;Password=<sua-senha>;"
   ```

3. Aplique as migrações do banco:
   ```bash
   dotnet ef database update --project src/Sunset.Infrastructure --startup-project src/Sunset.API
   ```

4. Suba o LocalStack (emulação de S3) e o Mailpit (captura de e-mails):
   ```bash
   docker compose up -d
   ```

5. Rode a API:
   ```bash
   dotnet run --project src/Sunset.API
   ```

   Em ambiente de desenvolvimento, o Swagger UI abre automaticamente
   (`http://localhost:5256/swagger`), e o banco é populado com dados fictícios no primeiro
   startup (idempotente — não roda de novo se já houver usuários). E-mails enviados pela API
   (ex.: redefinição de senha) ficam disponíveis em `http://localhost:8025` (UI do Mailpit) — não
   são entregues a caixas de entrada reais.

### Dados de seed (apenas em Development)

8 usuários, 6 locais reais no Brasil, fotos/curtidas/comentários/avaliações, e uma versão
inicial dos termos de uso e da política de privacidade. Todos os usuários de seed compartilham
a senha `Password123!`. Dois deles já vêm com papel elevado para testar a moderação:

| E-mail | Papel |
|---|---|
| `beatriz@sunsetapp.dev` | Moderator |
| `thiago@sunsetapp.dev` | Admin |

## Testes

```bash
# Todos os testes
dotnet test

# Só os unitários (rápidos, sem dependências externas)
dotnet test tests/Sunset.UnitTests

# Só os de integração (exige Docker Desktop rodando — sobe um MySQL real via Testcontainers)
dotnet test tests/Sunset.IntegrationTests
```

Os testes de integração usam um MySQL real (não in-memory) porque a busca de locais depende de
um índice FULLTEXT do MySQL, sem equivalente em bancos em memória.

## Comandos úteis

| Ação | Comando |
|---|---|
| Build | `dotnet build` |
| Rodar a API | `dotnet run --project src/Sunset.API` |
| Nova migração | `dotnet ef migrations add <Nome> --project src/Sunset.Infrastructure --startup-project src/Sunset.API` |
| Aplicar migrações | `dotnet ef database update --project src/Sunset.Infrastructure --startup-project src/Sunset.API` |
