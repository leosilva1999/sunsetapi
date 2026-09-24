# Sunset API

API .NET para o **Sunset** — plataforma onde usuários pesquisam locais com as
mais bonitas visões de pôr do sol, postam fotos marcando o local, curtem,
comentam e avaliam os locais para gerar rankings.

## Arquitetura

Clean Architecture em camadas. Regra de dependência: `Domain` não depende de
nada; `Application` depende só de `Domain`; `Infrastructure` implementa as
interfaces definidas em `Application`; `API` depende de `Application` e
`Infrastructure` (via injeção de dependência).

```
Sunset.sln
├── src/
│   ├── Sunset.API/                  # Controllers, Middlewares, Program.cs
│   ├── Sunset.Application/          # Services, Interfaces, DTOs, Validators
│   ├── Sunset.Domain/               # Entities, Enums — sem dependência de framework
│   └── Sunset.Infrastructure/       # EF Core, Repositories, Storage (S3), JWT
└── tests/
    ├── Sunset.UnitTests/
    └── Sunset.IntegrationTests/
```

Regras:
- Controllers ficam finos: recebem request, chamam o Service, devolvem DTO. Nenhuma regra de negócio no Controller.
- Toda nova entidade de domínio entra em `Domain/Entities`, sem atributos de EF Core (mapeamento fica em `Infrastructure/Persistence/Configurations` via Fluent API).
- Toda dependência externa (storage, envio de token, etc.) é uma interface em `Application/Interfaces`, implementada em `Infrastructure`.
- DTOs organizados por feature (`DTOs/Photos/`, `DTOs/Locations/`), não por tipo.

## Entidades principais

| Entidade | Campos-chave | Observação |
|---|---|---|
| `User` | id, name, email, avatar_url, role | `role`: `User`\|`Moderator`\|`Admin` |
| `Location` | id, name, latitude, longitude, city, avg_rating | `avg_rating` desnormalizado, atualizado quando uma `Rating` é criada |
| `Photo` | id, user_id, location_id, image_url, caption, likes_count, deleted_at, deleted_by_user_id | `likes_count` desnormalizado; soft delete (moderador ou autor) |
| `Like` | id, user_id, photo_id | par (user_id, photo_id) único |
| `Comment` | id, user_id, photo_id, content, deleted_at, deleted_by_user_id | soft delete (moderador ou autor) |
| `Rating` | id, user_id, location_id, score (1–5) | par (user_id, location_id) único — nota do local, separada da curtida na foto |
| `Report` | id, reporter_id, target_type, target_id, reason, status | par (reporter_id, target_type, target_id) único; `target_id` sem FK (associação polimórfica Photo/Comment) |
| `ModerationAction` | id, moderator_id, action_type, target_description | log de auditoria append-only, sem endpoint de leitura ainda |
| `TermsOfService` | id, content, version, updated_by_user_id | cada edição cria uma linha nova (histórico via `version`), sem update in-place |

Relacionamentos: `User` 1:N `Photo`/`Like`/`Comment`/`Rating`/`Report`/`ModerationAction`.
`Location` 1:N `Photo`/`Rating`. `Photo` 1:N `Like`/`Comment`.

## Endpoints (prefixo `/api/v1`)

**Auth**
- `POST /auth/register`, `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`

**Users**
- `GET /users/:id`, `PATCH /users/me` (auth), `GET /users/:id/photos`

**Locations**
- `GET /locations` (busca: `?q=`, `?lat=&lng=&radius=`, paginado)
- `GET /locations/:id`
- `POST /locations` (auth)
- `GET /locations/:id/photos`
- `GET /locations/ranking` (`?period=week|month|all`)
- `POST /locations/:id/ratings` (auth)

