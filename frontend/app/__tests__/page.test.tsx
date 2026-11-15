import type { ReactNode } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Page from "../page";

// US-102: the home page asks the local API whether it answers (GET {API_BASE_URL}/health) and says so in text.
// fetch is always mocked: no network. API_BASE_URL is read when the page renders, not when the module loads.
// The page awaits the check itself (an async server component), so the test awaits Page() before rendering.
const API_BASE_URL = "http://127.0.0.1:5080";

const fetchMock = vi.fn<typeof fetch>();

function healthAnswers(status: number, body: unknown): void {
  fetchMock.mockResolvedValue(
    new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } }),
  );
}

async function renderHomePage(): Promise<string> {
  const element: ReactNode = await Page();
  return renderToStaticMarkup(element);
}

function visibleText(html: string): string {
  return html
    .replace(/<[^>]*>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

beforeEach(() => {
  fetchMock.mockReset();
  healthAnswers(200, { status: "ok" });
  vi.stubGlobal("fetch", fetchMock);
  vi.stubEnv("API_BASE_URL", API_BASE_URL);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});

describe("home page", () => {
  it("renders a level-one heading naming the platform", async () => {
    const html = await renderHomePage();
    const headings = Array.from(html.matchAll(/<h1\b[^>]*>([\s\S]*?)<\/h1>/g));

    expect(headings).toHaveLength(1);
    const headingText = visibleText(headings[0]?.[1] ?? "");
    expect(headingText).toMatch(/\bplatform\b/i);
  });

  it("never uses the word secure", async () => {
    const reachable = await renderHomePage();
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));
    const unreachable = await renderHomePage();

    expect(reachable).not.toMatch(/\bsecure(d|ly)?\b/i);
    expect(unreachable).not.toMatch(/\bsecure(d|ly)?\b/i);
  });

  it("shows API reachable when health answers ok", async () => {
    healthAnswers(200, { status: "ok" });

    const text = visibleText(await renderHomePage());

    expect(fetchMock).toHaveBeenCalled();
    const requested = new URL(String(fetchMock.mock.calls[0]?.[0]));
    expect(requested.origin).toBe(API_BASE_URL);
    expect(requested.pathname).toBe("/health");
    expect(text).toMatch(/\bAPI reachable\b/);
    expect(text).not.toMatch(/\bAPI unreachable\b/);
  });

  it("shows API unreachable when the request fails", async () => {
    fetchMock.mockRejectedValue(new TypeError("fetch failed"));

    const text = visibleText(await renderHomePage());

    expect(text).toMatch(/\bAPI unreachable\b/);
    expect(text).not.toMatch(/\bAPI reachable\b/);
    expect(text).not.toMatch(/fetch failed/i);
  });

  it("shows API unreachable when health answers an error status", async () => {
    healthAnswers(503, { status: "unavailable" });

    const text = visibleText(await renderHomePage());

    expect(text).toMatch(/\bAPI unreachable\b/);
    expect(text).not.toMatch(/\bAPI reachable\b/);
  });
});
