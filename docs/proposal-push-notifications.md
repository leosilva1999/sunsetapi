# Proposta: push notifications reais (app mobile ↔ API)

> Documento para a sessão da **API** (`Sunset.API`). Escrito pelo lado do app mobile (Expo / React
> Native). Descreve o que o app precisa que a API ofereça, o contrato proposto e os pontos em aberto.
> Nada aqui foi implementado ainda, nem no app nem na API.

## 1. Contexto e objetivo

Hoje as notificações existem **só dentro do app**, por *polling*:

- A API grava uma `Notification` nos 4 gatilhos: `NewFollower`, `PhotoLiked`, `PhotoCommented`,
  `CommentReplied` (nunca para o próprio autor da ação).
- O app consulta `GET /notifications/unread-count` a cada 45 s **com o app aberto** (sino da Home) e
  `GET /notifications` ao abrir a tela.
- Com o app **fechado ou em segundo plano o usuário não recebe nada**: sem aviso na barra do
  celular, sem som, sem selo no ícone.

**Objetivo:** quando uma `Notification` é criada, o usuário recebe também um **push** no celular, que
ao ser tocado abre a tela certa do app. O que já existe (lista, contador, marcar como lida) continua
igual — o push é um canal adicional, não um substituto.

## 2. Decisão de arquitetura: Expo Push Service

O app usa Expo/EAS. Recomendamos que a API envie pelos **serviços de push da Expo**
(`https://exp.host/--/api/v2/push/send`), que fazem a ponte com FCM (Android) e APNs (iOS):

- A API **não precisa** de credenciais do Firebase/Apple nem de SDK nativo — só de uma chamada HTTPS
  e (opcionalmente) de um *access token* da Expo. As credenciais FCM/APNs ficam no EAS, do lado do app.
- O app registra o **Expo push token** (`ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]`) e o envia à API.

Alternativa descartada por ora: a API falar direto com FCM/APNs (FirebaseAdmin etc.). Dá mais controle,
mas exige guardar credenciais Apple/Google na API e tokens nativos no app. Sugerimos **abstrair o envio
atrás de uma interface** (`IPushSender`) para poder trocar o provedor depois sem mexer no resto.

## 3. Contrato proposto (v1)

Convenções do `docs/API.md` valem aqui: 🔒 = Bearer, erros `{ title, errors, requestId }`,
`204` sem corpo, idempotência quando possível.

### 3.1 🔒 `POST /users/me/devices` — registrar / atualizar o aparelho

```json
{
  "token": "ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]",
  "platform": "android",
  "installId": "b9a3e1f0-0c2d-4f6a-9d58-1d3f6c5e7a21",
  "appVersion": "1.0.0",
  "locale": "pt-BR"
}
```

- `token`: obrigatório, formato `^(Exponent|Expo)PushToken\[.+\]$` (senão `400`, `errors.Token`).
- `platform`: `"android"` | `"ios"` (obrigatório).
- `installId`: UUID estável **por instalação do app**, gerado no app (opcional, mas recomendado: permite
  trocar o token do mesmo aparelho sem duplicar linhas).
- `appVersion`, `locale`: opcionais, informativos (≤ 32 chars).
- **Upsert** por `token`. Se o mesmo token já pertence a **outro usuário** (aparelho compartilhado,
  logout sem limpar, troca de conta), passa a pertencer ao usuário atual — um token nunca deve receber
  push de dois usuários.
- Se vier `installId` já conhecido com **token diferente**, atualiza o token daquela instalação.
- Limite por usuário (ex.: **10 aparelhos**, descarta o menos recente) e rate limit de registro (o app
  chama no login, na abertura e quando o token muda; sugestão: 30 req / 15 min por IP).
- `204 No Content`. Idempotente.

### 3.2 🔒 `DELETE /users/me/devices` — remover o aparelho

Body `{ "token": "ExponentPushToken[...]" }` (ou `{ "installId": "..." }`). `204`, **idempotente**
(token desconhecido também é `204`). O app chama **antes** do `POST /auth/logout` (precisa do Bearer) e na
exclusão de conta.

### 3.3 Limpeza automática (lado da API)

Remover **todos** os tokens do usuário quando: a conta é excluída/anonimizada (`DELETE /users/me`,
anonymize por moderação), o usuário é banido (permanente ou enquanto durar o ban), e quando a Expo
responder `DeviceNotRegistered` (ver 4.3).

### 3.4 (v1.1) 🔒 `GET` / `PUT /users/me/notification-settings` — preferências

```json
{ "pushEnabled": true, "types": { "NewFollower": true, "PhotoLiked": true, "PhotoCommented": true, "CommentReplied": true } }
```

Padrão: tudo ligado. `GET` devolve o estado atual; `PUT` substitui. O app mostrará os interruptores em
"Editar perfil" → "Notificações". Na v1 pode ficar de fora (o usuário desliga pelo sistema Android/iOS).

