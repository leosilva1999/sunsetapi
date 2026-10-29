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
| `LegalDocument` | id, document_type, content, version, updated_by_user_id | `document_type`: `TermsOfService`\|`PrivacyPolicy`; cada edição cria uma linha nova (histórico via `version`, numerado por tipo), sem update in-place |
| `PasswordResetToken` | id, user_id, token_hash, expires_at, used_at | mesmo padrão de `RefreshToken` (hash SHA-256, nunca o token puro); validade de 30 min, uso único |
| `Follow` | id, follower_id, following_id | par (follower_id, following_id) único; `User` ganha `followers_count`/`following_count` desnormalizados |
| `Notification` | id, recipient_user_id, type, actor_user_id, target_description | `target_description` livre (sem FK polimórfica), mesmo padrão de `ModerationAction`; lido via polling, sem push em tempo real |

Relacionamentos: `User` 1:N `Photo`/`Like`/`Comment`/`Rating`/`Report`/`ModerationAction`/`PasswordResetToken`/`Notification`.
`Location` 1:N `Photo`/`Rating`. `Photo` 1:N `Like`/`Comment`. `User` N:N `User` via `Follow`.

## Endpoints (prefixo `/api/v1`)

**Health** (público, fora do `/api/v1`)
- `GET /health` (liveness), `GET /health/ready` (readiness: checa o banco; 503 se indisponível)

**Auth**
- `POST /auth/register`, `POST /auth/login`, `POST /auth/google`, `POST /auth/refresh`, `POST /auth/logout`
- `POST /auth/forgot-password`, `POST /auth/reset-password` (recuperação de senha por e-mail, rate limited)

**Users**
- `GET /users/:id`, `PATCH /users/me` (auth), `GET /users/:id/photos`
- `PUT /users/me/password` (auth — troca de senha; revoga os outros dispositivos, rate limited)
- `POST /users/:id/follow` / `DELETE /users/:id/follow` (auth), `GET /users/:id/followers`, `GET /users/:id/following`

**Notifications** (auth, sempre "minhas")
- `GET /notifications`, `GET /notifications/unread-count`, `POST /notifications/:id/read`, `POST /notifications/read-all`

**Locations**
- `GET /locations` (busca: `?q=`, `?lat=&lng=&radius=`, paginado; com `lat`+`lng` vira "perto de mim": ordena por distância e devolve `distanceKm`)
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
- `DELETE /moderation/locations/:id` (soft delete + esconde as fotos), `POST .../restore`, `GET /moderation/locations/deleted`, `PATCH /moderation/locations/:id/coordinates` (moderador/admin)
- `POST /moderation/locations/:id/merge` (mescla duplicata no local `targetId`: move fotos e avaliações, apaga a duplicata)
- `POST /locations/:id/reports`, `POST /users/:id/reports` (auth, rate limited — denúncia de local e de usuário; motivo `IncorrectInfo` p/ dados errados)
- `POST /moderation/users/:id/anonymize` (admin only — apaga os dados da conta; `hideContent` esconde fotos e comentários)
- `POST /moderation/users/:id/ban` / `DELETE /moderation/users/:id/ban` (banir/desbanir; moderador só bane `User`, admin bane também moderador)
- `GET /moderation/reports` (`?status=Pending|Resolved|Dismissed`, paginado)
- `PATCH /moderation/reports/:id` (resolver/dispensar)
- `PATCH /moderation/users/:id/role` (admin only — promove/rebaixa)
- `GET /terms` / `GET /privacy` (público), `PUT /terms` / `PUT /privacy` (admin only — cria nova versão, numerada por documento)

## Decisões de design

- **Upload de imagem**: o cliente sobe o arquivo direto pro storage (S3/R2) via URL pré-assinada; o `POST /photos` recebe só a URL resultante, nunca o binário.
- **Ranking**: `avg_rating` e `likes_count` são desnormalizados e atualizados no momento da escrita (ou por job), não calculados a cada leitura.
- **Paginação**: cursor-based nos endpoints de feed (`/photos`, `/locations`), não `?page=`.
- **Locais próximos ("perto de mim")**: `lat`+`lng` (obrigatórios juntos) ativam o modo; `radius` é opcional
  (padrão 50 km, máx. 200) e a validação está em `LocationSearchQueryValidator` (400 se incompleto/fora da
  faixa). A distância é Haversine exata calculada no SQL (`LocationRepository.SearchNearbyAsync`); o
  bounding box roda antes só como pré-filtro (usa o índice `(Latitude, Longitude)`) e o Haversine tira os
  cantos. A ordem é por distância crescente, e o cursor é `(distância, id)` (`DistanceCursor`), não o
  `(CreatedAt, id)` dos demais feeds — por isso só vale para os mesmos `lat`/`lng`/`radius`/`q`. O
  repositório devolve `LocationWithDistance`; `distanceKm` é `null` fora desse modo.
