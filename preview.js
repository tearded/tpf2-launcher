'use strict';
// Presentation-only state. No native operations, network requests or installation
// paths belong in this layer. The desktop bridge will provide verified state.
const channels = {
  official: {
    version: '0.6.1.1', name: 'Original', description: 'Die veröffentlichte Version des Originalprojekts.',
    status: 'Update verfügbar', action: 'Aktualisieren & spielen',
    headline: 'Mehr Zeit zum gemeinsamen Laden.',
    notes: 'Der Resync wartet jetzt zuverlässig, während die anderen Spieler ihre Welt noch laden.',
    points: ['Gemeinsames Laden mit drei Spielern korrigiert', 'Für alle Mitspieler dieselbe Version verwenden'],
    url: 'https://github.com/silver2127/tpf2-multiplayer/releases/tag/v0.6.1.1'
  },
  community: {
    version: '0.4.30', name: 'Community', description: 'Die alternative Version von tearded mit eigenen Ergänzungen.',
    status: 'Kanal wechseln', action: 'Zu Community wechseln',
    headline: 'Schneller wieder zusammenfinden.',
    notes: 'Die Community-Version ergänzt die Navigation zu Mitspielern und Kartenmarkierungen.',
    points: ['Zu Mitspielern und Kartenmarkierungen springen', 'Älterer Versionsstand als beim Original'],
    url: 'https://github.com/tearded/tpf2-multiplayer/releases/tag/v0.4.30'
  }
};
let selected = 'official';
let toastTimeout;
const byId = id => document.getElementById(id);
const channelButtons = [...document.querySelectorAll('[data-channel]')];
function selectChannel(key) {
  const channel = channels[key];
  if (!channel) return;
  selected = key;
  channelButtons.forEach(button => {
    const active = button.dataset.channel === key;
    button.classList.toggle('selected', active);
    button.setAttribute('aria-checked', String(active));
    button.tabIndex = active ? 0 : -1;
  });
  byId('channel-description').textContent = channel.description;
  byId('offered-version').textContent = channel.version;
  byId('release-status').textContent = channel.status;
  byId('main-action').querySelector('span').textContent = channel.action;
  byId('news-channel').textContent = channel.name.toUpperCase();
  byId('news-version').textContent = `VERSION ${channel.version}`;
  byId('news-headline').textContent = channel.headline;
  byId('news-description').textContent = channel.notes;
  byId('news-points').replaceChildren(...channel.points.map(point => {
    const item = document.createElement('li');
    const dot = document.createElement('span');
    dot.className = 'point-dot';
    item.append(dot, document.createTextNode(point));
    return item;
  }));
  byId('release-link').href = channel.url;
}
channelButtons.forEach((button, index) => {
  button.addEventListener('click', () => selectChannel(button.dataset.channel));
  button.addEventListener('keydown', event => {
    let target;
    if (['ArrowLeft', 'ArrowUp'].includes(event.key)) target = (index + channelButtons.length - 1) % channelButtons.length;
    if (['ArrowRight', 'ArrowDown'].includes(event.key)) target = (index + 1) % channelButtons.length;
    if (event.key === 'Home') target = 0;
    if (event.key === 'End') target = channelButtons.length - 1;
    if (target === undefined) return;
    event.preventDefault();
    selectChannel(channelButtons[target].dataset.channel);
    channelButtons[target].focus();
  });
});
function toast(message) {
  clearTimeout(toastTimeout);
  byId('toast').textContent = message;
  byId('toast').hidden = false;
  toastTimeout = setTimeout(() => { byId('toast').hidden = true; }, 4500);
}
for (const id of ['open-settings', 'launcher-info']) byId(id).addEventListener('click', () => byId('settings-dialog').showModal());
document.querySelectorAll('.close-dialog').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
byId('main-action').addEventListener('click', () => {
  byId('preview-title').textContent = selected === 'official' ? 'Bereit für die nächste Runde.' : 'Alle auf denselben Stand.';
  byId('preview-message').textContent = selected === 'official'
    ? 'Hier aktualisiert der fertige Launcher das Original auf Version 0.6.1.1 und startet anschließend das Spiel.'
    : 'Hier wechselt der fertige Launcher zur Community-Version 0.4.30. Die aktuelle Installation wird vorher gesichert. Alle Mitspieler müssen denselben Kanal verwenden.';
  byId('preview-dialog').showModal();
});
byId('play-current').addEventListener('click', () => toast('Vorschau: Start der installierten Version 0.5.6 über Steam.'));
byId('check-game').addEventListener('click', () => toast('Designvorschau: Angezeigt werden Beispieldaten, keine Live-Abfrage.'));
byId('open-folder').addEventListener('click', () => toast('Hier öffnet der fertige Launcher deinen erkannten Spielordner.'));
byId('open-backups').addEventListener('click', () => toast('Hier findest du später die gesicherten Multiplayer-Versionen.'));
