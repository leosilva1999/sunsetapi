# Sunsetsss API — Reference for frontend integration

API .NET 9 (Clean Architecture) para o Sunsetsss: usuários pesquisam locais com belas visões de
pôr do sol, postam fotos marcando o local, curtem, comentam e avaliam os locais.

This document describes the API **as actually implemented**, for a client (React web app) to
integrate against. Field names below are copy-pasted from real request/response payloads, not
paraphrased.

## Recent changes

New since this doc was first handed over — if you already built against the earlier contract,
these are the diffs to check:

- **New `PUT /users/me/password`** (change password) and **`UserResponse.hasPassword`** (`boolean`) —
  see [Users](#users). `hasPassword` is `false` for accounts created through Google sign-in: hide
  the "Change password" entry for them.
- **`UserResponse.bio`** (`string | null`, ≤160 chars) — see [Users](#users).
- **`PATCH /users/me` is now a true partial update** (JSON Merge Patch semantics: omitted field =
  unchanged, `null` = clear, value = replace) — it used to require re-sending all three fields.
  ⚠️ its Swagger schema is wrong, see the note under [Users](#users).
- **Comment replies, one level deep**: `CommentResponse.parentCommentId` / `repliesCount`,
  `POST /photos/{id}/comments` takes an optional `parentCommentId`, new
  `GET /comments/{id}/replies` — see [Photos](#photos).
- **`PhotoResponse.commentsCount`** (roots + replies combined) — see [Photos](#photos).
- **User banning (Moderator/Admin)**: new `POST` / `DELETE /moderation/users/{id}/ban`; a banned
  account gets `403` on login (with `errors.reason` / `errors.bannedUntil`) and its sessions end
  immediately. `UserResponse` gains `isBanned` / `bannedUntil` — see [Moderation](#moderation).
- **Location moderation (Moderator/Admin)**: new `DELETE /moderation/locations/{id}` (hides the
  location **and its photos**), `POST .../restore`, `GET /moderation/locations/deleted` and
  `PATCH /moderation/locations/{id}/coordinates` (fix a wrong map pin) — see
  [Moderation](#moderation).
- **New `POST /moderation/users/{id}/anonymize` (Admin only)** — erases a user's account on
  behalf of moderation — see [Moderation](#moderation).
- **Reporting locations and users**: new `POST /locations/{id}/reports` and `POST /users/{id}/reports`
  (same body, rate limit and one-report-per-target rule as photos/comments), `ReportTargetType` gains
  `Location` and `User`, and `ReportReason` gains `IncorrectInfo` (e.g. wrong map pin) — see
  [Moderation](#moderation).
- **New `POST /moderation/locations/{id}/merge`** — merges a duplicate location into another — see
  [Moderation](#moderation).
- **CORS** is now configured (was previously missing/blocking) — see [CORS](#cors).
- **Ratings now carry a comment and have their own endpoints**: `RatingResponse` (score + comment
  + author), `GET /locations/{id}/ratings` (list), `GET /locations/{id}/ratings/me`, and
  `DELETE /locations/{id}/ratings`. ⚠️ **`POST /locations/{id}/ratings` changed its response type**
  from `LocationResponse` to `RatingResponse` — see [Locations](#locations).
- **New `POST /photos/upload-url`** — get a pre-signed URL, `PUT` the image to storage yourself,
  then pass the returned `imageUrl` to `POST /photos` — see [Photos](#photos).
- **`GET /locations?q=` search changed semantics and is now rate limited** — built for instant
  search (safe to call on every keystroke), it's backed by a FULLTEXT index for terms ≥3 chars,
  which means **prefix-per-word matching, not substring** (`"osa"` no longer matches "Rosa"); ⚠️
  if your search UI/tests assumed arbitrary substring matching, check the behavior change — see
  [Locations](#locations).
- **New `POST /users/me/avatar-upload-url`** — same pre-signed-upload pattern as
  `POST /photos/upload-url`: get a URL, `PUT` the image yourself, then pass the returned
  `avatarUrl` to `PATCH /users/me` — see [Users](#users).
- **`GET /users/{id}` no longer returns `email`** ⚠️ — it's a public, unauthenticated endpoint and
  `id` is exposed everywhere (photos/comments/ratings), so returning `email` there let anyone
  harvest any user's e-mail. `email` still comes back from authenticated contexts (login/register/
  refresh, `PATCH /users/me`) — see [Users](#users).
- **New `DELETE /users/me`** — account deletion (LGPD right to erasure). Anonymizes the user in
  place rather than cascade-deleting their photos/comments/ratings — see [Users](#users).
- **`PATCH /users/me`'s `avatarUrl` must now point into our own storage bucket** ⚠️ — it used to
  accept any well-formed absolute URL, which let a client set someone's avatar to an
  attacker-controlled tracking URL (every viewer's browser would then request it, leaking their
  IP). Only URLs from `POST /users/me/avatar-upload-url` are accepted now — see [Users](#users).
- **New moderation system**: `UserResponse.role` (`User`/`Moderator`/`Admin`), reporting
  (`POST /photos/{id}/reports`, `POST /comments/{id}/reports`), a moderator queue
  (`GET`/`PATCH /moderation/reports`), role promotion (`PATCH /moderation/users/{id}/role`,
  Admin-only), and terms of service (`GET`/`PUT /terms`, `PUT` Admin-only) — see
  [Moderation](#moderation). ⚠️ **`DELETE /photos/{id}` and `DELETE /comments/{id}` now also
  accept a Moderator/Admin caller**, not just the author — see the updated notes under
  [Photos](#photos).
- **First genuine `403 Forbidden` in the API** ⚠️ — previously *every* "not allowed" case was a
  `401` (see gotcha #1, now split in two). A `403` means "you're authenticated but your role
  doesn't allow this" (Moderator/Admin-only endpoints); it comes from ASP.NET Core's own
  authorization middleware, so its body is **not** this API's usual `{ title, errors }` shape —
  see [Error format](#error-format).
- **Deleted photos/comments are now soft-deleted, not removed** — `DELETE` still behaves the same
  from the client's perspective (`204`, then `404` on subsequent fetches), but the row is kept
  (with who deleted it and when) instead of erased, so a root comment's replies are no longer
  dropped via a database `CASCADE` — they're soft-deleted individually, same as before but at the
  application layer. No client-visible change unless you were relying on the old cascade timing.
- **New `GET`/`PUT /privacy`** (privacy policy), sharing the exact same versioned-document
  mechanism as `/terms`. ⚠️ **`/terms`'s response shape changed**: `TermsOfServiceResponse` is now
  the generic `LegalDocumentResponse` (adds a `documentType` field: `"TermsOfService"` or
  `"PrivacyPolicy"`), and each document type now versions independently instead of sharing one
  global counter — see [Moderation](#moderation).
- **New `GET /comments/{id}`** — a single-comment fetch that was missing (only listings existed),
  now returns `photoId` too — see [Photos](#photos).
- **New `GET /moderation/users?q=`** (Admin only) — the moderation UI had no way to find a user's
  id to promote/demote them; simple substring search over name/email — see
  [Moderation](#moderation).
- **New `GET /moderation/actions?cursor=&limit=`** (Moderator or Admin) — read side of the
  `ModerationAction` audit log, which was write-only until now — see [Moderation](#moderation).
- **New `POST /auth/google`** — sign in/up with a Google ID token, verified server-side against
  `GoogleAuth:ClientId`; returns the same `AuthResponse` shape as register/login — see
  [Auth](#auth).
- **New `POST /auth/forgot-password` / `POST /auth/reset-password`** — password recovery via a
  single-use, 30-minute emailed token; `forgot-password` always returns `204` regardless of
  whether the email exists (no account enumeration), and a successful reset revokes every refresh
  token for that user. Both are rate limited (5/15min per IP). In Development the email lands in
  Mailpit (`http://localhost:8025`), not a real inbox — see [Auth](#auth) and the README.
- **`PhotoResponse.city`** — the photo's location's city, denormalized the same way
  `locationName` already was, so a photo card can show "{user} em {location}, {city}" without an
  extra lookup — see [Photos](#photos).
- **New `GET /photos/count`** — total non-deleted photos, backing the "X pôr do sol catalogados"
  stat on the home page — see [Photos](#photos).
- **New "follow users" feature**: `PublicUserResponse` gains `followersCount`, `followingCount`,
  `isFollowedByCurrentUser`; new `POST`/`DELETE /users/{id}/follow`,
  `GET /users/{id}/followers`, `GET /users/{id}/following` — see [Users](#users).
- **New notifications bell (polling-based)**: liking/commenting on someone else's photo, replying
  to someone else's comment, or following someone now writes a `Notification` for the recipient.
  New `GET /notifications`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`,
  `POST /notifications/read-all` — see [Notifications](#notifications). No real-time push (no
  WebSocket/SignalR) — the frontend bell is expected to poll `unread-count` periodically.

- **"Perto de mim" (`GET /locations?lat=&lng=&radius=`)** — now a real nearby search: exact
  (Haversine) distance, results ordered **nearest-first**, and each `LocationResponse` carries a new
  `distanceKm`. ⚠️ `radius` is now optional (defaults to 50 km, max 200) and `lat`/`lng` are now
  **validated** (`400` if only one is sent, out of range, or `radius` is ≤0 / >200) — they used to be
  silently ignored unless all three arrived together. See [Locations](#locations).

- **Error responses now carry a `requestId`** ⚠️ — every `{ "title", "errors" }` error body (and the
  `429` one) gains a third field, `"requestId"`, and **every** response (success or error, including
  the empty-bodied `401`/`403`) carries the same value in an `X-Request-Id` header. It's the key to
  the server-side logs, so show it (or put it in a "copy details" action) on unexpected-error
  screens and include it when reporting a bug. Additive: clients that ignore unknown fields keep
  working; strict/typed clients need the new field. See [Error format](#error-format).

## Base URL & running locally

- All routes are prefixed with **`/api/v1`**.
- Local dev (via Visual Studio / `dotnet run --project src/Sunset.API`): `http://localhost:5256` or `https://localhost:7044`.
- Swagger UI (Development only): `/swagger`. Raw OpenAPI doc: `/openapi/v1.json`.
- Content type: `application/json` for all request/response bodies. No API envelope — responses
  are the resource (or an array/page object) directly.
- JSON casing: **camelCase** for all fields (System.Text.Json default).
- Dates/times: ISO 8601 strings. Most `createdAt` fields are UTC without offset
  (e.g. `"2026-09-04T18:08:52.6071314Z"`); the sunset-time endpoint returns local
  offset-aware timestamps (e.g. `"2026-09-04T17:44:06-03:00"`).

### Local dev seed data

The API auto-seeds fictitious data on startup in Development (idempotent — skipped if already
seeded). 8 users, 6 real Brazilian sunset spots, 12 photos, likes/comments/ratings, plus an
initial terms-of-service and privacy policy (each version 1). All seed users share the password **`Password123!`**.
Emails: `beatriz@sunsetapp.dev`, `rafael@sunsetapp.dev`, `camila@sunsetapp.dev`,
`lucas@sunsetapp.dev`, `juliana@sunsetapp.dev`, `pedro@sunsetapp.dev`, `mariana@sunsetapp.dev`,
`thiago@sunsetapp.dev`. Use any of these to log in during frontend development instead of
registering a throwaway account each time. `beatriz@sunsetapp.dev` seeds as a **Moderator** and
`thiago@sunsetapp.dev` as an **Admin**, so the moderation endpoints (see
[Moderation](#moderation)) are testable without a manual role promotion.

## Authentication

JWT bearer. `POST /auth/register` and `POST /auth/login` return an **access token** (short-lived,
15 min by default) and a **refresh token** (long-lived, 30 days by default, opaque random
string — not a JWT). Send the access token on every authenticated request:

```
Authorization: Bearer <accessToken>
```

- Refresh tokens are **rotated**: calling `POST /auth/refresh` returns a new access+refresh pair
  and invalidates the old refresh token. Store only the newest refresh token.
- `POST /auth/logout` revokes a refresh token server-side (pass the refresh token, not the access
  token). After logout, that refresh token can no longer be used to get new access tokens — but
  any already-issued access token remains valid until it naturally expires (no server-side
  access-token revocation).
- There is no endpoint to fetch "my" user by a special alias — decode the JWT's `sub` claim
  (the user id) or keep the `user` object returned by register/login/refresh, and call
  `GET /users/{id}` when you need fresh profile data.
- <a id="banned-accounts"></a>**Banned accounts.** A moderator can ban a user (see [Moderation](#moderation)).
  The ban takes effect **at once**, not when the access token expires: the user's refresh tokens are
  revoked and the access token they hold is rejected on the next request with a plain `401`. The
  client therefore ends up on the login screen, where `POST /auth/login` (and `/auth/google`) answers
  `403` instead of the usual `401`:
  ```json
  { "title": "This account is banned.", "errors": { "reason": ["Spam repetido"], "bannedUntil": ["2026-11-05T12:00:00.0000000Z"] }, "requestId": "..." }
  ```
  `errors.reason` is the moderator's message; `errors.bannedUntil` is only present for a temporary ban
  (a permanent ban has no such key). A wrong password still gets the ordinary `401`, so a `403` is only
  ever shown to someone who proved they own the account. A temporary ban lifts itself at `bannedUntil`.
  A user removed through [anonymize](#moderation) is also (permanently) banned, but cannot sign in
  anyway because the e-mail and password were erased.
- Endpoints not marked **🔒 auth** still read a bearer token if one is sent (e.g. to compute
  `likedByCurrentUser` on photos), but work fine anonymously too.

## CORS

Configured via `Cors:AllowedOrigins` (a string array) in config — empty by default, meaning
**no cross-origin browser calls are allowed unless an environment explicitly lists origins**.
`appsettings.Development.json` pre-populates `http://localhost:3000` and `http://localhost:5173`
(CRA/Vite defaults). If your React dev server runs on a different port, add it there — the
policy allows any header/method for listed origins, no credentials/cookies involved (auth is a
bearer header, not a cookie, so `credentials: 'include'` isn't needed on fetch calls).

## Common conventions

### Pagination (cursor-based)

Feed-style list endpoints (`/photos`, `/photos/{id}/comments`, `/comments/{id}/replies`,
`/locations` search, `/locations/{id}/photos`, `/users/{id}/photos`) return:

```json
{
  "items": [ /* ... */ ],
  "nextCursor": "opaque-string-or-null",
  "hasMore": true
}
```

Pass `nextCursor` back as `?cursor=` to get the next page. Treat the cursor as an opaque token —
don't parse or construct it. `?limit=` is accepted on all of them, default `20`, clamped to
`1–50` server-side (accepted `?limit=200` will silently become `50`, no error).

`GET /locations/ranking` is the one list endpoint that is **not** paginated — it returns a plain
array, capped by `?limit=` (default 20, same 1–50 clamp).

### Error format

Non-2xx responses are:

```json
{ "title": "human-readable message", "errors": { "FieldName": ["message"] } | null, "requestId": "0HNP7OBIF010S:00000001" }
```

`requestId` is an opaque id for the request (do not parse it); the exact same value is in the
`X-Request-Id` response header of **every** response, which is the only place it appears when the
body is empty (`401` from a missing/invalid token, `403`). The browser exposes that header to
JavaScript (`Access-Control-Expose-Headers`). Sending your own `X-Request-Id` has no effect - the
server always generates it. The server writes it on every log line of the request, so quoting it
is enough to find what happened.

`errors` is populated (grouped by field name) only for `400` validation failures; every other
error status has `errors: null` and a single message in `title`.

| Status | Meaning |
|---|---|
| 400 | Request failed FluentValidation rules (see `errors` for per-field messages) |
| 401 | Missing/invalid/expired bearer token, invalid login/refresh/reset-password credentials, "not the owner" (see gotcha below), or a **banned** account's still-unexpired access token (see [Banned accounts](#banned-accounts)) |
| 403 | Authenticated, but not allowed: the role is missing for a Moderator/Admin-only endpoint, **or** the API's own rule said no — a **banned account logging in**, or a moderation action on someone it can't touch (yourself, an Admin, a Moderator when you aren't an Admin). See the caveat below for the body |
| 404 | Referenced resource (user/location/photo/comment/report) doesn't exist — or was soft-deleted (a deleted location/photo/comment, an anonymized user as a report target) |
| 409 | Conflict — "email already registered", "you already reported this", "can't follow yourself", "can't report yourself", "location not deleted" (restore), "can't merge a location into itself", "user already deleted" (anonymize) |
| 502 | An upstream call failed — sunrise-sunset.org (see Locations → sunset), or SMTP for `POST /auth/forgot-password` |
| 500 | Unhandled server error |

Model-binding failures (e.g. malformed GUID in the URL, malformed `?date=`) short-circuit to a
`400` with ASP.NET Core's default `ProblemDetails` shape instead — don't rely on the `title`
field being present in that specific case. **A `403` has two possible bodies**:
- a **role** failure comes from ASP.NET Core's authorization middleware before this API's code ever
  runs, so the body is whatever ASP.NET Core's default is (typically empty), not `{ title, errors }`;
- the API's **own** `403` (banned login, "you cannot act on yourself", "admins cannot be banned or
  removed", "only an Admin can act on a Moderator") uses the normal `{ title, errors, requestId }`
  shape. So read `title` only if the body is non-empty, and always fall back to the status code.

### Auth requirement legend

🔒 = requires `Authorization: Bearer <token>`. Endpoints without 🔒 are public (some
optionally read the token if present, noted inline).

🐌 = **rate limited** beyond the norm (`GET /locations` search, `POST .../reports`,
`POST /auth/forgot-password`/`reset-password` — see each for
the exact limit). Every endpoint is implicitly subject to normal infrastructure-level limits; 🐌
marks the ones with a specific, tighter, documented policy worth knowing about.

🛡️ = requires a **role** on top of being authenticated (Moderator or Admin — noted inline which).
Missing the role → `403`, not `401` (see [Error format](#error-format)).

---

## Health

Public, no auth, outside the `/api/v1` prefix. Meant for load balancers / uptime monitors.

- `GET /health` — **liveness**: `200` as long as the process is up; touches no dependency.
- `GET /health/ready` — **readiness**: also checks the database; `200` when healthy, `503` when the
  database is unreachable. Note: with a serverless database (Neon) the first call after idle can take
  a few seconds while it wakes up.

Body (no error details are exposed): `{ "status": "Healthy|Unhealthy", "totalDurationMs": 12,
"checks": [{ "name": "database", "status": "Healthy", "durationMs": 10 }] }`

## Auth

### `POST /auth/register`
Body: `{ "name": string, "email": string, "password": string }`
Validation: `name` required ≤100 chars · `email` required, valid format, ≤256 chars ·
`password` required, ≥8 chars.

### `POST /auth/login`
Body: `{ "email": string, "password": string }`. `401` for bad credentials; `403` for a banned account
(see [Banned accounts](#banned-accounts)) — the same `403` applies to `POST /auth/google`.
`POST /auth/refresh` answers a plain `401` for a banned user (the ban revoked their refresh tokens).

### `POST /auth/google`
Body: `{ "idToken": string }` — an ID token from Google Identity Services (frontend), verified
server-side against `GoogleAuth:ClientId` (audience, signature, expiry). `401` if the token is
invalid or its email isn't verified by Google. Finds an existing user by the token's email, or
creates one with a random unusable password (an account created this way has no password of its
own, so `POST /auth/login` will never succeed for it — only `/auth/google` will). If a
password-based account already exists with that email, logs into it instead of creating a
duplicate.

Register, login and google return:
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "NeX9jEIO...",
  "expiresAt": "2026-09-04T18:23:24.59Z",
  "user": { "id": "guid", "name": "string", "email": "string", "avatarUrl": "string|null", "bio": "string|null", "role": "User", "createdAt": "date", "hasPassword": true, "isBanned": false, "bannedUntil": null }
}
```

### `POST /auth/refresh`
Body: `{ "refreshToken": string }` → same `AuthResponse` shape as above, with a new pair.

### `POST /auth/logout`
Body: `{ "refreshToken": string }` → `204 No Content`. Idempotent (calling it twice, or with an
already-revoked token, still returns `204`).

### 🐌 `POST /auth/forgot-password`
Body: `{ "email": string }` → **always `204`**, whether or not that email has an account —
responding differently would let a caller enumerate registered emails by probing this endpoint.
If the account exists, emails it a link (`{Frontend:BaseUrl}/reset-password?token=...`) built
around a single-use, 30-minute-lived token. Sending the email is synchronous with the request;
if the SMTP server is unreachable, this returns `502` instead (see `ExternalServiceException` in
[Error format](#error-format)). Rate limited: 5 requests/15min per IP — much tighter than
`Search`/`Reports`, since every call (even for an unknown email) triggers an outbound email.
In Development, the email lands in [Mailpit](http://localhost:8025) instead of a real inbox —
see the README.

### 🐌 `POST /auth/reset-password`
Body: `{ "token": string, "newPassword": string }` — `newPassword` same rule as registration
(≥8 chars). → `204` on success; `401` if the token is invalid, expired, or already used (tokens
are single-use — a second call with the same token, even a valid one already spent, is a `401`).
On success, **every refresh token for that user is revoked** (same as `DELETE /users/me`) — every
already-logged-in session is forced to log in again, since whoever reset the password isn't
provably the account owner just by having clicked an emailed link.

---

## Users

### `GET /users/{id}`
Public (no auth, but reads the bearer token if present to resolve `isFollowedByCurrentUser`) -
`id` is exposed everywhere (photos/comments/ratings), so this must not leak `email`. →
`{ "id", "name", "avatarUrl", "bio", "followersCount", "followingCount",
"isFollowedByCurrentUser", "createdAt" }`. `bio` is `null` until the user sets one.
`isFollowedByCurrentUser` is `false` for an anonymous caller or when the caller follows no one.
⚠️ **No `email` field** - that only ever comes back in an authenticated context (see
below), never from this public lookup. Before 2026-09-23 this endpoint also returned `email`;
if you cached/typed against the old shape, drop `email` from it.

### 🔒 `POST /users/{id}/follow` / 🔒 `DELETE /users/{id}/follow`
**Idempotent.** Following an already-followed user, or unfollowing one you don't follow, is a
silent no-op → `204` either way. Following yourself → `409 Conflict`. On a genuine new follow,
`followersCount`/`followingCount` on both profiles update immediately and the followee gets a
`NewFollower` notification (see [Notifications](#notifications)).

### `GET /users/{id}/followers?cursor=&limit=` / `GET /users/{id}/following?cursor=&limit=`
Users who follow `{id}`, and users `{id}` follows, respectively — both paginated, newest-follow-
first. → `CursorPagedResult<PublicUserResponse>` (same shape as `GET /users/{id}` above).
⚠️ **`isFollowedByCurrentUser` is always `false` on these two listings**, same caveat as
`likedByCurrentUser` on `/users/{id}/photos` (see [gotchas](#known-gotchas-for-the-frontend)) —
not wired up here, only on the single-user `GET /users/{id}` lookup.

### 🔒 `PATCH /users/me`
Updates the **authenticated** user's own profile (no `{id}` in the URL — resolved from the
token). Body: `{ "name"?: string, "avatarUrl"?: string | null, "bio"?: string | null }`.

**True partial update (JSON Merge Patch, RFC 7396 style)**: only the fields present in the JSON
body are touched.
- **Omit a field entirely** → left unchanged. `PATCH` with `{}` is a valid no-op.
- **Send a field as `null`** → clears it (only meaningful for `avatarUrl`/`bio`; `name` can't be
  null — see validation below).
- **Send a field with a value** → replaces it.

So to change only the bio, send `{ "bio": "new bio" }` — no need to re-send `name`/`avatarUrl`.

⚠️ **Swagger/OpenAPI's schema for this endpoint is wrong** — it shows `name`/`avatarUrl`/`bio` as
all `required` with an opaque `OptionalOfstring` type, because the presence-vs-absence
distinction (via a custom `Optional<T>` JSON converter) isn't something OpenAPI schema generation
understands. Trust this doc's description of the body, not the schema shown in `/swagger`.

Validation (only runs on fields actually present in the body): `name`, if present, must be
non-empty ≤100 chars (i.e. you can omit `name`, but you can't send it as `null` or `""`) ·
`avatarUrl`, if present and non-null, must be a valid absolute URL ≤2048 chars **and must point
into our own storage bucket** (i.e. start with the same base URL `POST
/users/me/avatar-upload-url` returns) — `400` otherwise. In practice this means the only way to
set `avatarUrl` is: call `POST /users/me/avatar-upload-url`, `PUT` the image there, then send the
`avatarUrl` you got back here verbatim · `bio`, if present and non-null, ≤160 chars.
→ updated `UserResponse`.

### 🔒 `PUT /users/me/password`
Changes the **authenticated** user's own password. Body:
`{ "currentPassword": string, "newPassword": string, "refreshToken"?: string }` → `204`.

- `newPassword` must be ≥8 characters (same rule as register/reset) and different from the
  current one.
- **Sessions:** every refresh token of the user is revoked **except** the one sent in
  `refreshToken` (send the caller's current one so this device stays signed in). If it is
  omitted — or isn't an active token of this user — **all** sessions are revoked, this device's
  included, and the app must send the user back to the login screen. Already-issued access
  tokens stay valid until they expire (≤15 min).
- A notice email ("sua senha foi alterada") goes to the account address; a failure to send it
  does not fail the request.
- Errors (all with the usual `{ title, errors, requestId }` body):
  - `400` with `errors.CurrentPassword` — the current password is wrong. ⚠️ This is a `400`, not
    a `401`, on purpose: don't treat it as an expired session (no refresh/sign-out) — show it
    inline on the field.
  - `400` with `errors.NewPassword` — too short, or equal to the current password.
  - `409` — the account has no password (`hasPassword: false`, i.e. Google sign-in). They can
    set one through `POST /auth/forgot-password`, after which `hasPassword` becomes `true`.
  - `401` — missing/invalid access token · `429` — rate limited (5 attempts per 15 min per IP).

**UI suggestion:** put "Alterar senha" in the profile screen's kebab (⋮) menu rather than in the
edit-profile form — it opens a small modal/screen with current / new / confirm fields (the
confirmation field is client-side only). Hide the entry when `user.hasPassword` is `false`.

### 🔒 `POST /users/me/avatar-upload-url`
Same pre-signed-upload pattern as `POST /photos/upload-url` (see [Photos](#photos)), just for
avatars instead of photo images. Body: `{ "contentType": string }` — only `image/jpeg` or
`image/png`.
→ `{ "uploadUrl": string, "avatarUrl": string }`. `PUT` the image bytes directly to `uploadUrl`
(no auth header, `Content-Type` must match what you requested); `uploadUrl` expires in 5 minutes.
Once the `PUT` succeeds, send `avatarUrl` to `PATCH /users/me` above to actually set it on the
profile — this endpoint only generates the URL, it doesn't touch the user record itself.

### 🔒 `DELETE /users/me`
Deletes the **authenticated** user's own account (LGPD Art. 18, IX - right to erasure). This
**anonymizes, it does not cascade-delete**: `name` becomes `"Usuário excluído"`, `email` becomes
an unrecoverable placeholder, `avatarUrl`/`bio` are cleared, and the password hash is replaced
with an unusable one - but the row (and everything FK'd to it: photos, comments, ratings, likes)
stays. `userName`/`userAvatarUrl` on those are resolved live from the user, so every photo/
comment/rating this person made shows up as authored by "Usuário excluído" from then on, instead
of disappearing or taking other people's replies/likes down with it. All the user's refresh
tokens are revoked (further `POST /auth/refresh` calls with them → `401`); any not-yet-expired
access token stays valid until it naturally expires, same caveat as `POST /auth/logout` above.
→ `204 No Content`. No body, no `{id}` in the path (always the caller's own account - there's no
way to delete another user's account through this endpoint).

### `GET /users/{id}/photos?cursor=&limit=`
Paginated photos authored by that user. → `CursorPagedResult<PhotoResponse>` (see Photos for
shape). **Note:** `likedByCurrentUser` is always `false` here regardless of the caller's token —
not wired up for this listing (only `/photos` feed and `/photos/{id}` resolve it correctly).

---

## Locations

### 🐌 `GET /locations?q=&lat=&lng=&radius=&cursor=&limit=`
Search/browse — safe to call on every keystroke for a live/instant-search box (still debounce on
the frontend; see rate limit below regardless). All query params optional.

- `q` search behavior **depends on term length**, because the ≥3-character path is backed by a
  PostgreSQL full-text index (`tsvector` + GIN over `Name`, `City`, `simple` config — no stemming):
  - **≥3 characters**: full-text search, **per-word prefix match** — each word in `q`
    must match the *start* of a word in `Name` or `City`. `"jeric"` matches "**Jeric**oacoara";
    `"praia rosa"` matches "**Praia** do **Rosa**" (multi-word is AND, order-independent). ⚠️
    **This is not a substring match** — `"osa"` will *not* match "Rosa" (only a match starting
    with "osa" would), unlike a naive `LIKE '%osa%'`. Punctuation/symbols in `q` are stripped
    before searching (so `q` is safe to pass through as-is, no need to sanitize client-side).
    Matching is case-insensitive but **accent-sensitive** (`"conceicao"` won't match "Conceição").
  - **<3 characters**: falls back to a plain case-insensitive `ILIKE '%q%'` substring scan (real substring match,
    anywhere in the word) — short queries can't use the index either way, so there's no downside
    to substring semantics there, and it keeps single/double-letter input from returning nothing.
- **"Near me" mode — `lat`+`lng` (+ optional `radius` in km):** `lat`/`lng` must be sent together
  (`lat` ∈ [-90,90], `lng` ∈ [-180,180]), otherwise `400`. `radius` defaults to **50** km and must be
  in (0, 200]. The filter uses the exact great-circle (Haversine) distance — a circle, not a box —
  and results come back **ordered by distance, nearest first** (not by recency, unlike the plain
  listing). It combines with `q`: the text filter is applied too, still ordered by distance. A
  `radius` sent without `lat`/`lng` is a `400`, not silently ignored.
- In near-me mode every item has `distanceKm` (km, rounded to 2 decimals); outside it, `distanceKm`
  is `null`. The `nextCursor` of a near-me page is only valid for the same `lat`/`lng`/`radius`/`q`
  it came from — don't reuse it if the user's position changes, restart from the first page.
- Getting the coordinates is the frontend's job: `navigator.geolocation.getCurrentPosition` (needs
  HTTPS or `localhost`, and the user may deny permission — fall back to the normal listing or to a
  city search). The API never sees or stores the user's position beyond that single request.

→ `CursorPagedResult<LocationResponse>`.

🐌 = **rate limited**: 40 requests per rolling 10-second window, partitioned by client IP. Over
the limit → `429 Too Many Requests`, `Retry-After: 10` header, body
`{"title": "Too many search requests. Please slow down.", "errors": null}`. This is independent
of frontend debouncing — it's a floor to protect the DB even if debounce is misconfigured, buggy,
or bypassed, so don't rely on 429s as your only signal to slow down; debounce first regardless.

### `GET /locations/ranking?period=all&limit=20`
`period` is one of `week` | `month` | `all` (case-insensitive; default `all`), ranking by average
rating **within that window** (not the location's all-time `avgRating` field, though that field
is still what's returned per location). → plain array of `LocationResponse` (not paginated).

### `GET /locations/{id}`
→ `{ "id", "name", "latitude", "longitude", "city", "avgRating", "createdAt", "distanceKm" }`
(`distanceKm` is always `null` outside the near-me search above). `avgRating` is a
decimal (e.g. `4.33`), 0 for a location with no ratings yet. `latitude`/`longitude` are exactly
what a map embed needs (e.g. Google Maps Embed's `q={latitude},{longitude}`) — always validated
on creation (-90..90 / -180..180), never missing — see `docs/proposal-location-map-embed.md` for
the frontend spec.

### 🔒 `POST /locations`
Body: `{ "name": string, "latitude": number, "longitude": number, "city": string }`
Validation: `name` required ≤150 · `city` required ≤100 · `latitude` ∈ [-90,90] · `longitude` ∈ [-180,180].
→ `201 Created` with `LocationResponse`, `Location` header pointing to `GET /locations/{id}`.

### `GET /locations/{id}/photos?cursor=&limit=`
Same `likedByCurrentUser`-always-`false` caveat as `/users/{id}/photos` above.

### `GET /locations/{id}/sunset?date=YYYY-MM-DD`
Sunset/sunrise time for the location's coordinates, live from **sunrise-sunset.org** (no auth,
no key, called on every request — no caching). `date` optional (defaults to today).
```json
{
  "date": "2026-09-04",
  "tzId": "America/Fortaleza",
  "utcOffset": "-03:00",
  "sunrise": "2026-09-04T05:38:00-03:00",
  "sunset": "2026-09-04T17:44:06-03:00",
  "solarNoon": "2026-09-04T11:41:03-03:00",
  "dayLengthSeconds": 43566
}
```
If the upstream call fails, this returns `502` — **the frontend must handle this endpoint
failing independently of the rest of the location data being fine.** sunrise-sunset.org also
contractually requires a visible attribution link back to their site wherever this data is
shown — that's on the frontend to add, the API doesn't inject it.

### `GET /locations/{id}/ratings?cursor=&limit=`
All ratings for the location, newest-first. → `CursorPagedResult<RatingResponse>`:
```json
{
  "id": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null",
  "score": 5, "comment": "string|null", "createdAt": "date"
}
```
`comment` is `null` when the user rated without writing anything — it's optional, not required.

### 🔒 `GET /locations/{id}/ratings/me`
The **authenticated** user's own rating for this location. `404` if they haven't rated it yet —
use that to decide between showing "Rate this place" vs. "Edit your rating" in the UI. → single
`RatingResponse`.

### 🔒 `POST /locations/{id}/ratings`
Body: `{ "score": 1-5, "comment": string | null }`. `comment` is optional (omit or send `null` for
a stars-only rating), ≤1000 chars when present.
**Upsert semantics**: calling it again for the same user+location updates the existing rating
(both score and comment are replaced — this one is NOT a partial update like `PATCH /users/me`,
sending `{"score": 3}` after previously rating with a comment **clears** the comment) rather than
erroring or creating a duplicate.
→ `RatingResponse` for the rating just created/updated. **This used to return `LocationResponse`
— if you need the location's updated `avgRating` after rating, re-fetch `GET /locations/{id}`.**

### 🔒 `DELETE /locations/{id}/ratings`
Removes the **authenticated** user's own rating for this location and recalculates the location's
`avgRating`. `404` if they hadn't rated it. → `204`. No `{ratingId}` in the path — a user has at
most one rating per location, so there's nothing to disambiguate.

---

## Photos

### `GET /photos?sort=recent&cursor=&limit=`
`sort` is `recent` (default) | `top` (by `likesCount`, then recency).
```json
{
  "items": [{
    "id": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null",
    "locationId": "guid", "locationName": "string", "city": "string",
    "imageUrl": "string", "caption": "string|null",
    "likesCount": 5, "likedByCurrentUser": true, "commentsCount": 3,
    "createdAt": "date"
  }],
  "nextCursor": "...", "hasMore": true
}
```
`likedByCurrentUser` correctly reflects the caller's bearer token here (or `false` if anonymous).
`commentsCount` is the **total conversation size**, root comments plus replies combined — not
just root comments. It's on every `PhotoResponse` (feed, `GET /photos/{id}`, `POST /photos`,
etc.), denormalized and kept in sync on every comment/reply create or delete, including the
cascade delete case (see `DELETE /comments/{id}` below).

### `GET /photos/count`
→ `{ "count": 18402 }` — total non-deleted photos. Cheap `COUNT(*)` over an indexed column; used
by the frontend to show a real number on the home page instead of a hardcoded one.

### 🔒 `POST /photos/upload-url`
Body: `{ "contentType": string }` — only `image/jpeg` or `image/png`.
→ `{ "uploadUrl": string, "imageUrl": string }`. `PUT` the image bytes directly to `uploadUrl`
(no auth header, `Content-Type` must match what you requested — the pre-signed URL is locked to
it) — that request goes straight to storage, not through this API. `uploadUrl` expires in 5
minutes. Once the `PUT` succeeds, `imageUrl` is the permanent URL to send to `POST /photos` below.
`POST /photos` rejects (`400`, `errors.ImageUrl`) any `imageUrl` that wasn't generated by this endpoint
(i.e. outside our bucket), same rule as the avatar.
In dev this is LocalStack (`docker compose up` from the repo root); see [Local dev
seed data](#local-dev-seed-data)'s neighboring setup notes in `CLAUDE.md` for the bucket details.

### 🔒 `POST /photos`
Body: `{ "locationId": "guid", "imageUrl": string, "caption": string | null }`.
**The client uploads the image to storage itself first (via `POST /photos/upload-url` above) and
only sends the resulting URL here — this endpoint never accepts binary/multipart data.**
Validation: `locationId` required · `imageUrl` required, valid absolute URL, ≤2048 · `caption` ≤500.
404 if `locationId` doesn't exist. → `201 Created` with `PhotoResponse`.

### `GET /photos/{id}`
→ single `PhotoResponse`, same shape as feed items, with `likedByCurrentUser` resolved.

### 🔒 `DELETE /photos/{id}`
Author **or** a Moderator/Admin caller → `204`; anyone else → `401` (see gotcha #1 — this
particular check still isn't a `403`, unlike the newer Moderator/Admin-only endpoints). Soft
delete under the hood (row kept, just hidden) — no visible difference to the client, but see
[Moderation](#moderation) if you're a moderator deleting someone else's content.

### 🔒 `POST /photos/{id}/likes` / 🔒 `DELETE /photos/{id}/likes`
**Idempotent.** Liking an already-liked photo, or unliking one you haven't liked, is a silent
no-op → `204` either way, `likesCount` unchanged. No "already liked" error to handle.

### `GET /photos/{id}/comments?cursor=&limit=`
**Root comments only** (`parentCommentId == null`) — replies are fetched separately, see below.
Ordered newest-first. → `CursorPagedResult<CommentResponse>`:
```json
{
  "id": "guid", "photoId": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null",
  "content": "string", "createdAt": "date",
  "parentCommentId": "guid|null", "repliesCount": 5
}
```
`parentCommentId` is always `null` in this listing (roots only). `repliesCount` — direct replies
to that comment; use it to decide whether to show a "View N replies" affordance before fetching
them.

### `GET /comments/{id}`
Single comment fetch (root or reply) — same `CommentResponse` shape as above, `photoId` included
so you can link back to the photo it's on without already knowing it (useful from a moderation
context, where a `ReportResponse` for a `Comment` only gives you `targetId`). `404` if it doesn't
exist (or was deleted).

### 🔒 `POST /photos/{id}/comments`
Body: `{ "content": string, "parentCommentId": "guid" | null }`.
- `content`: required, ≤1000 chars.
- `parentCommentId` (optional, omit or `null` for a root comment): **one level of nesting only**
  — it must reference an existing **root** comment on the *same photo*. Referencing a comment
  from a different photo, or one that doesn't exist, → `404`. Referencing a comment that is
  itself a reply (i.e. trying to reply to a reply) → `409 Conflict`.
→ `200 OK` with `CommentResponse` (not `201` — no `Location` header, unlike photo/location
creation).

### `GET /comments/{id}/replies?cursor=&limit=`
Direct replies to root comment `{id}`, **oldest-first** (chronological, like a conversation —
opposite order from the main comments listing). `{id}` must be a root comment; if it's itself a
reply, or doesn't exist, → `404` (both cases look the same: "no replies list for this id"). →
`CursorPagedResult<CommentResponse>`, same shape as above (`repliesCount` is always `0` on a
reply — no second level of nesting).

### 🔒 `DELETE /comments/{id}`
**Note the path** — this is *not* nested under `/photos/{photoId}/comments/{id}`, it's its own
top-level `/api/v1/comments/{id}`. Author **or** a Moderator/Admin caller → `204`; anyone else →
`401`. Soft delete under the hood, same as photos.
- Deleting a **reply** decrements its parent's `repliesCount`.
- Deleting a **root comment that has replies cascades** — all its replies are soft-deleted too,
  regardless of who authored them. There's no confirmation step or "orphan" state; the frontend
  should warn the user before deleting a root comment that has `repliesCount > 0`.

---

## Moderation

Roles (`UserResponse.role`): `User` (default), `Moderator`, `Admin`. Both come back in the
`user` object from register/login/refresh, and from `PATCH /users/me` — decide whether to show
moderator UI off of that, no need to decode the JWT yourself. Every Moderator/Admin-only endpoint
below is 🛡️-marked; missing the role → `403`, not `401` (see [Error format](#error-format)).

### Moderation at a glance

Everything a moderator can do, who may do it, and whether it can be undone. Details for each
endpoint follow in this section.

| Action | Endpoint | Who | Reversible? |
|---|---|---|---|
| Delete a photo / comment | `DELETE /photos/{id}` · `DELETE /comments/{id}` | Author, Moderator, Admin | No (soft delete, no restore endpoint) |
| Report photo / comment / location / user | `POST /photos|comments|locations|users/{id}/reports` | Any logged-in user | — |
| Work the report queue | `GET /moderation/reports` · `PATCH /moderation/reports/{id}` | Moderator, Admin | Closing a report doesn't touch the target |
| **Ban** a user (permanent or until a date) | `POST /moderation/users/{id}/ban` | Moderator (users only), Admin (users and Moderators) | **Yes** — `DELETE .../ban` |
| **Unban** | `DELETE /moderation/users/{id}/ban` | same as ban | — |
| **Remove** a user's account (erase personal data, optionally hide their content) | `POST /moderation/users/{id}/anonymize` | **Admin only** | **No** |
| **Delete** a location (its photos are hidden with it) | `DELETE /moderation/locations/{id}` | Moderator, Admin | **Yes** — `POST .../restore` |
| List deleted locations (to restore) | `GET /moderation/locations/deleted` | Moderator, Admin | — |
| **Fix a location's map pin** | `PATCH /moderation/locations/{id}/coordinates` | Moderator, Admin | Edit it again (the old value is in the audit log) |
| **Merge** a duplicate location into another | `POST /moderation/locations/{id}/merge` | Moderator, Admin | **No** (restoring the duplicate gives an empty location) |
| Promote / demote a user | `PATCH /moderation/users/{id}/role` | Admin | Yes |
| Find users (to act on them) | `GET /moderation/users?q=` | Admin | — |
| Edit Terms / Privacy | `PUT /terms` · `PUT /privacy` | Admin | New version, old ones kept |
| Read the audit log | `GET /moderation/actions` | Moderator, Admin | — |

Rules that apply to every action **on a user** (ban, unban, anonymize): you can't act on yourself,
**nobody can act on an Admin**, and a **Moderator can only be acted on by an Admin** → otherwise `403`.
Every action in the table that changes something is written to the audit log.

**Typical flow for a moderation screen:** list `GET /moderation/reports` → open the target (photo,
comment, location or user — the report only carries `targetType` + `targetId`, so fetch the target to
show its name) → act on it (delete / ban / fix / merge / anonymize) → close the report with
`PATCH /moderation/reports/{id}`.

⚠️ **There is no endpoint to create the first Admin.** `PATCH /moderation/users/{id}/role`
requires an existing Admin caller, so bootstrapping one has to happen outside the API — in
Development this is `DbSeeder` (see below); in any other environment, a direct database write
(`UPDATE users SET Role = 2 WHERE Email = '...'` — `0` = User, `1` = Moderator, `2` = Admin).

### 🔒🐌 `POST /photos/{id}/reports` / 🔒🐌 `POST /comments/{id}/reports`
Body: `{ "reason": "Spam" | "Inappropriate" | "Harassment" | "Other" | "IncorrectInfo", "details"?: string }`
(`IncorrectInfo` is meant for locations — "the pin/name is wrong"; see the location/user variant below).
The same shape and rules apply to `POST /locations/{id}/reports` and `POST /users/{id}/reports`.
`details` ≤500 chars. Rate limited: 10 requests/60s per IP (tighter than search — filing a report
is a deliberate, infrequent action, this exists only to stop flooding). One report per
(reporter, target) pair — reporting the same photo/comment again → `409`. Target must exist (not
already deleted) → `404` otherwise.
→ `200 OK`:
```json
{
  "id": "guid", "reporterId": "guid", "reporterName": "string",
  "targetType": "Photo" | "Comment" | "Location" | "User", "targetId": "guid", "reason": "Spam", "details": "string|null",
  "status": "Pending", "createdAt": "date", "resolvedByUserId": "guid|null", "resolvedAt": "date|null"
}
```

### 🔒🛡️ `GET /moderation/reports?status=Pending&cursor=&limit=` (Moderator or Admin)
`status` defaults to `Pending` (also accepts `Resolved`/`Dismissed`). →
`CursorPagedResult<ReportResponse>`, same shape as above, newest-first.

### 🔒🛡️ `PATCH /moderation/reports/{id}` (Moderator or Admin)
Body: `{ "status": "Resolved" | "Dismissed" }` — `status` can't be `Pending` (that's the initial
state, not something you set going forward) → `400` otherwise. Does **not** delete the reported
content itself; resolving a report and deleting the photo/comment it's about are two separate
calls (`DELETE /photos/{id}` or `DELETE /comments/{id}`, which now also accept a Moderator/Admin
caller — see [Photos](#photos)). → updated `ReportResponse`.

### 🔒🛡️ `PATCH /moderation/users/{id}/role` (Admin only)
Body: `{ "role": "User" | "Moderator" | "Admin" }`. No safeguard against demoting/promoting
yourself or the last remaining Admin — the API trusts the caller. → updated `UserResponse`
(includes `email`, unlike the public `GET /users/{id}`).

### 🔒 `POST /locations/{id}/reports` · `POST /users/{id}/reports`
Same as `POST /photos/{id}/reports`: body `{ "reason": "Spam" | "Inappropriate" | "Harassment" | "Other" | "IncorrectInfo", "details"?: string (≤500) }`
→ `ReportResponse` (`targetType` is `Location` / `User`), `Reports` rate limit, one report per
reporter and target (`409` on a repeat). Specific to these two: `IncorrectInfo` is meant for locations
("the pin/name/city is wrong" — a moderator can then fix it with `PATCH /moderation/locations/{id}/coordinates`);
reporting **yourself** is `409`; an unknown or already-deleted/anonymized target is `404`. They show up in
`GET /moderation/reports` with the others — the moderator then acts on the target through the
endpoints below (ban, anonymize, delete location) and closes the report with `PATCH /moderation/reports/{id}`
(resolving a report does not act on the target by itself).

### 🔒🛡️ `POST /moderation/users/{id}/anonymize` (Admin only)
Removes a user's account on behalf of moderation → `204`. Body: `{ "reason": string (≤500, required), "hideContent"?: boolean }`.
**Irreversible** — ask for confirmation in the UI. Prefer [banning](#moderation) when the user may come back.

- Same erasure as the user deleting their own account: name → "Usuário excluído", e-mail replaced by a
  placeholder, password unusable, avatar/bio cleared; every session is revoked and (through a permanent
  ban) an access token already issued stops working immediately. The user can no longer sign in.
- `hideContent: true` also hides all the user's **photos and comments** (soft delete, counters kept
  consistent; replies to a hidden comment are hidden with it). Default `false`: their content stays visible
  under the "Usuário excluído" name.
- A notice email with the reason goes to the **original** address before it is erased; a failed send
  doesn't fail the request.
- Rules: Admin only (a Moderator gets `403`); the target can't be an Admin or yourself (`403`); a
  Moderator target is allowed; unknown user `404`; already removed `409`; empty/oversized `reason` `400`.
- Audited as `UserAnonymized` with the reason and, if hidden, how many photos/comments.

### 🔒🛡️ `POST /moderation/locations/{id}/merge` (Moderator or Admin)
Merges a **duplicate** into the location that stays. Body: `{ "targetId": guid }` — the location in the
URL is the duplicate (it is removed), `targetId` is the one that keeps living → the updated target
`LocationResponse`.

- All photos of the duplicate (hidden ones too, keeping their moderation state) are moved to the target.
- Its ratings move to the target, except those of users who **already rated the target** (one rating per user and location):
  the target's rating wins and the duplicate's stays behind on the removed location. The target's `avgRating` is recomputed.
- The duplicate is then soft-deleted (disappears from search/ranking/`GET`, listed under
  `GET /moderation/locations/deleted`). **Not reversible by restore**: restoring it gives back an empty location,
  the photos stay on the target.
- `409` if `targetId` is the location itself; `404` if either is unknown or already deleted; `400` for a
  missing `targetId`; `403` for a regular user. Audited as `LocationMerged` (with how many photos/ratings moved).
- Likes and comments travel with their photos. Reports about the duplicate are left as they are.

**UI suggestion:** in the moderation view of a location, a "Merge into…" action that picks the target
by searching locations (`GET /locations?q=`), asks for confirmation ("N photos will move to X; this can't be undone")
and calls the endpoint.

### 🔒🛡️ `PATCH /moderation/locations/{id}/coordinates` (Moderator or Admin)
Corrects a location's map pin. Body: `{ "latitude": number (-90..90), "longitude": number (-180..180) }`
→ the updated `LocationResponse`. `404` for an unknown/deleted location, `400` for out-of-range values,
`403` for a regular user. It is written to the audit log (`LocationCoordinatesChanged`, with the old and new
pair). Distances (`distanceKm`, "near me" search) and the sunset time use the new coordinates immediately.

**UI suggestion:** on the location's map, show an "Edit location" button only when
`user.role` is `Moderator` or `Admin`; clicking it makes the pin draggable (or lets the moderator
click a new spot / type the coordinates) with Save / Cancel, and Save calls the endpoint above. The
button is just a convenience: the server enforces the role, a regular user always gets `403`.

### 🔒🛡️ `DELETE /moderation/locations/{id}` · `POST /moderation/locations/{id}/restore` (Moderator or Admin)
Soft delete / undo → `204`.

- **Delete** hides the location everywhere (`GET /locations`, ranking, search, `GET /locations/{id}` → `404`)
  **and every photo of it** (they disappear from the feed and from their authors' profiles). Likes,
  comments and ratings are kept, so a restore is lossless. Unknown or already-deleted → `404`.
- **Restore** brings back the location and **only the photos hidden by that same delete**: a photo a
  moderator had removed on its own earlier stays removed. `409` if the location isn't deleted.
- Both are audited (`LocationDeleted` / `LocationRestored`, with the number of photos affected).

### 🔒🛡️ `GET /moderation/locations/deleted?limit=` (Moderator or Admin)
The restore list: most recently deleted first, up to `limit` (default 50, max 100; not cursor-paginated —
deleted locations are rare). → `[{ id, name, city, latitude, longitude, deletedAt, deletedByUserId }]`.

### 🔒🛡️ `POST /moderation/users/{id}/ban` · `DELETE /moderation/users/{id}/ban` (Moderator or Admin)
Bans / unbans a user → `204`. Body of the ban: `{ "reason": string (≤500, required), "until"?: ISO-8601 UTC
date }` — omit `until` for a **permanent** ban; a temporary one lifts itself at `until` (max 10 years
ahead), nothing needs to run.

- **Effect, immediately:** every refresh token of the user is revoked and the access token they
  already hold stops working on the very next request (`401`; the server checks the ban state with a
  ≤30 s cache that ban/unban invalidate). The client therefore lands on the login screen.
- **Login while banned** → `403` `{ title: "This account is banned.", errors: { reason: [..], bannedUntil?: [..] }, requestId }`
  (checked *after* the password, so a wrong password still gets the plain `401`). Same `403` for Google
  sign-in. Show `errors.reason` and, when present, `errors.bannedUntil`.
- The user gets a notice email with the reason; a failed send doesn't fail the ban. Their photos and
  comments stay visible.
- **Who can be banned:** a regular user by any Moderator/Admin; a **Moderator only by an Admin**; an
  **Admin never**; nobody can ban themselves → `403`. Unknown user → `404`. Missing/oversized `reason` or
  past/too-far `until` → `400`. Re-banning an already-banned user just replaces the ban.
- Both calls are written to the audit log (`UserBanned` / `UserUnbanned`, see `GET /moderation/actions`).
- `UserResponse` (as returned by `GET /moderation/users`) now has `isBanned` and `bannedUntil` so the
  admin UI can show the state and offer "Unban".

### 🔒🛡️ `GET /moderation/users?q=&cursor=&limit=` (Admin only, not Moderator)
Exists so an Admin can *find* the user they want to promote/demote via the endpoint above —
there's no other way to discover a user's id (no public search/listing endpoint). `q` optional,
matched against `Name` OR `Email` as a plain substring (`LIKE '%q%'`, no FULLTEXT index here,
unlike `/locations`); omit it to list users newest-first. → `CursorPagedResult<UserResponse>`
(includes `email`, and `isBanned` / `bannedUntil` so the screen can show the ban state and offer "Unban", same reasoning as the role-change response above — this is already gated to
Admin only). Admin-only rather than Moderator-too like the rest of `/moderation/*`, since its only
consumer is the Admin-only role-change flow.

### 🔒🛡️ `GET /moderation/actions?cursor=&limit=` (Moderator or Admin)
Read side of the append-only audit log that `ModerationAction` writes to on every action below
(resolving/dismissing a report, deleting someone else's photo/comment, publishing a legal
document, changing a role) — newest-first. →
```json
{
  "id": "guid", "moderatorId": "guid", "moderatorName": "string",
  "actionType": "PhotoDeleted" | "CommentDeleted" | "ReportResolved" | "ReportDismissed" |
    "LegalDocumentUpdated" | "UserRoleChanged" | "UserBanned" | "UserUnbanned" | "UserAnonymized" |
    "LocationDeleted" | "LocationRestored" | "LocationCoordinatesChanged" | "LocationMerged",
  "targetDescription": "string", "notes": "string|null", "createdAt": "date"
}
```
`targetDescription` is a free-text label the writing code chose for itself, **not a typed
reference** — it's `"Photo:{id}"` / `"Comment:{id}"` / `"User:{id}"` / `"Location:{id}"` / `"Report:{id}"` /
`"{documentType}:v{version}"` depending on `actionType`, so parse it by convention if you want to
link back to the thing (`Report:{id}` doesn't have a good link target since it's the *report* id,
not what the report was about — you'd need to have that report's own record, which this doesn't
carry). `notes` is a human-readable free-text summary, not something to parse:

| `actionType` | `notes` |
|---|---|
| `UserRoleChanged` | `New role: {role}` |
| `UserBanned` | `Permanent. Reason: …` or `Until {ISO date}. Reason: …` |
| `UserAnonymized` | `Reason: …; content kept` or `Reason: …; hidden N photo(s) and M comment(s)` |
| `LocationDeleted` / `LocationRestored` | `{name} ({city}); N photo(s) hidden` / `… restored` |
| `LocationCoordinatesChanged` | `(oldLat, oldLng) -> (newLat, newLng)` |
| `LocationMerged` | `{duplicate} merged into {target} [{target id}]; N photo(s) and M rating(s) moved` |
| the others (`UserUnbanned`, report/photo/comment/legal actions) | `null` |

### `GET /terms` / `GET /privacy`
Public, no auth. Both share the same shape and versioning mechanism (see below) — the only
difference is which document you get. →
`{ "id": "guid", "documentType": "TermsOfService" | "PrivacyPolicy", "content": "string", "version": 1, "createdAt": "date" }`.
`404` only if that particular document has never been published (shouldn't happen outside a
from-scratch dev DB that hasn't run `DbSeeder` or `PUT /terms`/`PUT /privacy` yet).

> **Current content (2026-10-29):** both documents are at **version 2** — full Terms of Use and LGPD
> Privacy Policy drafts, plain text with numbered headings, published while awaiting legal review (the
> text says so at the top and still has `[BRACKETED]` fields to fill in). The sources and the review
> checklist live in `docs/legal/`. Render `content` preserving line breaks (`white-space: pre-wrap`) or
> as light Markdown. Once the lawyers finish, publish the final text with the `PUT`s below.

### 🔒🛡️ `PUT /terms` / 🔒🛡️ `PUT /privacy` (Admin only)
Body: `{ "content": string }`, required, ≤20000 chars. **Every update creates a new version** —
there's no in-place edit, so old versions aren't retrievable through the API but aren't lost
either. Versions are numbered **independently per document** (each publish moves that
document's own `version` forward from 1 — updating `/terms` never touches `/privacy`'s version
counter, and vice versa). → the new, now-current `LegalDocumentResponse`.

⚠️ Before 2026-09-24, this was terms-of-service-only (`TermsOfServiceResponse`, versions numbered
globally). If you built against that shape: the response now always includes `documentType`, and
the type name changed.

---

## Notifications

All 🔒 (always "my own notifications" — there's no `{userId}` in any of these paths). Written for
a bell icon in the top-right corner: **polling-based**, no WebSocket/SignalR — poll
`GET /notifications/unread-count` on an interval (e.g. every 30-60s) and refresh the dropdown
list (`GET /notifications`) when the count changes or the bell is opened.

Four triggers write a `Notification`, always skipping the case where the actor would be
notifying themselves (liking/commenting on your own photo, replying to your own comment):
- Someone follows you → `NewFollower`.
- Someone likes your photo → `PhotoLiked`.
- Someone posts a **root** comment on your photo → `PhotoCommented`.
- Someone **replies** to your comment → `CommentReplied` (notifies the comment's author, not the
  photo's owner — a reply never generates two notifications, even if you own both the photo and
  the comment it's replying to).

### 🔒 `GET /notifications?cursor=&limit=`
Newest-first. → `CursorPagedResult<NotificationResponse>`:
```json
{
  "id": "guid",
  "type": "NewFollower" | "PhotoLiked" | "PhotoCommented" | "CommentReplied",
  "actorUserId": "guid", "actorName": "string", "actorAvatarUrl": "string|null",
  "targetDescription": "string",
  "readAt": "date|null", "createdAt": "date"
}
```
`targetDescription` is a free-text label, same convention as `ModerationAction` (see
[Moderation](#moderation)): `"User:{id}"` for `NewFollower`, `"Photo:{id}"` for `PhotoLiked`/
`PhotoCommented`, `"Comment:{id}"` for `CommentReplied` (the comment that got replied to, not the
new reply itself). `readAt` is `null` until marked read.

### 🔒 `GET /notifications/unread-count`
→ `{ "count": 3 }` — what the bell badge shows. Cheap `COUNT(*)`; safe to poll.

### 🔒 `POST /notifications/{id}/read`
Marks a single notification read → `204`. `401` if you're not its recipient (same "not the
owner" semantics as photo/comment delete, not a `403`). Idempotent — marking an
already-read notification read again is still `204`.

### 🔒 `POST /notifications/read-all`
Marks every one of the caller's unread notifications read → `204`. Use when the bell dropdown is
opened, if you want "seen" semantics instead of per-item read tracking.

---

## Known gotchas for the frontend

1. **`401` vs `403` depends on *which* check failed, and they mean different things.** The
   original "not the owner" checks (deleting someone else's photo/comment) predate roles and are
   still `401`, not `403` — the API doesn't distinguish "not logged in" from "logged in but not
   the owner" there. A `401` on a delete/update call while the user clearly has a valid session
   means "not the owner," not "session expired" — check the response `title` text if you need to
   tell those apart in the UI. The newer 🛡️-marked Moderator/Admin-only endpoints (`/moderation/*`,
   `PUT /terms`, `PUT /privacy`) are different: those correctly return `403` when authenticated but under-privileged,
   via ASP.NET Core's own authorization middleware (see the `403` caveat under
   [Error format](#error-format)).
2. Query enums (`sort`, `period`) are matched **by name, case-insensitively** — send
   `recent`/`top`, `week`/`month`/`all` as plain lowercase strings; don't send numeric enum
   values.
3. `likedByCurrentUser` is only accurate on `/photos` (feed) and `/photos/{id}` — it's hardcoded
   `false` on the two "photos by X" listings (`/users/{id}/photos`, `/locations/{id}/photos`).
   Same pattern for `isFollowedByCurrentUser`: accurate on `GET /users/{id}`, hardcoded `false` on
   `GET /users/{id}/followers` and `GET /users/{id}/following`.
4. Ratings are an **upsert** (`POST /locations/{id}/ratings` again just replaces score *and*
   comment — it's not a partial update) — use `GET /locations/{id}/ratings/me` to check whether
   the current user already rated a location before deciding "update your rating" vs. "rate this"
   UI.
5. There's no endpoint to list a user's likes or comments — only their photos
   (`GET /users/{id}/photos`) and, per-location, their ratings show up in
   `GET /locations/{id}/ratings` like anyone else's (filter client-side by `userId` if you need
   just theirs).
6. Replies are **one level deep, hard**: the API rejects (`409`) any attempt to reply to a
   comment that already has a `parentCommentId`. Design the UI so "Reply" only ever appears on
   root comments, not on replies themselves — there's no server-side flattening to fall back on.
7. Deleting a root comment silently takes its replies with it (see `DELETE /comments/{id}`
   above) — confirm with the user before deleting a root comment when `repliesCount > 0`.
8. **A `401` on a call that used to work can mean "banned", not "expired".** The refresh will also fail
   (the ban revoked every refresh token), so the usual "refresh, then retry, else go to login" flow
   lands on the login screen — which is where `POST /auth/login` returns the `403` with
   `errors.reason` / `errors.bannedUntil`. Handle that `403` there and show the message instead of
   "wrong password".
9. **`403` bodies differ** (empty for a missing role, `{ title, errors, requestId }` for the API's own
   rules) — branch on the status code, and read `title` only when a body is present.
10. **Soft-deleted things disappear everywhere, as `404`:** a deleted location is gone from search,
    ranking, `GET /locations/{id}`, and **its photos are gone from the feed and from their authors'
    profiles** until it is restored. A merged-away duplicate looks the same, but restoring it does not
    bring its photos back (they live on the target now). Don't cache a location id past a `404`.
11. **Destructive moderation actions need a confirmation step in the UI:** *anonymize* (irreversible)
    and *merge* (restoring doesn't undo it) in particular. The audit log can answer "who did that",
    but it cannot undo either.
12. **Moderators can fix the map pin** (`PATCH /moderation/locations/{id}/coordinates`) — show the
    "Edit location" control only when `user.role` is `Moderator` or `Admin`; the server enforces it
    anyway (`403`). The same endpoint's effect is immediate for `distanceKm` and the sunset time.