- **Logs (Serilog)**: troca o provider, mas o código segue usando `ILogger<T>`; sinks e níveis por namespace
  ficam na seção `Serilog` do `appsettings` (arquivos em `logs/`, gitignored). `RequestIdMiddleware` usa o
  `TraceIdentifier` como `RequestId`: vai em toda linha de log da requisição, no header `X-Request-Id` de
  toda resposta e no campo `requestId` do corpo de erro (`{ title, errors, requestId }`). Id enviado pelo
  cliente é ignorado de propósito. `UseSerilogRequestLogging` escreve uma linha por requisição (com `UserId`
  do claim `sub`). Erros de negócio passam por `ExceptionHandlingMiddleware`: 5xx/502 em Error com stack,
  401 em Warning, 400/404/409 em Information sem stack. **Nunca logar** senha, tokens (acesso/refresh/reset),
  corpo de requisição nem e-mail do destinatário; validação loga só os nomes dos campos. `Application`
  continua sem logger (só `Domain` como dependência) — log de falha de dependência externa fica na
  `Infrastructure` (ex: `SmtpEmailSender`, `SunsetTimeService`).
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
- **E-mail (SMTP)**: `IEmailSender` (Application) implementado com MailKit (Infrastructure) — o
  `System.Net.Mail.SmtpClient` embutido é obsoleto. Conteúdo do e-mail é montado por quem chama
  (ex: `AuthService`), a interface só transporta `to/subject/htmlBody`. Em dev/testes, roda contra
  o Mailpit (Docker, sem credencial) em vez de um SMTP real — ver `docker-compose.yml` e
  `FakeEmailSender` nos testes de integração.
- **Troca de senha (logado)**: `PUT /users/me/password` exige a senha atual e responde senha errada com
  **400** (`errors.CurrentPassword`), nunca 401 — um 401 faria o cliente achar que a sessão expirou. Revoga
  todos os refresh tokens menos o enviado em `refreshToken` (se válido e do próprio usuário). `User.HasPassword`
  é `false` nas contas criadas pelo Google (o hash é aleatório e desconhecido): a troca devolve 409 e o
  `hasPassword` do `UserResponse` deixa o frontend esconder a opção; passa a `true` quando a senha é definida
  pelo fluxo de redefinição. O e-mail de aviso não derruba a operação se o SMTP falhar.
- **Validação de configuração no startup**: `StartupConfigurationValidator` (chamado no início do `Program.cs`)
  derruba o boot com **uma** mensagem listando tudo que está errado, em vez de virar 500 opaco depois. Em
  todos os ambientes: `Jwt:Secret` precisa ser Base64 e ter ≥32 bytes. Só em `Production`: connection string,
  `Frontend:BaseUrl`, `Storage:*` (com chaves AWS quando não há `ServiceUrl`/LocalStack) e o provedor de
  e-mail escolhido (`Email:Provider` = `HostingerApi` exige `Hostinger:Mail:ApiKey`/`MailboxResourceId`;
  senão exige `Smtp:Host`/`FromAddress`). Variável nova obrigatória em produção entra aqui.
- **Exclusão de locais (moderação)**: soft delete (`Location.DeletedAt/DeletedByUserId` + `HasQueryFilter`, mesmo
  padrão de `Photo`/`Comment`). Apagar um local esconde também as fotos dele com **o mesmo `DeletedAt`**
  (`UPDATE` em lote; o instante é truncado a microssegundos para sobreviver ao round-trip do Postgres) e o
  `restore` só traz de volta fotos com esse instante exato — foto removida antes, isoladamente, continua
  removida. Curtidas/comentários/avaliações não são tocados. `PATCH /moderation/locations/:id/coordinates`
  corrige o pino (auditado com valor antigo → novo); o botão "editar localização" no mapa é do frontend,
  só para `Moderator`/`Admin`.