**Photos**
- `GET /photos` (`?sort=recent|top`, paginado)
- `POST /photos/upload-url` (auth — recebe `content_type`, devolve URL pré-assinada de PUT + a `image_url` final)
- `POST /photos` (auth — recebe `location_id` + `image_url` já enviada ao storage + legenda)
- `GET /photos/:id`
- `DELETE /photos/:id` (auth, autor ou moderador/admin — soft delete)
- `POST /photos/:id/likes` / `DELETE /photos/:id/likes` (auth)
- `GET /photos/:id/comments`, `POST /photos/:id/comments` (auth)
- `POST /photos/:id/reports`, `POST /comments/:id/reports` (auth, rate limited)
- `DELETE /comments/:id` (auth, autor ou moderador/admin — soft delete)

**Moderation** (moderador/admin)
- `GET /moderation/reports` (`?status=Pending|Resolved|Dismissed`, paginado)
- `PATCH /moderation/reports/:id` (resolver/dispensar)
- `PATCH /moderation/users/:id/role` (admin only — promove/rebaixa)
- `GET /terms` (público), `PUT /terms` (admin only — cria nova versão)

## Decisões de design

- **Upload de imagem**: o cliente sobe o arquivo direto pro storage (S3/R2) via URL pré-assinada; o `POST /photos` recebe só a URL resultante, nunca o binário.
- **Ranking**: `avg_rating` e `likes_count` são desnormalizados e atualizados no momento da escrita (ou por job), não calculados a cada leitura.
- **Paginação**: cursor-based nos endpoints de feed (`/photos`, `/locations`), não `?page=`.
- **Auth**: JWT (Bearer token) nos endpoints marcados como "auth". Claim `role` no token
  (`RoleClaimType = "role"` no `Program.cs`, já que `MapInboundClaims = false`) habilita
  `[Authorize(Roles = "Moderator,Admin")]` nos endpoints de moderação.
- **Moderação**: exclusão de posts/comentários por moderador é soft delete (`DeletedAt`/
  `DeletedByUserId` + `HasQueryFilter` no `SunsetDbContext`), não remoção física — preserva
  contadores desnormalizados e dá auditoria. Não existe rota para criar o primeiro `Admin`;
  em dev isso vem do `DbSeeder`, em outros ambientes exige update direto no banco.
- **Enums em corpo JSON** serializam como string, não int — via `[JsonConverter(typeof(
  JsonStringEnumConverter))]` na propriedade do DTO (não um `AddJsonOptions` global, que só
  afeta o serializer do próprio servidor e quebraria clientes/testes que leem a resposta com
  as próprias opções padrão).

## Stack

- .NET 9 (net9.0 — apenas o SDK 9 está instalado neste ambiente; migrar para net8.0 LTS é uma troca de `TargetFramework` quando o SDK 8 estiver disponível)
- Entity Framework Core + MySQL (Pomelo.EntityFrameworkCore.MySql)
- FluentValidation para os validators em `Application/Validators`
- xUnit para os testes

## Convenções de código

- Nomes de classes e métodos em inglês; nomes de rotas/URLs em inglês (`/locations`, `/photos`).
- Um repository por entidade agregada (`IPhotoRepository`, `ILocationRepository`, `IUserRepository`).
- Exceptions de domínio/aplicação em `Application/Exceptions` (ex: `NotFoundException`, `UnauthorizedActionException`), tratadas centralmente pelo `ExceptionHandlingMiddleware`.

## Comandos

- Build: `dotnet build`
- Testes (todos): `dotnet test`
- Testes (um projeto): `dotnet test tests/Sunset.UnitTests`
- Testes de integração: `dotnet test tests/Sunset.IntegrationTests` — **exige Docker Desktop rodando**
  (sobe um MySQL 8 real via Testcontainers; necessário porque a busca de locations usa FULLTEXT do
  MySQL, sem equivalente em InMemory/SQLite). Um único container é compartilhado por toda a suíte
  (`SunsetApiFactory` + `IntegrationTestCollection`); isolamento entre testes vem de dados com nomes
  únicos por teste, não de reset de banco.
- Rodar a API: `dotnet run --project src/Sunset.API`
- Migração EF Core (a partir de `Sunset.Infrastructure`, com `Sunset.API` como startup project): `dotnet ef migrations add <Nome> --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
- Aplicar migrações: `dotnet ef database update --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
