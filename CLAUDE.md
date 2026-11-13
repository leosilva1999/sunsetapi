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
| `City` | id, kind (`Municipality`\|`Locality`), name, country, admin_area, slug, parent_city_id, national_code, time_zone_id, population | catálogo geográfico do GeoNames (não é digitado por usuário); `slug` imutável; localidade tem pai município; nunca apagada (`is_active`) |
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
- `POST /users/me/devices` / `DELETE /users/me/devices` (auth — registra/remove o token de push Expo do celular; 5 aparelhos por usuário)
- `GET` / `PUT /users/me/notification-settings` (auth — preferências de push, só o push), `POST /users/me/devices/test` (Admin ou Development — push de teste)
- `POST /users/:id/follow` / `DELETE /users/:id/follow` (auth), `GET /users/:id/followers`, `GET /users/:id/following`

**Cities** (público)
- `GET /cities` (padrão: só cidades com locais, mais locais primeiro; autocomplete: `?q=&kind=any&includeEmpty=true&sort=population`; `?state=`, `?sort=locations|rating|population|name`, cursor), `GET /cities/:slug` (página da cidade: contagens, melhor local, localidades; 404 sem locais), `GET /cities/:slug/locations|photos|sunset`

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
- `POST /moderation/announcements/preview`, `POST /moderation/announcements` (202), `GET /moderation/announcements`, `GET /moderation/announcements/:id` — comunicados da equipe: serviço ou promoção, por notificação no app e push (Admin: qualquer público; Moderador: só contas nomeadas)
- `DELETE /moderation/locations/:id` (soft delete + esconde as fotos), `POST .../restore`, `GET /moderation/locations/deleted`, `PATCH /moderation/locations/:id/coordinates` (moderador/admin)
- `GET /moderation/locations/unresolved-city` (fila: locais sem cidade do catálogo, com sugestões), `PATCH /moderation/locations/:id/city` (vincula; aceita município ou localidade)
- `POST /moderation/locations/:id/merge` (mescla duplicata no local `targetId`: move fotos e avaliações, apaga a duplicata)
- `POST /locations/:id/reports`, `POST /users/:id/reports` (auth, rate limited — denúncia de local e de usuário; motivo `IncorrectInfo` p/ dados errados)
- `POST /moderation/users/:id/anonymize` (admin only — apaga os dados da conta; `hideContent` esconde fotos e comentários)
- `POST /moderation/users/:id/ban` / `DELETE /moderation/users/:id/ban` (banir/desbanir; moderador só bane `User`, admin bane também moderador)
- `GET /moderation/stats/growth` (`?granularity=day|week|month&from=&to=&metrics=users,locations,photos,ratings` — séries de crescimento para o painel; só moderação/admin)
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
- **Aceite dos Termos/Política**: tabela `legal_acceptances` (usuário, tipo do documento, versão, quando = `CreatedAt`,
  origem `Registration`/`Reacceptance`), append-only e mantida mesmo após anonimização (é a prova do que foi aceito).
  `LegalAcceptanceService`: `register`/`google` aceitam `termsVersion`/`privacyVersion` opcionais (versão que não é
  a atual → 400 antes de criar a conta); toda resposta de auth traz `legalAcceptance` (`pending` quando existe versão
  publicada que o usuário não aceitou); `GET`/`POST /users/me/legal-acceptances`. **Não bloqueia endpoints** — é o
  cliente que mostra a tela de aceite; clientes antigos seguem funcionando (a conta só fica *pending*). Documento
  nunca publicado nunca é pendência.
- **Cidades (GeoNames)**: `City` é um catálogo carregado por `tools/Sunset.CityImporter` (comando manual; a API nunca
  baixa nada) a partir do dump do GeoNames — **CC BY 4.0, a atribuição é obrigatória onde os dados aparecem**.
  Município = `ADM2` (o `admin2` é o código IBGE de 7 dígitos); localidade = lugar povoado (sem `PPLX`) cujo `admin2`
  aponta para um município importado. Descartadas: localidades sem município e as com o **mesmo nome do município** (a
  sede, repetida pelo GeoNames — a página do município já a cobre). Nome oficial do IBGE vence
  (`data/city-name-overrides.json`, `SourceName` guarda o original); município que falta no GeoNames entra por
  `city-additions.json` e é *adotado* (mesma linha/slug) quando o GeoNames passar a tê-lo. Slug = `{nome}-{uf}` /
  `{nome}-{slug do município}` (sufixo `-2` em colisão), **gerado uma vez e nunca alterado**. Importação idempotente
  por `(Source, ExternalId)`, nunca apaga (`IsActive=false`). A busca usa `SearchText` (nome + nome de origem + nomes
  alternativos, sem acento) com `tsvector` + GIN, calculado pelo importador — sem depender da extensão `unaccent`.
  Regras por país em `CityImportRules` (só o Brasil). Medido no Neon: 65.401 linhas = 18 MB de tabela + índices = 37 MB.
