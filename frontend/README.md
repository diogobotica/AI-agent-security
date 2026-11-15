# frontend

The founder's web app: Next.js App Router, TypeScript strict, Tailwind CSS, with shadcn/ui configured. It holds no business logic; the API owns it. Right now it is one placeholder page. Components, the typed API client, the theme toggle and the screens arrive in later stories (US-813 onwards).

## Layout

- `app/`: App Router. `layout.tsx` is the root layout, `page.tsx` the home page (an async server component that shows whether the API answers), and `globals.css` has Tailwind plus light and dark colour variables that follow the system theme.
- `lib/utils.ts`: the `cn` class-name helper used by shadcn/ui.
- `lib/api-health.ts`: server-side `GET {API_BASE_URL}/health` with a 2 s timeout; the page shows "API reachable" or "API unreachable" (text plus icon). Without `API_BASE_URL`, development uses http://127.0.0.1:5080 (the local stack); production builds must set it.
- `components.json`: shadcn/ui settings. No components are added yet.
- `package.json`: every version is pinned exactly (`.npmrc` sets `save-exact=true`). Install from the lockfile with `npm ci`.
- `app/__tests__/`: vitest tests that render pages to static markup, with no browser and no network.

## Run locally

Node.js 22.13 or newer (ESLint 10 needs it).

```sh
cd frontend
npm ci
npm run dev            # http://localhost:3000
npm run build && npm run start
```

## Tests and linters

```sh
cd frontend
npm test
npm run lint
npm run build          # also type-checks
```