### 3.5 (opcional, só `Development` ou Admin) 🔒 `POST /users/me/devices/test`

Envia um push de teste para os aparelhos do próprio chamador. Útil para QA sem precisar de duas contas.
Rate limit apertado (ex.: 5 / 15 min).

## 4. Envio

### 4.1 Quando e como

Ao **criar** uma `Notification` (os 4 gatilhos atuais), enfileirar um envio de push para o destinatário.
O envio **não pode atrasar nem derrubar** a requisição de origem (curtir, comentar, seguir): a
notificação continua sendo salva mesmo que o push falhe.

- Padrão sugerido: *outbox* + worker em segundo plano (`IHostedService`/`Channel<T>`, ou o framework de
  jobs que a API já usar). Salvar a `Notification` e o item de envio na mesma transação.
- Não enviar se: usuário sem tokens, push desativado nas preferências, usuário banido/anonimizado,
  `pushEnabled=false`.

### 4.2 Formato do push (Expo)

Uma mensagem por token do destinatário (a Expo aceita lotes de até **100** mensagens por chamada):

```json
{
  "to": "ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]",
  "title": "Novo seguidor",
  "body": "Ana começou a seguir você",
  "sound": "default",
  "channelId": "default",
  "priority": "high",
  "ttl": 86400,
  "badge": 3,
  "data": {
    "v": 1,
    "notificationId": "guid",
    "type": "NewFollower",
    "actorUserId": "guid",
    "targetDescription": "User:guid"
  }
}
```

Textos em pt-BR, compostos no servidor:

| `type` | `title` | `body` |
|---|---|---|
| `NewFollower` | Novo seguidor | `{actorName} começou a seguir você` |
| `PhotoLiked` | Nova curtida | `{actorName} curtiu sua foto` |
| `PhotoCommented` | Novo comentário | `{actorName} comentou na sua foto` |
| `CommentReplied` | Nova resposta | `{actorName} respondeu seu comentário` |

- `data` é o que o app usa para abrir a tela certa (mesma lógica de `targetDescription` que o app já usa
  na lista). Manter `v` para evoluir o formato.
- `badge` = contagem de não lidas **depois** de criar a notificação (selo no ícone, iOS).
- `data` não deve carregar nada além de ids (nada sensível). O texto aparece na tela bloqueada: se
  preferirem, uma opção de configuração para um corpo genérico ("Você tem uma nova notificação").

### 4.3 Resultado e tokens inválidos

A Expo responde com *tickets*; os *receipts* (consultados depois, ~15 min) indicam falhas reais.

- `DeviceNotRegistered` (ticket ou receipt) → **apagar o token** imediatamente.
- Erros transitórios / `5xx` / `429` → repetir com *backoff* exponencial e teto de tentativas; depois
  descartar o push (a notificação in-app continua existindo).
- `MessageTooBig`, `InvalidCredentials` etc. → registrar (log) sem repetir; alertar se `InvalidCredentials`
  (credencial FCM/APNs do EAS errada).

### 4.4 Excesso de pushes (v1.1)

Muitas curtidas seguidas viram spam. Sugestões, da mais simples à mais elaborada:

1. Limite por usuário (ex.: no máximo N pushes por hora; o excedente vira só notificação in-app).
2. **Agrupar** por `(destinatário, type, alvo)` numa janela curta (ex.: 60 s): "Ana e mais 3 curtiram sua foto".

A decisão fica com a sessão da API; o app funciona com qualquer uma delas, pois o texto vem pronto.

## 5. Segurança, privacidade (LGPD) e operação

- O **push token é dado pessoal** (identificador de aparelho): citar na Política de Privacidade; apagar
  em exclusão de conta (item 3.3); **não logar o token inteiro** (mascarar, ex.: últimos 6 caracteres).
- Validar o formato do token e limitar tokens por usuário (3.1); registrar com rate limit.
- Configuração: `Push:Enabled` (feature flag), `Push:ExpoAccessToken` (*enhanced push security* da Expo —
  recomendado, mantido em segredo), URL base configurável. Em `Development` sem token real, **logar em
  vez de enviar** (análogo ao Mailpit para e-mails).
- Observabilidade: contadores de enviados / falhas / tokens removidos; incluir o `requestId` da origem nos
  logs do envio.
- Documentar no `docs/API.md` (seção "Notifications" + "Recent changes") e na tabela de status.

## 6. O que o app faz (para a API saber o que esperar)

1. Instala `expo-notifications`, pede a permissão (Android 13+: `POST_NOTIFICATIONS`) **em momento
   contextual**, não na abertura.
2. Após o login (e a cada abertura / mudança de token), chama `POST /users/me/devices`.
3. Antes do logout e na exclusão de conta, chama `DELETE /users/me/devices`.
4. Ao tocar no push (app aberto, em segundo plano ou fechado), abre a tela do alvo usando `data`
   (reaproveitando a navegação que já existe) e marca a notificação como lida
   (`POST /notifications/{id}/read`).
