import { defineConfig } from '@playwright/test';

const win = process.platform === 'win32';
// Paths are relative to each server's cwd (its own component folder).
const py = () => (win ? '.venv\\Scripts\\python.exe' : '.venv/bin/python');

// Local stack for US-101: API :5080, worker :8001, mock agent :8002, frontend :3000 (all 127.0.0.1).
export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  retries: 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: [
    {
      command: 'dotnet run --project src/Platform.Api --no-launch-profile --urls http://127.0.0.1:5080',
      cwd: '../api',
      url: 'http://127.0.0.1:5080/health',
      reuseExistingServer: true,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: `${py()} -m uvicorn worker.main:app --app-dir src --host 127.0.0.1 --port 8001`,
      cwd: '../worker',
      url: 'http://127.0.0.1:8001/health',
      reuseExistingServer: true,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: `${py()} -m uvicorn mock_agent.main:app --app-dir src --host 127.0.0.1 --port 8002`,
      cwd: '../mock-agent',
      url: 'http://127.0.0.1:8002/health',
      reuseExistingServer: true,
      stdout: 'pipe',
      stderr: 'pipe',
    },
    {
      command: 'npm run start -- --hostname 127.0.0.1 --port 3000',
      cwd: '../frontend',
      url: 'http://127.0.0.1:3000',
      reuseExistingServer: true,
      timeout: 120_000,
      stdout: 'pipe',
      stderr: 'pipe',
    },
  ],
});
