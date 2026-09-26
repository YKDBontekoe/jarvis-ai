import { spawn } from 'node:child_process';
import readline from 'node:readline';

const child = spawn('codex', ['app-server', '-c', 'mcp_servers={}'], {
  stdio: ['pipe', 'pipe', 'ignore'],
});
const input = readline.createInterface({ input: child.stdout });
const send = value => child.stdin.write(JSON.stringify(value) + '\n');
const timeout = setTimeout(() => { child.kill(); process.exitCode = 1; }, 15000);
input.on('line', line => {
  const response = JSON.parse(line);
  if (response.id === 1) {
    send({ method: 'initialized', params: {} });
    send({ method: 'model/list', id: 2, params: { limit: 100, includeHidden: false } });
  } else if (response.id === 2) {
    console.log(JSON.stringify(response.result.data.map(({ model, inputModalities, isDefault }) =>
      ({ model, inputModalities, isDefault }))));
    clearTimeout(timeout);
    child.kill();
  }
});
send({ method: 'initialize', id: 1, params: { clientInfo: { name: 'jarvis-verification', version: '1.0.0' } } });