- **Local ↔ cidade**: `Location.CityId` (sempre um **município**), `LocalityId` (localidade, opcional; o pai dela é o
  `CityId`) e `CreatedByUserId` (autoria, **só a moderação vê**; nulo nos locais antigos). `Location.City` (texto) continua e,
  quando há vínculo, vira `"{Nome} - {UF}"` do município (`Location.ChangeCity`). `POST /locations` aceita `cityId`
  (município ou localidade; vence o texto) ou só o texto legado: `CityResolver` vincula **só sem dúvida** (um único
  município com o nome/UF, ou homônimos em que o pino está a ≤60 km e ≥2× mais perto de um) e **nunca** vincula localidade
  sozinho; o resto fica sem cidade e cai na fila `GET /moderation/locations/unresolved-city` (motivos `match`/`ambiguous`/
  `locality`/`none`). Contagens (`ratingsCount`, `photosCount`, capa) são **calculadas na leitura** em consultas agrupadas
  (um round trip por página), não colunas — há muitos caminhos que mexem nelas (apagar/mesclar local, `hideContent`).
  A lista de cidades ordenada por atividade (`locations`/`rating`) só ranqueia cidades **com** locais e pagina em memória.
  **Armadilha do EF**: as cidades que o resolvedor devolve (e o `GetActiveByIdAsync`) são lidas **com rastreamento**; com
  `AsNoTracking` o `Add` do `Location` marcaria a cidade solta como nova e tentaria inseri-la (`duplicate key PK_cities`).
- **IP do cliente atrás do proxy**: `UseForwardedHeaders` (XFF + XFP, `KnownProxies/Networks` limpos porque o app só é
  alcançável pelo proxy) é o primeiro middleware. `ForwardLimit` = quantas entradas consumir **da direita** do
  `X-Forwarded-For` (o proxy *acrescenta* o endereço que viu; o que está à esquerda o cliente pode forjar) — padrão 1,
  configurável em `ForwardedHeaders:ForwardLimit` (**no Render é 3**, medido em produção: Cloudflare → balanceador do
  Render → salto interno; com 1 o "cliente" seria um IP 10.x que muda a cada requisição). Sem isso todos os limitadores por IP (Search, Reports, PasswordReset,
  ChangePassword) dividiam um balde só. `GET /health/client-ip` mostra como a API vê a requisição (para conferir o limite
  em produção). Há CI em `.github/workflows/ci.yml` (build + testes unitários + de integração, o runner tem Docker).
