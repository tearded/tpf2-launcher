import { invoke } from '@tauri-apps/api/core';
import { listen } from '@tauri-apps/api/event';
import { getVersion } from '@tauri-apps/api/app';
import { check } from '@tauri-apps/plugin-updater';

const $ = id => document.getElementById(id);
const channels = { official: { name: 'Original', author: 'silver2127', description: 'Die veröffentlichte Version des Originalprojekts.' }, community: { name: 'Community', author: 'tearded', description: 'Die alternative Version von tearded mit eigenen Ergänzungen.' } };
let selected = 'official', installed = null, offer = null, status = null, busy = false, pendingUpdate = null, updateChecking = false, launcherVersion = '';
const radios = [...document.querySelectorAll('[data-channel]')];
let toastTimer;
function toast(message) { clearTimeout(toastTimer); $('toast').textContent = String(message); $('toast').hidden = false; toastTimer = setTimeout(() => $('toast').hidden = true, 6500); }
function message(text) { document.querySelector('.connection-status').textContent = String(text); }
function setMainLabel(text) { $('main-action').querySelector('span').textContent = text; }
function render() {
  const channel = channels[selected];
  radios.forEach(button => { const active = button.dataset.channel === selected; button.classList.toggle('selected', active); button.setAttribute('aria-checked', String(active)); button.tabIndex = active ? 0 : -1; button.disabled = busy; });
  $('channel-description').textContent = channel.description;
  $('installed-version').textContent = installed ? `${installed.version} · ${channels[installed.channel]?.name || 'Lokal'}` : status?.running ? 'Spiel läuft' : 'Nicht installiert';
  $('offered-version').textContent = offer?.version || '—';
  const same = installed?.channel === selected && installed?.version === offer?.version;
  $('release-status').textContent = same ? 'Aktuell' : installed?.channel === selected ? 'Update verfügbar' : 'Kanal wählen';
  setMainLabel(status?.recovery ? 'Gesicherten Stand wiederherstellen' : !status?.gameFolder ? 'Spielordner auswählen' : !status?.initialized ? selected === 'official' ? 'Multiplayer installieren' : 'Zuerst Original installieren' : same ? 'Spielen' : installed?.channel === selected ? 'Aktualisieren & spielen' : `Zu ${channel.name} wechseln`);
  $('main-action').disabled = busy || Boolean(status?.running) || (!status?.recovery && Boolean(status?.gameFolder) && (!offer || (!status?.initialized && selected !== 'official')));
  $('play-current').disabled = busy || !status?.gameFolder || !status?.initialized || status?.running || status?.recovery;
  ['check-game', 'open-folder', 'open-backups', 'choose-folder'].forEach(id => { if ($(id)) $(id).disabled = busy; });
  $('open-folder').setAttribute('aria-label', status?.gameFolder ? 'Spielordner öffnen' : 'Spielordner wählen');
  $('launcher-install').disabled = busy || !pendingUpdate;
  $('launcher-check').disabled = busy || updateChecking;
  $('main-action').setAttribute('aria-busy', String(busy));
}
async function native(action, options = {}) { return invoke('native_action', { action, channel: selected, ...options }); }
function acceptStatus(value) { status = value; installed = value.installed; render(); }
async function refreshStatus() { acceptStatus(await native('status')); }
async function fetchOffer() {
  offer = await native('fetch');
  $('news-channel').textContent = channels[selected].name.toUpperCase();
  $('news-version').textContent = offer ? `VERSION ${offer.version}` : 'KEIN RELEASE';
  $('news-headline').textContent = offer ? `Neu in ${offer.version}` : 'Noch kein Release';
  $('news-description').textContent = offer ? 'Versionshinweise des veröffentlichten Pakets.' : 'Für diesen Kanal liegt noch kein stabiles Paket vor.';
  $('news-points').hidden = true;
  $('release-notes').textContent = offer?.notes || '';
  $('release-link').href = `https://github.com/${channels[selected].author}/tpf2-multiplayer/releases`;
  render();
}
async function task(work) {
  if (busy) return;
  busy = true; render();
  try { await work(); }
  catch (error) { message(String(error)); toast(error); }
  finally { busy = false; render(); }
}
async function selectChannel(key) {
  if (busy || !channels[key]) return;
  selected = key; offer = null; render();
  await task(async () => { message('Release wird geprüft …'); await fetchOffer(); message(status?.running ? 'Spiel läuft · Änderungen erst nach dem Schließen' : 'Release geprüft · Download mit SHA-256-Prüfung'); });
}
async function performInstall() {
  $('preview-dialog').close();
  await task(async () => {
    const previous = installed?.channel;
    message('Version wird vorbereitet …');
    acceptStatus(await native('install', {version: offer.version}));
    message(`${channels[selected].name} ${installed.version} ist aktiv.`);
    if (previous === selected) { await native('play'); message('Spielstart über Steam angefordert.'); }
  });
}
async function mainAction() {
  if (busy) return;
  if (status?.recovery) { await task(async () => { acceptStatus(await native('recover')); message('Vorheriger Stand wiederhergestellt.'); }); return; }
  if (!status?.gameFolder) { await task(async () => { acceptStatus(await native('choose-folder')); message(status.gameFolder || 'Kein Spielordner gewählt.'); }); return; }
  if (!offer) return;
  if (installed?.channel === selected && installed.version === offer.version) { await task(async () => { await native('play'); message('Spielstart über Steam angefordert.'); }); return; }
  // Make a channel change/downgrade explicit. Selecting a channel never installs it.
  if (installed?.channel !== selected) {
    $('preview-title').textContent = status.initialized ? `Zu ${channels[selected].name} wechseln?` : 'Multiplayer installieren?';
    $('preview-message').textContent = `Version ${offer.version} wird installiert. ${installed ? `Aktuell: ${installed.version}. ` : ''}Alle Mitspieler müssen denselben Kanal und dieselbe Version verwenden.`;
    $('preview-dialog').showModal();return;
  }
  await performInstall();
}
async function checkLauncherUpdate() {
  if (busy || updateChecking) return;
  updateChecking = true; render();
  $('launcher-update-copy').textContent = 'Launcher-Updates werden geprüft …';
  try {
    if (pendingUpdate) await pendingUpdate.close();
    pendingUpdate = null;
    pendingUpdate = await check({ timeout: 20000 });
    $('launcher-update-copy').textContent = pendingUpdate ? `Version ${pendingUpdate.version} ist verfügbar.` : `Launcher ${launcherVersion} ist aktuell.`;
    $('launcher-info').querySelector('span:not(.status-dot)')?.remove();
    $('launcher-info').textContent = pendingUpdate ? `Launcher ${pendingUpdate.version} verfügbar` : `Launcher ${launcherVersion} · Aktuell`;
  } catch { $('launcher-update-copy').textContent = 'Updateprüfung gerade nicht möglich. Bitte später erneut prüfen.'; $('launcher-info').textContent = 'Launcher-Updates prüfen'; }
  finally { updateChecking = false; render(); }
}
async function installLauncherUpdate() {
  if (!pendingUpdate || busy) return;
  busy = true;render();
  let downloaded = 0, total = 0;
  try {
    await pendingUpdate.downloadAndInstall(event => {
      if (event.event === 'Started') total = event.data.contentLength || 0;
      if (event.event === 'Progress') downloaded += event.data.chunkLength;
      $('launcher-update-copy').textContent = event.event === 'Finished' ? 'Signatur geprüft. Der Launcher wird aktualisiert …' : `Launcher herunterladen … ${total ? Math.floor(downloaded / total * 100) + '%' : ''}`;
    });
  } catch (error) { $('launcher-update-copy').textContent = `Launcher-Update fehlgeschlagen: ${error}`; }
  finally { busy = false;render(); }
}

