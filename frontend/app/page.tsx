import { checkApiHealth, type ApiHealth } from "../lib/api-health";

// The API status is checked on every request, never baked in at build time.
export const dynamic = "force-dynamic";

export default async function Page() {
  const health = await checkApiHealth();

  return (
    <main className="mx-auto flex min-h-screen max-w-2xl flex-col justify-center gap-4 px-6">
      <h1 className="text-3xl font-semibold tracking-tight">AI agent security testing platform</h1>
      <p className="text-muted-foreground">
        Tests AI agents and chatbots, proves each weakness with evidence and proves the fix with a retest.
      </p>
      <ApiStatus health={health} />
    </main>
  );
}

// Status is shown as text plus an icon, never by colour alone. currentColor keeps it readable in both themes.
function ApiStatus({ health }: { health: ApiHealth }) {
  const reachable = health === "reachable";
  return (
    <p role="status" className="flex items-center gap-2 text-sm">
      <svg aria-hidden="true" viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2">
        {reachable ? (
          <path d="M3 8.5l3 3 7-7" strokeLinecap="round" strokeLinejoin="round" />
        ) : (
          <path d="M4 4l8 8M12 4l-8 8" strokeLinecap="round" />
        )}
      </svg>
      <span>{reachable ? "API reachable" : "API unreachable"}</span>
    </p>
  );
}