- **Push notifications (Expo), fase 1**: toda `Notification` também vira push no celular. **Gancho único**: `NotificationRepository.AddAsync`
  grava e, depois do `SaveChanges`, chama `IPushNotifier.Enqueue` (sem esperar, sem lançar) — vale para qualquer tipo, atual ou futuro, e
  herda a regra "nunca notifica o próprio autor". Fila em memória (`PushNotifier`, 1000, descarta a mais antiga) → `PushNotifierWorker` (junta ~1 s,
  lotes de 100, escopo próprio) → `PushDispatchService`: pula destinatário banido/anonimizado/sem aparelho, limita **20 pushes/hora/pessoa**
  (`PushThrottle`, em memória; excedente fica só no app), compõe o texto pt-BR no servidor (`PushMessageComposer`: título, corpo, `badge` = não lidas,
  `data` só com ids), manda 1 mensagem por aparelho. `IPushSender` é a abstração do provedor; `ExpoPushSender` (HTTP `exp.host`, lotes de 100,
  3 tentativas com backoff em 429/5xx/rede, nunca lança por falha de entrega, **token só mascarado nos logs**) trocável depois. **Opção "C"**:
  envio rápido em memória (um reinício perde só aquele push; a notificação segue no app) **mais** a tabela `push_tickets` (ticket aceito + token) lida
  a cada 5 min por `PushMaintenanceWorker` ~15 min depois (receipts): `DeviceNotRegistered` apaga o token; ticket sem resposta some em 24 h.
  Tabelas: `push_devices` (token único; **um token = um dono** — registrar token de outra conta o move; `installId` conhecido com token novo
  atualiza a linha; máx. **5 aparelhos**/usuário, o menos recente sai) e `push_tickets`. Token apagado em: exclusão de conta
  (`DeleteAccountAsync`), anonimização e banimento (moderação), `DeviceNotRegistered` e 90 dias sem registrar (job diário, roda mesmo com push
  desligado — retenção de dado pessoal). Config `Push:*` (`Enabled` **desligado por padrão**, `DryRun` só loga, `ExpoAccessToken` opcional/segredo,
  `BaseUrl`, `MaxPushesPerUserPerHour`, `MaxDevicesPerUser`, `DeviceRetentionDays`); desligado = registro de token funciona, nada é enviado.
  **Fase 2**: (1) *Preferências* — `notification_settings` (uma linha por pessoa, criada no 1º salvamento; sem linha = tudo ligado; `PushEnabled` +
  `MutedTypes` bitmask por `NotificationType`); `PUT` **substitui** (tipo omitido = ligado; nome desconhecido = 400); afetam **só o push** — a lista
  `/notifications` e o contador seguem completos; tipo desligado nem abre janela de agrupamento. (2) *Agrupamento* — `PushGrouper` (singleton, em memória):
  chave (destinatário, tipo, alvo) — `NewFollower` ignora o alvo (todos os seguidores de uma pessoa = 1 grupo); **o 1º sai na hora** e abre uma janela de
  `Push:GroupWindowSeconds` (60; 0 desliga); os seguintes ficam retidos e saem juntos no fim da janela ("Ana e mais 3 curtiram sua foto", `data.count`), quando abre
  nova janela; conta pessoas **distintas** (alguém que comenta 3 vezes não vira "e mais 2"). `PushGroupFlushWorker` varre a cada 5 s
  (`FlushDueGroupsAsync`, que reaplica as preferências). Reinício perde só os pushes retidos. O limite de 20/hora conta pushes enviados (cada grupo vale 1).
  (3) *Push de teste* — `POST /users/me/devices/test` → `PushDispatchService.SendTestAsync` (direto, sem fila/agrupamento/preferências; contabiliza tickets e remove
  token morto); só Admin ou `Development`; 409 com o push desligado; 5/15 min.
  Testes de integração trocam o provedor por `FakePushSender` (nada de rede). Token de push é dado pessoal: está no rascunho da Política
  (`docs/legal`). Fora da fase 1 (fica no `docs/proposal-push-notifications.md`): preferências por tipo, agrupamento, endpoint de teste, pushes de moderação.
- **Comunicados da equipe (promoções e avisos)**: tabela `announcements` (conteúdo, categoria `Service|Promotional`, público `All|Users|PushEnabled`, estado
  `Pending→Sending→Sent`, `ExpectedRecipients`/`CreatedCount`) + **uma `Notification` por destinatário** (`Type=Announcement`, `AnnouncementId`, ator = a conta da
  equipe, que a resposta **esconde**: "Equipe Sunset", sem avatar, `ActorUserId` zerado). Assim lista, contador de não lidas, "marcar como lida" e push reaproveitam
  tudo; `NotificationResponse` ganha `title/body/category/link/announcementId`. **Entrega durável**: `AnnouncementWorker` (a cada `Push:AnnouncementPollSeconds`)
  → `AnnouncementDeliveryService` pega o mais antigo não concluído e cria as notificações em lotes (`AnnouncementBatchSize`, 200) com a consulta "elegíveis sem
  notificação deste comunicado" — **a própria consulta é o ponteiro de retomada** (reinício continua de onde parou; índice único `(RecipientUserId, AnnouncementId)`
  é a rede de segurança); depois `DispatchAnnouncementAsync` manda o push (texto da equipe, `data` com `announcementId/category/link`; sem agrupamento e **fora** do limite de 20/h;
  respeita só o interruptor geral do push). Notificações salvas antes do push (no app: pelo menos uma vez; push: melhor esforço). **Elegibilidade**: conta ativa (não banida,
  não `@sunset.invalid`); `Promotional` exige `NotificationSetting.PromotionalEnabled` (**opt-in**, padrão desligado; `PUT` de settings que omite o campo desliga — direção segura) e no
  máximo **1 promoção/24 h** por pessoa (vale para app e push); `Service` vai a todos. **Permissões**: Admin qualquer público; Moderador só `Users` (1–20 contas). **Salvaguardas**:
  `POST /moderation/announcements/preview` conta; o envio exige `expectedRecipients` igual à contagem refeita (senão 409 com o número real); no máx. 5 envios em massa/24 h (409);
  `link` só `Location|City|Photo|User:{id/slug}` (nunca URL externa); auditado como `AnnouncementSent`. 202 na criação; acompanhar por `GET .../{id}`. Sem agendamento, sem
  recolher (retirar) e sem segmentos por cidade — fase B. Os testes de integração só usam público `Users` (um envio em massa notificaria usuários de testes posteriores).
