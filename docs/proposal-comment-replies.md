# Proposta: comentários-resposta (replies) em fotos

Proposta de contrato pra levar ao time de backend (Sunset.API), no mesmo formato do `API.md`
existente. Objetivo: permitir responder a um comentário específico de uma foto, em vez de só a
lista plana atual.

## Estado atual (referência)

`CommentResponse`:
```json
{ "id": "guid", "userId": "guid", "userName": "string", "userAvatarUrl": "string|null", "content": "string", "createdAt": "date" }
```

- `GET /photos/{id}/comments?cursor=&limit=` → `CursorPagedResult<CommentResponse>`, lista plana.
- `POST /photos/{id}/comments` → body `{ "content": string }`, sem noção de destinatário.
- `DELETE /comments/{id}` → autor-only, `204`.

Não existe `parentCommentId` nem qualquer hierarquia hoje — é 100% flat.

## Decisões propostas (a confirmar com backend)

1. **Profundidade máxima: 1 nível.** Só dá pra responder a um comentário raiz, não a uma resposta
   (sem "resposta da resposta"). É o padrão mais comum (Instagram, YouTube) e evita árvore
   recursiva/paginação aninhada infinita. Se um `parentCommentId` apontar pra um comentário que já
   é ele mesmo uma resposta, rejeitar com `400`.
2. **Respostas em endpoint separado, não misturadas na listagem principal.** `GET
   /photos/{id}/comments` continua retornando só comentários raiz (`parentCommentId == null`),
   paginados como hoje. Um novo endpoint dedicado busca as respostas de um comentário sob demanda
   — casa com o padrão "Carregar mais" que o frontend já usa, e evita que a paginação da listagem
   principal fique poluída por respostas.
3. **Deletar um comentário-raiz com respostas: cascade delete.** Mais simples de implementar e
   evita FKórfã. Alternativas (bloquear a exclusão até apagar as respostas; soft-delete mostrando
   "comentário removido" mas preservando as respostas) são mais amigáveis mas exigem mais trabalho
   — sinalizando aqui caso o backend prefira uma dessas em vez do cascade simples.

## Contrato proposto

### `CommentResponse` — novos campos

```json
{
  "id": "guid",
  "userId": "guid",
  "userName": "string",
  "userAvatarUrl": "string|null",
  "content": "string",
  "createdAt": "date",
  "parentCommentId": "guid|null",
  "repliesCount": "number"
}
```

- `parentCommentId`: `null` num comentário raiz; preenchido numa resposta.
- `repliesCount`: quantidade de respostas diretas. Só relevante em comentários raiz (sempre `0`
  numa resposta, já que não há segundo nível). Existe pra o frontend mostrar "Ver 3 respostas" sem
  precisar buscar as respostas antes.

### `POST /photos/{id}/comments` — body

```json
{ "content": "string", "parentCommentId": "guid|null" }
```

- `content`: igual hoje (obrigatório, ≤1000 chars).
- `parentCommentId` (novo, opcional — omitir ou `null` = comentário raiz):
  - Deve referenciar um comentário existente **da mesma foto** (`photoId` bate) → senão `404`.
  - O comentário referenciado não pode já ter um `parentCommentId` (não pode responder a uma
    resposta) → senão `400`.
- Resposta: `200 OK` com `CommentResponse` (igual hoje).

### `GET /photos/{id}/comments?cursor=&limit=` — sem mudança de shape

Continua retornando só comentários raiz (implicitamente `parentCommentId == null`), na mesma
paginação por cursor de hoje. Cada item já vem com `repliesCount` pro frontend decidir se mostra
o link "Ver respostas".

### `GET /comments/{id}/replies?cursor=&limit=` — novo endpoint

Paginado igual aos outros feeds (`CursorPagedResult<CommentResponse>`), mas escopado às respostas
diretas de `{id}`. Ordenação sugerida: mais antiga primeiro (cronológica, como uma conversa),
diferente da listagem principal de comentários (que provavelmente é mais recente primeiro — a
confirmar qual é a ordem atual real).

- `{id}` precisa ser um comentário raiz (`parentCommentId == null`) → se for uma resposta, `404`
  ou `400` (a definir; `404` é consistente com "não existe uma lista de respostas pra isso").

### `DELETE /comments/{id}` — sem mudança de shape

Continua autor-only, `204`. Comportamento novo: se `{id}` for um comentário raiz com respostas,
apaga em cascata (ver decisão 3 acima). Se `{id}` for uma resposta, comportamento igual ao atual
(remove só ela).

## Compatibilidade

Mudança aditiva — comentários existentes ficam com `parentCommentId: null` e `repliesCount: 0`
automaticamente, sem precisar de backfill. Clientes antigos que ignorarem os campos novos
continuam funcionando normalmente.

## Impacto no frontend (depois que a API suportar)

- `types/comment.ts`: adicionar `parentCommentId: string | null` e `repliesCount: number`.
- `lib/api/photos.ts`: `createComment` aceita `parentCommentId` opcional; novo `getCommentReplies`.
- `CommentList`/`Comments`: botão "Responder" por comentário raiz (abre um `CommentForm` inline
  com o `parentCommentId` preenchido); "Ver N respostas" carrega via `getCommentReplies` sob
  demanda, reaproveitando o padrão de paginação do `useComments`.
