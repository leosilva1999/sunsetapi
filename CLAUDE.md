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
| `User` | id, name, email, avatar_url | |
| `Location` | id, name, latitude, longitude, city, avg_rating | `avg_rating` desnormalizado, atualizado quando uma `Rating` é criada |
| `Photo` | id, user_id, location_id, image_url, caption, likes_count | `likes_count` desnormalizado |
| `Like` | id, user_id, photo_id | par (user_id, photo_id) único |
| `Comment` | id, user_id, photo_id, content | |
| `Rating` | id, user_id, location_id, score (1–5) | par (user_id, location_id) único — nota do local, separada da curtida na foto |

Relacionamentos: `User` 1:N `Photo`/`Like`/`Comment`/`Rating`. `Location` 1:N `Photo`/`Rating`. `Photo` 1:N `Like`/`Comment`.

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
- `POST /photos` (auth — recebe `location_id` + `image_url` já enviada ao storage + legenda)
- `GET /photos/:id`
- `DELETE /photos/:id` (auth, só o autor)
- `POST /photos/:id/likes` / `DELETE /photos/:id/likes` (auth)
- `GET /photos/:id/comments`, `POST /photos/:id/comments` (auth)
- `DELETE /comments/:id` (auth, só o autor)

## Decisões de design

- **Upload de imagem**: o cliente sobe o arquivo direto pro storage (S3/R2) via URL pré-assinada; o `POST /photos` recebe só a URL resultante, nunca o binário.
- **Ranking**: `avg_rating` e `likes_count` são desnormalizados e atualizados no momento da escrita (ou por job), não calculados a cada leitura.
- **Paginação**: cursor-based nos endpoints de feed (`/photos`, `/locations`), não `?page=`.
- **Auth**: JWT (Bearer token) nos endpoints marcados como "auth".

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
- Rodar a API: `dotnet run --project src/Sunset.API`
- Migração EF Core (a partir de `Sunset.Infrastructure`, com `Sunset.API` como startup project): `dotnet ef migrations add <Nome> --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
- Aplicar migrações: `dotnet ef database update --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