- **Estatísticas de crescimento (moderação)**: `GET /moderation/stats/growth` — `StatsService` monta os períodos (dia; semana começando na
  segunda; mês no dia 1) e preenche os vazios com zero; `StatsRepository` conta por data de criação com **SQL cru** (`date_trunc` em
  `America/Sao_Paulo`, só tabela/granularidade de listas fixas, valores como parâmetros). SQL cru **ignora o `HasQueryFilter`**, então locais e
  fotos escondidos são excluídos à mão (`DeletedAt IS NULL`). `total` = acumulado incluindo o que existia antes de `from`. Usuários: toda
  conta já criada conta (anonimizar mantém a linha); `summary` separa ativas/removidas **hoje** — a data da remoção não é gravada
  (`AnonymizedAt` seria uma migration simples se um dia se quiser desenhar remoções). Máx. 400 pontos por métrica; sem cache (chamada
  autenticada nunca entra no OutputCache) — a consulta é barata. Sem migration; se as tabelas crescerem muito, índice em `CreatedAt`.
- **Locais de lançamento (`tools/Sunset.LocationSeeder`)**: carga manual de uma lista curada (`data/launch-locations.json`, 25 locais
  de pôr do sol de guias/rankings de viagem; coordenadas do OpenStreetMap conferidas contra o catálogo) pelo **próprio
  `LocationService`** (slug, vínculo município/localidade e checagem de duplicados idênticos aos de um usuário). Simulação por padrão
  (transação desfeita); `--apply` grava. Idempotente (o que já existe vira "duplicado" e é pulado) e aborta tudo se qualquer item tiver
  problema. Os locais pertencem à conta admin (`CreatedByUserId`) e **não têm foto**: municípios com local ganham página, localidades só
  quando surgir foto.
- **Duplicados no cadastro**: `POST /locations` consulta `ILocationRepository.FindDuplicateCandidateIdsAsync` antes de gravar:
  locais **visíveis** a ≤150 m do pino (qualquer cidade; caixa envolvente + Haversine, mais próximo primeiro) e, com município
  conhecido, os do mesmo município com o **mesmo nome normalizado** (`NameNormalizer`, comparado em memória). Havendo candidatos
  (máx. 5) → `ConflictException` (agora com `Errors`) → **409** `{ title, errors: { candidateLocationIds }, requestId }`; o
  cliente reenvia com `confirmDistinct: true`, que pula a checagem (sempre respeitado — é consultivo; a mesclagem da moderação cobre o que
  escapar). **Mudança de contrato**: o site precisa tratar o 409 (nos testes de integração todo `CreateLocationRequest` que cria
  locais próximos/homônimos passa `ConfirmDistinct: true`).
- **Páginas de localidade**: `RequirePublicCityAsync` aceita `Kind = Locality` quando a localidade tem **≥1 local com foto** (município:
  ≥1 local visível) — localidades são ~12× mais numerosas e um nome sem conteúdo é página fina. A lista (`kind=locality`, sem
  `includeEmpty`) aplica a **mesma regra**, para todo item listado ter página (ordenações por coluna via `Any` com `Photos.Any`; por
  atividade, descartando as sem foto em memória). Mesmo id serve a município e localidade (GUIDs únicos): `GetByCityAsync`/
  `GetByCityIdAsync` filtram `CityId == id || LocalityId == id`; `GetDetailExtrasAsync(id, kind)` escolhe o melhor local entre os da
  localidade e não lista sub-localidades; `CityDetailResponse.Parent` (município-pai) só vem em localidade. Sitemap ganhou
  `type=locality` (mesma regra; `lastModified` por `LocalityId`).
