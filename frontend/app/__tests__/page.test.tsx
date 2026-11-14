import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import Page from "../page";

function renderHomePage(): string {
  return renderToStaticMarkup(<Page />);
}

function visibleText(html: string): string {
  return html
    .replace(/<[^>]*>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

describe("home page", () => {
  it("renders a level-one heading naming the platform", () => {
    const html = renderHomePage();
    const headings = Array.from(html.matchAll(/<h1\b[^>]*>([\s\S]*?)<\/h1>/g));

    expect(headings).toHaveLength(1);
    const headingText = visibleText(headings[0]?.[1] ?? "");
    expect(headingText).toMatch(/\bplatform\b/i);
  });

  it("never uses the word secure", () => {
    const html = renderHomePage();

    expect(html).not.toMatch(/\bsecure(d|ly)?\b/i);
  });
});