export async function initializeDesktop() {
  document.querySelector('.preview-label').textContent = 'WINDOWS';
  document.querySelector('.footer-version').textContent = '';
  $('installed-version').textContent = 'Wird geprüft …';$('offered-version').textContent = '—';
  $('news-headline').textContent = 'Release wird geladen …';$('news-description').textContent = '';$('news-points').hidden = true;
  const releaseNotes = document.createElement('pre');releaseNotes.id='release-notes';releaseNotes.className='release-notes';$('news-points').after(releaseNotes);
  const updateCard = document.querySelector('.update-preview');
  updateCard.innerHTML = '<svg aria-hidden="true"><use href="#download"/></svg><div><strong>Launcher aktualisieren</strong><p id="launcher-update-copy">Updates werden geprüft …</p></div>';
  const updateActions=document.createElement('div');updateActions.className='launcher-update-actions';
  updateActions.innerHTML='<button id="launcher-check" class="text-button">Updates prüfen</button><button id="launcher-install" class="primary-button" disabled>Launcher aktualisieren</button>';
  updateCard.after(updateActions);
  const folderChoice=document.createElement('button');folderChoice.id='choose-folder';folderChoice.className='text-button';folderChoice.textContent='Spielordner auswählen';updateActions.after(folderChoice);
  const confirm=$('preview-dialog').querySelector('.primary-button');confirm.classList.remove('close-dialog');confirm.textContent='Version installieren';confirm.addEventListener('click',performInstall);
  $('preview-dialog').querySelector('.eyebrow').textContent='VERSION WECHSELN';
  $('preview-dialog').querySelector('.demo-note').textContent='Deine Spielstände bleiben erhalten. Schließe vor dem Wechsel alle laufenden Spielinstanzen.';
  radios.forEach((button,index) => {
    button.addEventListener('click',()=>selectChannel(button.dataset.channel));
    button.addEventListener('keydown',event=>{if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)||busy)return;event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?radios.length-1:(index+1)%radios.length;radios[next].focus();selectChannel(radios[next].dataset.channel);});
  });
  ['open-settings','launcher-info'].forEach(id=>$(id).addEventListener('click',()=>$('settings-dialog').showModal()));
  document.querySelectorAll('.close-dialog').forEach(button=>button.addEventListener('click',()=>button.closest('dialog').close()));
  $('main-action').addEventListener('click',mainAction);
  $('play-current').addEventListener('click',()=>task(async()=>{await native('play');message('Spielstart über Steam angefordert.');}));
  $('check-game').addEventListener('click',()=>task(async()=>{await refreshStatus();await fetchOffer();message('Release geprüft.');}));
  $('open-folder').addEventListener('click',()=>task(async()=>{if(status?.gameFolder)await native('game-folder');else acceptStatus(await native('choose-folder'));}));
  $('choose-folder').addEventListener('click',()=>task(async()=>acceptStatus(await native('choose-folder'))));
  $('open-backups').addEventListener('click',()=>task(()=>native('backups')));
  $('release-link').addEventListener('click',event=>{event.preventDefault();task(()=>native('release-page'));});
  $('launcher-check').addEventListener('click',checkLauncherUpdate);
  $('launcher-install').addEventListener('click',installLauncherUpdate);
  await listen('native-progress',event=>message(event.payload.text+(event.payload.percent?` ${event.payload.percent}%`:'')));
  await listen('operation-busy',()=>toast('Eine Aktion läuft. Bitte vor dem Schließen kurz warten.'));
  launcherVersion=await getVersion();document.querySelector('.footer-version').textContent=`/ ${launcherVersion}`;
  render();document.documentElement.dataset.ready='true';
  await task(async()=>{
    message('Installation wird geprüft …');await refreshStatus();
    if(installed?.channel==='community')selected='community';
    await fetchOffer();message(status?.running?'Spiel läuft · Änderungen erst nach dem Schließen':status?.gameFolder?'Bereit · Download mit SHA-256-Prüfung':'Bitte den Transport-Fever-2-Spielordner auswählen.');
  });
  await checkLauncherUpdate();
}
