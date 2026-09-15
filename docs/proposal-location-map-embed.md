# Proposta: mapa (Google Maps Embed) na página de local

Especificação pra levar ao frontend (React) — sem mudanças no backend, ver seção final.

## Objetivo

Ao abrir a página de um `Location`, mostrar um mapa embutido centrado nas coordenadas exatas do
local.

## Dados já disponíveis (nenhuma mudança de API necessária)

`GET /locations/{id}` já retorna:
```json
{ "id": "guid", "name": "string", "latitude": -28.1305, "longitude": -48.6224, "city": "string", "avgRating": 4.33, "createdAt": "date" }
```
`latitude`/`longitude` são `double`, já validados no backend (-90..90 / -180..180 na criação do
local) — o componente de mapa pode assumir que sempre são números válidos, sem tratar caso de
coordenada ausente/inválida.

## URL do embed

```
https://www.google.com/maps/embed/v1/place?key={API_KEY}&q={latitude},{longitude}
```

**Usar sempre o par de coordenadas em `q`, nunca o nome do local.** Passar `q=Praia do Rosa` deixa
o Google geocodificar o texto, que pode resolver pro lugar errado (nome ambíguo, sem o local
cadastrado no Google Maps, etc.) — já temos a coordenada exata, não faz sentido reintroduzir essa
imprecisão.

Parâmetros opcionais úteis: `&zoom=14` (padrão razoável pra um ponto único), `&maptype=roadmap` ou
`satellite`.

## Componente sugerido

```tsx
// components/LocationMap.tsx
type LocationMapProps = {
  latitude: number;
  longitude: number;
  name: string; // só para o `title` do iframe (acessibilidade), não para a query do mapa
};

function LocationMap({ latitude, longitude, name }: LocationMapProps) {
  const src = `https://www.google.com/maps/embed/v1/place?key=${import.meta.env.VITE_GOOGLE_MAPS_EMBED_KEY}&q=${latitude},${longitude}&zoom=14`;

  return (
    <div className="aspect-video w-full overflow-hidden rounded-lg">
      <iframe
        src={src}
        title={`Mapa de ${name}`}
        width="100%"
        height="100%"
        style={{ border: 0 }}
        loading="lazy"
        referrerPolicy="no-referrer-when-downgrade"
        allowFullScreen
      />
    </div>
  );
}
```

Pontos de atenção:
- **`loading="lazy"`**: evita carregar o iframe (e gastar uma "carga" da key) se o mapa estiver
  fora da viewport inicial — relevante se a página tiver bastante conteúdo acima do mapa.
- **`title`**: obrigatório pra acessibilidade (leitor de tela); usar o nome do local, não um texto
  genérico tipo "mapa".
- **Container com `aspect-ratio` fixo**: iframe não tem tamanho intrínseco — sem isso o layout
  colapsa (altura 0) até o CSS carregar.

## Onde a key mora

- Variável de ambiente do projeto React (ex.: `VITE_GOOGLE_MAPS_EMBED_KEY` se for Vite,
  `NEXT_PUBLIC_GOOGLE_MAPS_EMBED_KEY` se for Next.js — o prefixo `VITE_`/`NEXT_PUBLIC_` é
  obrigatório pra a variável ficar disponível no bundle do client, já que isso roda 100% no
  browser).
- **Nunca commitar a key** — `.env` no `.gitignore`, só um `.env.example` com o nome da variável
  vazio no repositório.
- No Google Cloud Console, restringir a key por **HTTP referrer** pros domínios do frontend
  (produção + `http://localhost:PORTA/*` pra dev). Sem essa restrição, qualquer site pode "pegar
  carona" na sua key só lendo o HTML de uma página pública.

## Fora de escopo aqui

- Nenhuma mudança em `Sunset.API` — `latitude`/`longitude` já existem em `LocationResponse` desde
  a implementação inicial de Locations.
- A key não deve, em nenhuma hipótese, ser passada por uma requisição ao backend nem armazenada
  no banco — é uma credencial de uso exclusivamente client-side.
