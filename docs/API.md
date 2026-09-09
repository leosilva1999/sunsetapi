# Sunset API — Reference for frontend integration

API .NET 9 (Clean Architecture) para o Sunset: usuários pesquisam locais com belas visões de
pôr do sol, postam fotos marcando o local, curtem, comentam e avaliam os locais.

This document describes the API **as actually implemented**, for a client (React web app) to
integrate against. Field names below are copy-pasted from real request/response payloads, not
paraphrased.

## Recent changes

New since this doc was first handed over — if you already built against the earlier contract,
these are the diffs to check:

- **`UserResponse.bio`** (`string | null`, ≤160 chars) — see [Users](#users).
- **`PATCH /users/me` is now a true partial update** (JSON Merge Patch semantics: omitted field =
  unchanged, `null` = clear, value = replace) — it used to require re-sending all three fields.
  ⚠️ its Swagger schema is wrong, see the note under [Users](#users).
- **Comment replies, one level deep**: `CommentResponse.parentCommentId` / `repliesCount`,
  `POST /photos/{id}/comments` takes an optional `parentCommentId`, new
  `GET /comments/{id}/replies` — see [Photos](#photos).
- **`PhotoResponse.commentsCount`** (roots + replies combined) — see [Photos](#photos).
- **CORS** is now configured (was previously missing/blocking) — see [CORS](#cors).

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
seeded). 8 users, 6 real Brazilian sunset spots, 12 photos, likes/comments/ratings. All seed
users share the password **`Password123!`**. Emails: `beatriz@sunsetapp.dev`,
`rafael@sunsetapp.dev`, `camila@sunsetapp.dev`, `lucas@sunsetapp.dev`, `juliana@sunsetapp.dev`,
`pedro@sunsetapp.dev`, `mariana@sunsetapp.dev`, `thiago@sunsetapp.dev`. Use any of these to log
in during frontend development instead of registering a throwaway account each time.

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
{ "title": "human-readable message", "errors": { "FieldName": ["message"] } | null }
```

`errors` is populated (grouped by field name) only for `400` validation failures; every other
error status has `errors: null` and a single message in `title`.

| Status | Meaning |
|---|---|
| 400 | Request failed FluentValidation rules (see `errors` for per-field messages) |
| 401 | Missing/invalid/expired bearer token, or invalid login/refresh credentials |
| 404 | Referenced resource (user/location/photo/comment) doesn't exist |
| 409 | Conflict — currently only "email already registered" |
| 502 | The sunrise-sunset.org upstream call failed (see Locations → sunset below) |
| 500 | Unhandled server error |

Model-binding failures (e.g. malformed GUID in the URL, malformed `?date=`) short-circuit to a
`400` with ASP.NET Core's default `ProblemDetails` shape instead — don't rely on the `title`
field being present in that specific case.

### Auth requirement legend

🔒 = requires `Authorization: Bearer <token>`. Endpoints without 🔒 are public (some
optionally read the token if present, noted inline).

---

## Auth

### `POST /auth/register`
Body: `{ "name": string, "email": string, "password": string }`
Validation: `name` required ≤100 chars · `email` required, valid format, ≤256 chars ·
`password` required, ≥8 chars.

### `POST /auth/login`
Body: `{ "email": string, "password": string }`

Both register and login return:
```json
{
  "accessToken": "eyJ...",
  "refreshToken": "NeX9jEIO...",
  "expiresAt": "2026-09-04T18:23:24.59Z",
  "user": { "id": "guid", "name": "string", "email": "string", "avatarUrl": "string|null", "bio": "string|null", "createdAt": "date" }
}
```

### `POST /auth/refresh`
Body: `{ "refreshToken": string }` → same `AuthResponse` shape as above, with a new pair.

### `POST /auth/logout`
Body: `{ "refreshToken": string }` → `204 No Content`. Idempotent (calling it twice, or with an
already-revoked token, still returns `204`).

---

## Users

### `GET /users/{id}`
→ `{ "id", "name", "email", "avatarUrl", "bio", "createdAt" }`. `bio` is `null` until the user
sets one.

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
`avatarUrl`, if present and non-null, must be a valid absolute URL, ≤2048 chars · `bio`, if
present and non-null, ≤160 chars.
→ updated `UserResponse`.

### `GET /users/{id}/photos?cursor=&limit=`
Paginated photos authored by that user. → `CursorPagedResult<PhotoResponse>` (see Photos for
shape). **Note:** `likedByCurrentUser` is always `false` here regardless of the caller's token —
not wired up for this listing (only `/photos` feed and `/photos/{id}` resolve it correctly).

---

## Locations

### `GET /locations?q=&lat=&lng=&radius=&cursor=&limit=`
Search/browse. All query params optional.
- `q`: substring match against name or city.
- `lat`+`lng`+`radius` (km): must be supplied **together** to filter by distance — it's a coarse
  bounding-box approximation, not exact great-circle distance, so don't expect razor-precise
  radius edges.
→ `CursorPagedResult<LocationResponse>`.

### `GET /locations/ranking?period=all&limit=20`
`period` is one of `week` | `month` | `all` (case-insensitive; default `all`), ranking by average
rating **within that window** (not the location's all-time `avgRating` field, though that field
is still what's returned per location). → plain array of `LocationResponse` (not paginated).

### `GET /locations/{id}`
→ `{ "id", "name", "latitude", "longitude", "city", "avgRating", "createdAt" }`. `avgRating` is a
decimal (e.g. `4.33`), 0 for a location with no ratings yet.

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

### 🔒 `POST /locations/{id}/ratings`
Body: `{ "score": 1-5 }`. **Upsert semantics**: calling it again for the same user+location
updates the existing rating rather than erroring or creating a duplicate. → updated
`LocationResponse` (with recalculated `avgRating`) — note this returns the **location**, not the
rating itself.

---

## Photos

### `GET /photos?sort=recent&cursor=&limit=`
`sort` is `recent` (default) | `top` (by `likesCount`, then recency).
```json
{
  "items": [{
    "id": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null",
    "locationId": "guid", "locationName": "string",
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

### 🔒 `POST /photos`
Body: `{ "locationId": "guid", "imageUrl": string, "caption": string | null }`.
**The client uploads the image to storage itself first (e.g. pre-signed S3/R2 URL) and only
sends the resulting URL here — this endpoint never accepts binary/multipart data.**
Validation: `locationId` required · `imageUrl` required, valid absolute URL, ≤2048 · `caption` ≤500.
404 if `locationId` doesn't exist. → `201 Created` with `PhotoResponse`.

### `GET /photos/{id}`
→ single `PhotoResponse`, same shape as feed items, with `likedByCurrentUser` resolved.

### 🔒 `DELETE /photos/{id}`
Author-only (`403`... actually `401 UnauthorizedActionException` — see note below) if the caller
didn't author it. → `204`.

### 🔒 `POST /photos/{id}/likes` / 🔒 `DELETE /photos/{id}/likes`
**Idempotent.** Liking an already-liked photo, or unliking one you haven't liked, is a silent
no-op → `204` either way, `likesCount` unchanged. No "already liked" error to handle.

### `GET /photos/{id}/comments?cursor=&limit=`
**Root comments only** (`parentCommentId == null`) — replies are fetched separately, see below.
Ordered newest-first. → `CursorPagedResult<CommentResponse>`:
```json
{
  "id": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null",
  "content": "string", "createdAt": "date",
  "parentCommentId": "guid|null", "repliesCount": 5
}
```
`parentCommentId` is always `null` in this listing (roots only). `repliesCount` — direct replies
to that comment; use it to decide whether to show a "View N replies" affordance before fetching
them.

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
top-level `/api/v1/comments/{id}`. Author-only. → `204`.
- Deleting a **reply** decrements its parent's `repliesCount`.
- Deleting a **root comment that has replies cascades** — all its replies are deleted too, at the
  database level (FK `ON DELETE CASCADE`), regardless of who authored them. There's no
  confirmation step or "orphan" state; the frontend should warn the user before deleting a root
  comment that has `repliesCount > 0`.

---

## Known gotchas for the frontend

1. **Authorization error status is `401`, not `403`**, for every "you're not allowed to do this"
   case (deleting someone else's photo/comment) — the API doesn't distinguish "not logged in"
   from "logged in but not the owner." A `401` on a delete/update call while the user clearly has
   a valid session means "not the owner," not "session expired" — check the response `title`
   text if you need to tell those apart in the UI.
2. Query enums (`sort`, `period`) are matched **by name, case-insensitively** — send
   `recent`/`top`, `week`/`month`/`all` as plain lowercase strings; don't send numeric enum
   values.
3. `likedByCurrentUser` is only accurate on `/photos` (feed) and `/photos/{id}` — it's hardcoded
   `false` on the two "photos by X" listings (`/users/{id}/photos`, `/locations/{id}/photos`).
4. Ratings are an **upsert** (`POST /locations/{id}/ratings` again just updates the score) — no
   separate "edit my rating" endpoint, and no "get my rating for this location" endpoint either;
   the frontend has to track locally whether the current user already rated a location if it
   wants to show "update your rating" vs. "rate this" UI.
5. There's no endpoint to list a user's ratings, likes, or comments — only their photos
   (`GET /users/{id}/photos`).
6. Replies are **one level deep, hard**: the API rejects (`409`) any attempt to reply to a
   comment that already has a `parentCommentId`. Design the UI so "Reply" only ever appears on
   root comments, not on replies themselves — there's no server-side flattening to fall back on.
7. Deleting a root comment silently takes its replies with it (see `DELETE /comments/{id}`
   above) — confirm with the user before deleting a root comment when `repliesCount > 0`.