- **Locais próximos e rankings por região**: `GET /locations/{idOrSlug}/nearby` reaproveita `SearchAsync` ("perto de mim") centrado
  no local, pedindo `limit+1` porque o próprio local aparece a distância 0 e é descartado (raio padrão 30 km, máx. 200; limite
  máx. 12). `GET /locations/ranking?citySlug=&state=`: `RankingFilter` (município → `CityId`, localidade → `LocalityId`, estado →
  `Municipality.AdminArea`, tudo AND) aplicado **antes** do `Take`, via `Where(r => locations.Any(l => l.Id == r.LocationId))` — o
  que também impede um local removido de ocupar vaga no top N (antes ele era descartado depois do corte). Sem filtros mantém a
  consulta original. `GET /cities/ranking`: média das notas **dadas no período** por município (locais de localidade contam
  para o município pai), desempate por mais notas; devolve `CityResponse` (contagens totais) + `periodAvgRating`/`periodRatingsCount`.
- **Cidade sugerida pelo pino (`GET /geo/cities`)**: `CityRepository.GetNearestAsync` — Haversine exato no SQL atrás de uma
  caixa envolvente que usa o índice `(Latitude, Longitude)` de `cities`; alarga o raio (15 → 60 → 250 km) até achar `limit`
  lugares (tudo dentro do raio está na caixa, então os `limit` primeiros são os mais próximos de verdade). Mistura localidades e
  municípios (distância ao **ponto central**, não a um limite — é sugestão, não "dentro de qual município"). Com `cityId`,
  devolve `chosenCity.distanceKm` e `farAway` (> 150 km, `NearbyCitiesResponse.FarAwayKm`): **aviso, nunca bloqueio**; `POST /locations`
  não mudou. Usa a política `Search` (lat/lng contam como busca) e o cache `Cities`.
- **Descrição e categoria do local**: `Location.Description` (≤1000, texto puro, aparado; vazio = nenhum) e
  `Location.Category` (`LocationCategory?`: `Beach|Viewpoint|Mountain|Lake|River|Urban|Other`, nulo = não informado), ambos
  opcionais em `POST /locations` e expostos em `LocationResponse`. Edição só por moderação:
  `PATCH /moderation/locations/{id}` (parcial: campo ausente fica como está; `description: ""` limpa), auditada como
  `LocationDetailsChanged` **sem copiar o texto** (só os tamanhos / a categoria antiga→nova). Nome e cidade seguem sem
  edição (o slug é imutável). Edição pelo próprio autor fica para depois (`CreatedByUserId` já existe).
- **Avisar o site (revalidação + IndexNow)**: `ISiteNotifier.Enqueue` (Application) é fire-and-forget: fila `Channel` em
  memória (256, descarta o mais antigo) → `SiteNotifierWorker` espera ~2 s, junta o que chegou (união das tags, caminhos
  distintos) → `SiteNotificationSender` faz `POST {Frontend:BaseUrl}/api/revalidate` com `X-Revalidate-Secret`
  (`{paths, tags}`) e, para páginas **novas**, o ping do IndexNow. 2 tentativas, falha só logada (nunca o segredo), nunca
  derruba a requisição. Gancho único: `OutputCacheInvalidator` — **toda** invalidação do cache da API também avisa o
  site (só tags); `InvalidateAsync(tags, SiteChange, ct)` acrescenta os caminhos exatos (`SitePages.ForLocation`) e
  `Index=true` onde a página é nova: criar e restaurar local (foto nova só revalida). Sem `Frontend:RevalidateSecret`
  e sem `IndexNow:Key` o notificador é no-op. Rotas do site assumidas: `/locations/{slug}` e `/cidades/{slug}`
  (`SitePages`). Testes em `SiteNotificationTests` (sem banco). O site precisa implementar `/api/revalidate` e servir
  `/{chave}.txt`.
- **Tamanho da foto**: `Photo.Width/Height` (`int?`, ambos ou nenhum), preenchidos em `POST /photos` por
  `IUploadedImageSanitizer.SanitizeAndMeasureAsync` (o `SanitizeAsync` do avatar segue igual): `ImageDimensionReader` lê o
  cabeçalho do arquivo **já limpo** (SOF do JPEG / IHDR do PNG), sem decodificar. JPEG com orientação EXIF 5–8 tem
  largura/altura **trocadas** (é como o navegador exibe). Fotos anteriores ficam `null` — sem tarefa de backfill: em
  produção ainda não havia fotos; se surgirem, uma tarefa única lê o cabeçalho de cada objeto (precisa de leitura no bucket).
