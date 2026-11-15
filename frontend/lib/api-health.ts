/**
 * Asks the local API whether it answers (GET {API_BASE_URL}/health). Server-side only, no business logic.
 * The API answers {"status":"ok"} when it is up; anything else (an error status, another body, a failed or slow
 * request) counts as unreachable. The error itself is never shown to the user.
 */

export type ApiHealth = "reachable" | "unreachable";

const HEALTH_TIMEOUT_MS = 2000;

// The local API from the Docker Compose stack (US-102). Used only outside production builds when API_BASE_URL is not
// set; a production deployment must set API_BASE_URL. Not a secret: it is a loopback address.
const DEVELOPMENT_API_BASE_URL = "http://127.0.0.1:5080";

function apiBaseUrl(): string | undefined {
  // Read at call time, so the environment applies on every render.
  const configured = process.env.API_BASE_URL;
  if (configured) {
    return configured;
  }
  return process.env.NODE_ENV === "production" ? undefined : DEVELOPMENT_API_BASE_URL;
}

export async function checkApiHealth(): Promise<ApiHealth> {
  const baseUrl = apiBaseUrl();
  if (!baseUrl) {
    return "unreachable";
  }

  try {
    const response = await fetch(new URL("/health", baseUrl), {
      cache: "no-store",
      redirect: "error",
      signal: AbortSignal.timeout(HEALTH_TIMEOUT_MS),
    });
    if (!response.ok) {
      return "unreachable";
    }
    const body: unknown = await response.json();
    const isOk = typeof body === "object" && body !== null && (body as { status?: unknown }).status === "ok";
    return isOk ? "reachable" : "unreachable";
  } catch {
    return "unreachable";
  }
}
