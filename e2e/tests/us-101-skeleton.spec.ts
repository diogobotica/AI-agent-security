import { expect, test, type Page } from '@playwright/test';

const HOME = 'http://127.0.0.1:3000/';

test('api, worker and mock agent health endpoints answer ok', async ({ request }) => {
  for (const base of ['http://127.0.0.1:5080', 'http://127.0.0.1:8001', 'http://127.0.0.1:8002']) {
    const res = await request.get(`${base}/health`);
    expect(res.status(), base).toBe(200);
    expect(await res.json(), base).toEqual({ status: 'ok' });
  }
  const api = await request.get('http://127.0.0.1:5080/health');
  expect(api.headers()['server']).toBeUndefined();
});

test('worker and mock agent expose no docs routes', async ({ request }) => {
  for (const base of ['http://127.0.0.1:8001', 'http://127.0.0.1:8002']) {
    for (const path of ['/docs', '/redoc', '/openapi.json']) {
      expect((await request.get(base + path)).status(), base + path).toBe(404);
    }
  }
});

// Normalises any CSS colour to RGB through a canvas, so any colour syntax works.
async function colours(page: Page) {
  return page.evaluate(() => {
    const toRgb = (css: string): number[] => {
      const c = document.createElement('canvas');
      c.width = c.height = 1;
      const ctx = c.getContext('2d')!;
      ctx.fillStyle = css;
      ctx.fillRect(0, 0, 1, 1);
      const d = ctx.getImageData(0, 0, 1, 1).data;
      return [d[0], d[1], d[2]];
    };
    return {
      h1: toRgb(getComputedStyle(document.querySelector('h1')!).color),
      p: toRgb(getComputedStyle(document.querySelector('main p')!).color),
      bg: toRgb(getComputedStyle(document.body).backgroundColor),
    };
  });
}

const lum = ([r, g, b]: number[]) => {
  const f = (v: number) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
};
const contrast = (a: number[], b: number[]) => {
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
};

const backgrounds: Record<string, number[]> = {};

for (const scheme of ['light', 'dark'] as const) {
  test(`home page renders readable text in ${scheme} mode`, async ({ browser }) => {
    const context = await browser.newContext({ colorScheme: scheme });
    const page = await context.newPage();
    const errors: string[] = [];
    const external: string[] = [];
    page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
    page.on('pageerror', (e) => errors.push(e.message));
    page.on('request', (r) => {
      const u = new URL(r.url());
      if (u.protocol.startsWith('http') && !['127.0.0.1', 'localhost'].includes(u.hostname)) external.push(r.url());
    });

    await page.goto(HOME);
    const h1 = page.getByRole('heading', { level: 1 });
    await expect(h1).toBeVisible();
    await expect(h1).toContainText(/platform/i);

    const c = await colours(page);
    expect(contrast(c.h1, c.bg), `heading contrast in ${scheme}`).toBeGreaterThanOrEqual(4.5);
    expect(contrast(c.p, c.bg), `paragraph contrast in ${scheme}`).toBeGreaterThanOrEqual(4.5);
    backgrounds[scheme] = c.bg;

    await page.screenshot({ path: `test-results/home-${scheme}.png` });
    expect(errors, 'console errors').toEqual([]);
    expect(external, 'requests leaving 127.0.0.1').toEqual([]);
    await context.close();
  });
}

test('light and dark themes differ', async () => {
  // Relies on the two theme tests above (single worker, declaration order).
  expect(backgrounds.light).toBeDefined();
  expect(backgrounds.dark).toBeDefined();
  expect(backgrounds.light).not.toEqual(backgrounds.dark);
});

test('home page never shows the word secure', async ({ page }) => {
  await page.goto(HOME);
  const text = await page.locator('body').innerText();
  expect(text.toLowerCase()).not.toContain('secure');
});