- **Mesclar locais**: `POST /moderation/locations/:id/merge` `{ targetId }` — a URL é a duplicata. Fotos (inclusive
  as já escondidas) vão para o alvo por `UPDATE` em lote; avaliações também, **exceto** as de quem já avaliou
  o alvo (par `(user, location)` é único: vale a do alvo, a outra fica no local removido); `AvgRating` do alvo é
  recalculado. A duplicata sofre soft delete **por último** (um erro no meio pode ser repetido: os passos
  anteriores viram no-op). Restaurar a duplicata depois devolve um local vazio — não desfaz a mescla.
- **Denúncias de local e usuário**: `Report.TargetType` ganha `Location` e `User` (mesma tabela e fila
  `GET /moderation/reports`; `TargetId` segue sem FK). Alvo inexistente/apagado/anonimizado → 404; denunciar a
  si mesmo → 409. Resolver a denúncia **não age sobre o alvo**: o moderador usa banir/anonimizar/apagar local
  e depois fecha a denúncia. `ReportReason.IncorrectInfo` existe para "pino/nome errado" em locais.
- **Anonimizar usuário (admin)**: `POST /moderation/users/:id/anonymize` aplica o mesmo `User.Anonymize` da
  exclusão de conta, **mais** um ban permanente (assim o access token que o usuário já tem morre na hora, via
  `IBanStatusService`) e revoga os refresh tokens. `hideContent` esconde todas as fotos (UPDATE em lote) e
  comentários (um a um, espelhando `PhotoService.DeleteCommentAsync`, para manter `CommentsCount`/`RepliesCount`
  coerentes; comentários antes das fotos, porque o ajuste de contador precisa da foto visível). Admin e o
  próprio admin são intocáveis; já anonimizado (`@sunset.invalid`) → 409. O aviso por e-mail vai para o
  endereço **original**, antes de ser apagado.
- **Banimento de usuário**: `User.BannedAt/BannedUntil/BanReason/BannedByUserId` (sem `BannedUntil` = permanente;
  ban temporário expira sozinho, `IsBanned(now)` compara com o relógio). `ModerationService.BanUserAsync` revoga
  todos os refresh tokens, grava `ModerationAction` (`UserBanned`/`UserUnbanned`) e avisa por e-mail (falha de e-mail
  não desfaz o ban). Efeito imediato em quem já está logado: `JwtBearerEvents.OnTokenValidated` consulta
  `IBanStatusService` (cache de 30 s, invalidado em ban/unban) e rejeita o token (401); o login devolve **403**
  (`ForbiddenException`, `errors.reason`/`bannedUntil`) só **depois** de conferir a senha, para não revelar a
  existência da conta. Regras de papel: ninguém se bane, admin é intocável, moderador só é banido por admin.
  Conteúdo (fotos/comentários) do banido continua visível.
- **Redefinição de senha**: mesmo padrão de `RefreshToken` (token opaco, hash SHA-256 salvo,
  nunca o token puro). `forgot-password` sempre retorna 204 mesmo pra e-mail inexistente (evita
  enumeração de contas). `reset-password` revoga todos os refresh tokens do usuário ao suceder.
- **Notificações via polling**: o sino do frontend consulta `GET /notifications/unread-count`
  periodicamente — sem SignalR/WebSocket. `PhotoService`/`UserService` gravam `Notification`
  direto via `INotificationRepository` (mesmo padrão de `IModerationActionRepository` injetado
  em `PhotoService` pra auditoria), nunca notificando o próprio autor da ação.

## Stack

- .NET 9 (net9.0 — apenas o SDK 9 está instalado neste ambiente; migrar para net8.0 LTS é uma troca de `TargetFramework` quando o SDK 8 estiver disponível)
- Entity Framework Core + PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL)
- FluentValidation para os validators em `Application/Validators`
- Serilog (`Serilog.AspNetCore`) para logs estruturados — console + arquivo `logs/sunset-.log` (rotação diária)
- MailKit para envio de e-mail via SMTP (Mailpit em dev/testes)
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
  (sobe um PostgreSQL 16 real via Testcontainers; necessário porque a busca de locations usa
  `tsvector`/GIN e o "perto de mim" calcula Haversine no SQL, sem equivalente em InMemory/SQLite). Um único container é compartilhado por toda a suíte
  (`SunsetApiFactory` + `IntegrationTestCollection`); isolamento entre testes vem de dados com nomes
  únicos por teste, não de reset de banco.
- Rodar a API: `dotnet run --project src/Sunset.API`
- Migração EF Core (a partir de `Sunset.Infrastructure`, com `Sunset.API` como startup project): `dotnet ef migrations add <Nome> --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
- Aplicar migrações: `dotnet ef database update --project src/Sunset.Infrastructure --startup-project src/Sunset.API`
