import { defineConfig } from '@playwright/test';

// US-102: the API, worker and mock agent run in Docker Compose (python scripts/local_stack.py up).
// Only the frontend is started here, pointed at the containerised API on 127.0.0.1:5080.
export default defineConfig({
  testDir: './tests',
  testMatch: /us-102-.*\.spec\.ts/,
  timeout: 60_000,
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: {
    command: 'npm run start -- --hostname 127.0.0.1 --port 3000',
    cwd: '../frontend',
    url: 'http://127.0.0.1:3000',
    reuseExistingServer: true,
    timeout: 120_000,
    env: { API_BASE_URL: 'http://127.0.0.1:5080' },
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
