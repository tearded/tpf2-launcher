import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../desktop.js', import.meta.url), 'utf8')
  .replace(/^import .*;\r?\n/gm, '').replace('export async function', 'async function');
function setup(invoke) {
  const nodes = new Map();
  function node(id) {
    if (!nodes.has(id)) nodes.set(id, { textContent: '', disabled: false, hidden: false,
      setAttribute() {}, querySelector: key => node(`${id}/${key}`) });
    return nodes.get(id);
  }
  const document = { hidden: false, getElementById: node, querySelector: node, querySelectorAll: () => [] };
  const context = vm.createContext({ document, invoke, setTimeout, clearTimeout });
  vm.runInContext(source + `
    selected = 'community';
    installed = {channel:'official',version:'0.6.1.1'};
    offer = {version:'0.6.2'};
    status = {running:true,gameFolder:'fixture',initialized:true,installed};
    render();
    globalThis.api = {refreshGameStatus, task, selectChannel};
  `, context);
  return { ...context.api, node, document };
}
const state = running => ({running,gameFolder:'fixture',initialized:true,installed:{channel:'official',version:'0.6.1.1'}});

test('closing the game unlocks switching without restarting the launcher', async () => {
  const ui = setup(async () => state(false));
  assert.equal(ui.node('main-action').disabled, true);
  await ui.refreshGameStatus();
  assert.equal(ui.node('main-action').disabled, false);
  assert.equal(ui.node('main-action/span').textContent, 'Zu Community wechseln');
  assert.match(ui.node('.connection-status').textContent, /Spiel beendet/);
});

test('game start is observed and blocks updates with an explicit label', async () => {
  let running = false;
  const ui = setup(async () => state(running));
  await ui.refreshGameStatus();
  running = true;
  await ui.refreshGameStatus();
  assert.equal(ui.node('main-action').disabled, true);
  assert.match(ui.node('main-action/span').textContent, /Spiel läuft/);
});

test('focus/timer coalesce and user actions wait rather than colliding with the native helper', async () => {
  let finish, reads = 0, actionStarted = false;
  const ui = setup(() => { reads++; return new Promise(resolve => { finish = resolve; }); });
  const first = ui.refreshGameStatus(), second = ui.refreshGameStatus();
  const action = ui.task(async () => { actionStarted = true; });
  await ui.refreshGameStatus(); // Busy user action suppresses another poll.
  assert.equal(reads, 1);
  assert.equal(actionStarted, false);
  finish(state(false));
  await Promise.all([first, second, action]);
  assert.equal(actionStarted, true);
  assert.equal(ui.node('main-action').disabled, false);
});

test('a failed status check can recover on the next poll; hidden windows do not poll', async () => {
  let reads = 0;
  const ui = setup(async () => { if (++reads === 1) throw Error('helper failure'); return state(false); });
  ui.document.hidden = true;
  await ui.refreshGameStatus();
  assert.equal(reads, 0);
  ui.document.hidden = false;
  await ui.refreshGameStatus();
  assert.equal(ui.node('main-action').disabled, true);
  await ui.refreshGameStatus();
  assert.equal(ui.node('main-action').disabled, false);
});

test('channel selection refreshes game state before fetching the release', async () => {
  const calls = [];
  const ui = setup(async (_, {action}) => { calls.push(action); return action === 'status' ? state(false) : {version:'0.6.2',notes:''}; });
  await ui.selectChannel('community');
  assert.deepEqual(calls, ['status', 'fetch']);
  assert.equal(ui.node('main-action').disabled, false);
});