5. Cria o canal Android `default` e atualiza o selo.
6. Builds: precisa de **build nativo novo** e das credenciais FCM (Android) / APNs (iOS) no EAS — isso é
   do lado do app/infra, **não** da API.

## 7. Plano em fases

| Fase | API | App |
|---|---|---|
| 1 | `POST`/`DELETE /users/me/devices`, tabela de aparelhos, envio via Expo para os 4 gatilhos, limpeza de tokens (3.3, 4.3) | permissão, registro/remoção do token, abrir a tela ao tocar |
| 2 | preferências (3.4), agrupamento/limite (4.4), endpoint de teste (3.5) | tela de preferências, selo do ícone |
| 3 | novos tipos (ex.: aviso de suspensão de conta, resposta a denúncia), idioma por `locale` | — |

## 8. Perguntas em aberto para a sessão da API

1. Qual é o mecanismo de **jobs em segundo plano** já usado (ou preferido)? A API hoje roda em **uma**
   instância (Render): um `IHostedService` + fila em memória basta, ou prefere persistir o *outbox*?
2. Guardar o estado do envio na própria `Notification` (ex.: `PushSentAt`, `PushStatus`) ou em tabela
   separada?
3. **Janela de agrupamento** e **limite por hora** (4.4) — valores?
4. Textos dos pushes: ok os da tabela do item 4.2? O corpo genérico na tela bloqueada deve existir como
   opção?
5. Limite de aparelhos por usuário (sugerido 10) e rate limit de registro (sugerido 30 / 15 min).
6. Retenção: apagar tokens sem `LastSeenAt` há mais de N dias (ex.: 90)?
7. A conta de **Admin/Moderador** deve receber algum push específico (ex.: nova denúncia pendente)? Se
   sim, entra na fase 3.

### 8.1 Respostas da sessão da API (decisões tomadas)

Implementação da **fase 1** concluída no `dev` da API (contrato completo em `docs/API.md`, seção "Push notifications"). O push
fica **desligado em produção** (`Push__Enabled=false`) até o app estar pronto; registrar o token já funciona.

1. **Jobs em segundo plano.** `IHostedService` + fila em memória (mesmo padrão do aviso ao site), sem outbox completa. **Híbrido ("opção C")**:
   o envio é em memória (um reinício perde só aquele push; a notificação segue na lista do app), mas os *tickets* aceitos pela Expo ficam na
   tabela `push_tickets` para ler os *receipts* ~15 min depois e apagar tokens mortos (`DeviceNotRegistered`).
2. **Estado do envio.** Em tabelas separadas (`push_devices`, `push_tickets`); a `Notification` não ganhou colunas.
3. **Limite por hora.** **20 pushes por hora por pessoa**; o excedente vira só notificação no app. Agrupamento ("Ana e mais 3 curtiram") fica
   para a fase 2.
4. **Textos.** Os da tabela do item 4.2, sem alteração. Corpo genérico na tela bloqueada **não** existe na fase 1 (fica como opção futura se houver
   reclamação).
5. **Aparelhos por usuário.** **5** (não 10): ao registrar o sexto, sai o menos recente. Rate limit de registro: **30 / 15 min por IP**, como sugerido.
6. **Retenção.** Token sem registro há **90 dias** é apagado (job diário). Também é apagado em logout (`DELETE /users/me/devices`), exclusão de
   conta, anonimização e banimento.
7. **Moderação.** Push para admin/moderador (ex.: nova denúncia) fica para a fase 3, como proposto.

**Detalhes que o app precisa saber**
- `DELETE /users/me/devices` aceita `{ "token" }` **ou** `{ "installId" }`; chame **antes** do `POST /auth/logout`.
- Um mesmo token registrado por outra conta passa a pertencer à conta atual (aparelho compartilhado).
- O `badge` do push é a contagem de não lidas **depois** da nova notificação.
- O `data` do push tem exatamente `v`, `notificationId`, `type`, `actorUserId`, `targetDescription` (mesmos rótulos de `GET /notifications`).
- Fora da fase 1: preferências por tipo (3.4), endpoint de teste (3.5), agrupamento (4.4).

## 9. Critérios de aceite (fase 1)

- Registrar o mesmo token duas vezes é idempotente; o mesmo token registrado por outro usuário muda de dono.
- Curtir/comentar/seguir/responder gera **um** push no(s) aparelho(s) do destinatário em poucos segundos,
  **sem** push para o autor da ação.
- Falha ou lentidão da Expo **não** afeta a resposta de curtir/comentar/seguir.
- `DeviceNotRegistered` apaga o token; excluir a conta ou banir remove todos.
- `DELETE /users/me/devices` é idempotente; logout não deixa o aparelho recebendo push da conta antiga.
- Nenhum token completo aparece nos logs.