- **Slug de local**: `Location.Slug` (obrigatório, único em **todas** as linhas, apagadas inclusas — slug de local removido
  fica reservado), `{nome}-{slug do município}` (só o nome sem cidade), sufixo `-2`, `-3` na colisão
  (`SlugGenerator.ForLocation/WithSuffix`, `LocationService.CreateAsync` checa `SlugExistsAsync`). Atribuído **uma vez**
  (`AssignSlug` lança se já tem) e imutável: mudar a cidade depois não muda o slug. `AddAsync` recusa local sem slug; o
  `DbSeeder` gera os seus. A migração preenche os existentes com `{nome sem acento}-{8 hex do id}`.
  `GET /locations/{idOrSlug}`: GUID → por id, senão slug (valida formato antes da consulta); rotas literais (`ranking`)
  ganham da rota de slug. Não há tabela de histórico de slugs (não existe edição de nome/cidade).
- **Feed do sitemap**: `GET /sitemap/entries?type=` (`SitemapRepository`): paginado por id (cursor = último Guid), 1 consulta
  da página + consultas agrupadas de datas só para os ids da página (`lastModified` calculado, sem coluna). Cache de 1 h
  (política `Sitemap`, mesmas tags de invalidação), sem limite de busca.
- **Cache e limite de busca**: respostas **públicas** de leitura (`/cities*`, `/locations` lista, `/locations/ranking`,
  `/locations/:id`, `/locations/:id/photos`, `/photos`) usam `OutputCache` por 60 s (variando por query string e por
  `Origin` — o `Access-Control-Allow-Origin` fica gravado na entrada), em memória e por instância. **Pedido com
  `Authorization` nunca entra nem sai do cache** (por isso `likedByCurrentUser` segue certo). Ordem do pipeline:
  `UseCors` → `UseOutputCache` → `UseRateLimiter` → auth — o cache vem **antes** do limitador (acerto de cache não gasta
  cota). `PublicCacheHeadersMiddleware` (dentro do cache, só roda no miss) põe `Cache-Control` + `ETag`; o 304 num acerto é
  do próprio OutputCache. Invalidação por tag (`cities`, `locations`, `photos`) via `IResponseCacheInvalidator` (Application;
  `OutputCacheInvalidator` na API), chamada **depois** de gravar em: criar local, avaliar/remover nota, criar/apagar foto,
  apagar/restaurar/mesclar local, corrigir coordenadas, mudar cidade, `anonymize` com `hideContent`. Curtidas, comentários e
  perfil não invalidam (60 s). A ferramenta de cidades roda fora da API, então não invalida: o TTL cobre. **Nos testes de
  integração o cache só liga com o cabeçalho `X-Test-Cache: on`** (a suíte divide um servidor; cache sempre ligado vazaria
  respostas entre testes). Limite `Search` agora só vale com `q`/`lat`/`lng` (`CitySearch`: só com `q`); listagem simples não conta.
  `X-Service-Key` (`Service:ApiKey`, comparação em tempo constante, **obrigatória em produção** no `StartupConfigurationValidator`)
  dá ao servidor do site um balde próprio de 600/10 s; só capacidade, não autentica nada.
- **Remoção de EXIF/GPS**: o cliente sobe a imagem direto no S3 (URL pré-assinada), então o servidor limpa **depois**,
  em `POST /photos` e no `PATCH /users/me` com avatar novo (`IUploadedImageSanitizer`): baixa o objeto pela URL pública
  (o IAM continua só com `s3:PutObject`), `ImageMetadataStripper` (Application, sem dependências) remove EXIF/XMP/IPTC/
  comentários/tIME/dados após o fim **sem recodificar** (qualidade intacta) e regrava no mesmo key. A única coisa mantida
  é o flag de orientação JPEG, reescrito num EXIF mínimo (senão fotos em retrato giram). Também valida que é JPEG/PNG de
  verdade (≤15 MB) → 400 `ImageUrl`/`AvatarUrl`. Nos testes de integração o sanitizador é trocado por um fake
  (`AlwaysCleanImageSanitizer`), porque as URLs de teste não existem. Fotos enviadas antes dessa mudança continuam com EXIF
  até um backfill.
- **Termos de Uso e Política de Privacidade**: a versão 2 de cada (minuta completa, LGPD) está publicada na API e os
  textos-fonte ficam em `docs/legal/` (`termos-de-uso.txt`, `politica-de-privacidade.txt`), junto de
  `pontos-para-revisao-juridica.md` (campos `[...]` a preencher, decisões e lacunas técnicas). Aguardam revisão
  jurídica; a versão final entra por `PUT /terms` / `PUT /privacy` (cria a versão seguinte). Ao mudar
  comportamento de dados (novo dado coletado, novo prestador, nova retenção), atualize esses textos.
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
