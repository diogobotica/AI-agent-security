import { expect, test, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { resolve } from 'node:path';

// Local stack only: Docker Compose on this machine, every port bound to 127.0.0.1. No external host is contacted.
const API = 'http://127.0.0.1:5080';
const WORKER = 'http://127.0.0.1:8001';
const MOCK_AGENT = 'http://127.0.0.1:8002';
const HOME = 'http://127.0.0.1:3000/';
const REPO = resolve(import.meta.dirname, '..', '..');

const compose = (...args: string[]) => execFileSync('docker', ['compose', ...args], { cwd: REPO, encoding: 'utf8' });

test('local stack services all report healthy', async ({ request }) => {
  const rows = compose('ps', '--all', '--format', '{{.Service}}|{{.State}}|{{.Health}}|{{.ExitCode}}')
    .trim()
    .split('\n')
    .map((l) => l.split('|'));
  const services = rows.map((r) => r[0]).sort();
  for (const s of ['postgres', 'azurite', 'mssql', 'servicebus', 'ollama', 'api', 'worker', 'mock-agent']) {
    expect(services, `service ${s} defined`).toContain(s);
  }
  for (const [service, state, health, exit] of rows) {
    if (service === 'ollama-init') {
      expect(state, 'ollama-init finished').toBe('exited');
      expect(exit, 'ollama-init exit code').toBe('0');
    } else {
      expect(state, `${service} running`).toBe('running');
      expect(['healthy', ''], `${service} health`).toContain(health);
    }
  }
  for (const base of [API, WORKER, MOCK_AGENT]) {
    const res = await request.get(`${base}/health`);
    expect(res.status(), base).toBe(200);
    expect(await res.json(), base).toEqual({ status: 'ok' });
  }
});

test('published ports are bound to 127.0.0.1 only', () => {
  const ports = compose('ps', '--format', '{{.Ports}}');
  const bound = ports.match(/\d+\.\d+\.\d+\.\d+:\d+->/g) ?? [];
  expect(bound.length).toBeGreaterThan(0);
  for (const b of bound) expect(b.startsWith('127.0.0.1:'), b).toBe(true);
  expect(ports).not.toContain('0.0.0.0');
  expect(ports).not.toContain('[::]');
});

test('api and worker readiness report every dependency ok', async ({ request }) => {
  for (const base of [API, WORKER]) {
    const res = await request.get(`${base}/health/ready`);
    expect(res.status(), base).toBe(200);
    expect(await res.json(), base).toEqual({
      status: 'ready',
      checks: { postgres: 'ok', blob: 'ok', servicebus: 'ok', ollama: 'ok' },
    });
  }
});

test('readiness names a stopped dependency without revealing hosts', async ({ request }) => {
  test.setTimeout(180_000);
  compose('stop', 'servicebus');
  try {
    for (const base of [API, WORKER]) {
      const res = await request.get(`${base}/health/ready`);
      expect(res.status(), base).toBe(503);
      const text = await res.text();
      const body = JSON.parse(text);
      expect(body.status).toBe('not-ready');
      expect(body.checks.servicebus).toBe('unreachable');
      expect(body.checks.postgres).toBe('ok');
      // No host names, ports, addresses or error text.
      expect(text).not.toMatch(/servicebus:|sbemulator|127\.0\.0\.1|localhost|:\d{2,5}|exception|refused|timed out/i);
    }
  } finally {
    compose('start', 'servicebus');
  }
  // Recovers once the emulator is back (it needs a short start-up time).
  await expect
    .poll(async () => (await request.get(`${API}/health/ready`)).status(), { timeout: 120_000, intervals: [2000] })
    .toBe(200);
  await expect
    .poll(async () => (await request.get(`${WORKER}/health/ready`)).status(), { timeout: 60_000, intervals: [2000] })
    .toBe(200);
});

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
      status: toRgb(getComputedStyle(document.querySelector('[role=status]')!).color),
      h1: toRgb(getComputedStyle(document.querySelector('h1')!).color),
      bg: toRgb(getComputedStyle(document.body).backgroundColor),
    };
  });
}

for (const scheme of ['light', 'dark'] as const) {
  test(`home page reports the local API reachable in ${scheme} mode`, async ({ browser }) => {
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
    const status = page.getByRole('status');
    await expect(status).toHaveText('API reachable'); // text, not colour alone
    await expect(status.locator('svg')).toBeVisible(); // icon next to the text

    const c = await colours(page);
    expect(contrast(c.status, c.bg), `status contrast in ${scheme}`).toBeGreaterThanOrEqual(4.5);
    expect(contrast(c.h1, c.bg), `heading contrast in ${scheme}`).toBeGreaterThanOrEqual(4.5);

    expect((await page.locator('body').innerText()).toLowerCase()).not.toContain('secure');
    await page.screenshot({ path: `test-results/us-102-home-${scheme}.png` });
    expect(errors, 'console errors').toEqual([]);
    expect(external, 'requests leaving 127.0.0.1').toEqual([]);
    await context.close();
  });
}
