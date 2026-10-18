const fs = require('fs');
const dl = __dirname + '/icons/';
const dir = __dirname + '/';
const raw = s => { try { return fs.readFileSync(dl + s + '.svg', 'utf8'); } catch { return null; } };
const stroke = s => { const t = raw(s); return t && /stroke="currentColor"/.test(t) ? t.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>\s*$/, '') : null; };
const icon = s => {
  if (stroke(s)) return 'STROKE';
  try { return fs.readFileSync(dl + s + '.svg', 'utf8').match(/ d="([^"]+)"/)[1]; } catch { return null; }
};
// [name, group, icon slug or monogram, how it connects, account, status]
const D = [
  ['OpenRGB', 'pc', 'openrgb', 'Bundled with Ember. Also reaches ASUS, MSI, Gigabyte, ASRock, HyperX, SteelSeries and more when their own software is closed.', 'No account', ''],
  ['Logitech G', 'pc', 'logitechg', 'Through G HUB, per key. The programmable G keys show up as a device of their own.', 'No account', ''],
  ['Corsair', 'pc', 'corsair', 'Through iCUE. Keep iCUE up to date.', 'No account', ''],
  ['Razer Chroma', 'pc', 'razer', 'Through Synapse.', 'No account', 'Beta'],
  ['Philips Hue', 'room', 'philipshue', 'Through the bridge on your network.', 'No Hue account', ''],
  ['Govee', 'room', 'govee', 'Over your LAN with segments, or the Govee cloud with your own API key.', 'Key for cloud only', ''],
  ['Nanoleaf', 'room', 'nanoleaf', 'Light panels through the controller, laid out as they sit on your wall.', 'No account', 'Beta'],
  ['WiZ', 'room', 'wiz', 'Bulbs found on your network.', 'No account', 'Beta'],
  ['LIFX', 'room', 'lifx', 'Bulbs found on your network.', 'No account', 'Beta'],
  ['Yeelight', 'room', 'yeelight', 'Bulbs found on your network.', 'No account', 'Beta'],
  ['Kasa', 'room', 'tplink', 'Bulbs found on your network.', 'No account', 'Beta'],
  ['Meross', 'room', 'meross', 'Bulbs and light strips on the local network. Signs in once to learn their key.', 'Meross account', 'Beta'],
  ['Tapo', 'room', 'tplink', 'Bulbs on the local network. Switch on Third-Party Compatibility in the Tapo app.', 'Tapo account', 'Beta'],
  ['Meross plug', 'plug', 'meross', 'Reads what the whole PC draws at the wall. Never switches.', 'Meross account', 'Read only'],
  ['Tapo plug', 'plug', 'tplink', 'Reads what the whole PC draws at the wall. Never switches.', 'Tapo account', 'Read only'],
  ['Kasa plug', 'plug', 'tplink', 'Reads what the whole PC draws at the wall. Never switches.', 'No account', 'Read only'],
  ['Shelly plug', 'plug', 'shelly', 'Reads what the whole PC draws at the wall. Never switches.', 'No account', 'Read only'],
  ['Stream Deck', 'control', 'elgato', 'A plugin with keys for scenes, lights, brightness and fan modes, plus live CPU and GPU readouts.', 'No account', 'Beta'],
  ['Steam', 'games', 'steam', 'Games found for you, with covers and a last played line.', 'No account', ''],
  ['Epic Games', 'games', 'epicgames', 'Games found for you.', 'No account', ''],
  ['GOG', 'games', 'gogdotcom', 'Games found for you.', 'No account', ''],
  ['Ubisoft Connect', 'games', 'ubisoft', 'Games found for you.', 'No account', ''],
  ['EA app', 'games', 'ea', 'Games found for you.', 'No account', ''],
];
const cats = [['all', 'All'], ['pc', 'PC lighting'], ['room', 'Room lights'], ['plug', 'Plugs'], ['control', 'Controls'], ['games', 'Game libraries']];
const label = { pc: 'PC lighting', room: 'Room light', plug: 'Smart plug', control: 'Control', games: 'Game library' };
const cards = D.map((d, i) => {
  const p = icon(d[2]);
  const logo = p === 'STROKE' ? `<svg class="st" viewBox="0 0 48 48" aria-hidden="true">${stroke(d[2])}</svg>` : p ? `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${p}"/></svg>` : fs.existsSync(dl + d[2] + '.png') ? '<span class="m" aria-hidden="true"></span>' : `<b>${d[2]}</b>`;
  const badge = d[5] ? `<em class="${d[5] === 'Read only' ? 'ro' : ''}">${d[5]}</em>` : '';
  return `<article class="card" data-cat="${d[1]}" style="--k:${i}"><div class="logo">${logo}</div><div class="meta"><span class="kind">${label[d[1]]}</span>${badge}</div><h3>${d[0]}</h3><p>${d[3]}</p><div class="acct">${d[4]}</div></article>`;
}).join('\n');
const tabs = cats.map(c => `<button type="button" role="tab" data-c="${c[0]}" aria-selected="${c[0] === 'all'}">${c[1]} <span>${c[0] === 'all' ? D.length : D.filter(d => d[1] === c[0]).length}</span></button>`).join('');
const tpl = fs.readFileSync(dir + 'integrations.tpl.html', 'utf8');
fs.writeFileSync(dir + 'integrations.html', tpl.replace('{{CARDS}}', cards).replace('{{TABS}}', tabs));
console.log('built', D.length, 'cards;', D.filter(d => !icon(d[2]) && !fs.existsSync(dl + d[2] + '.png')).length, 'monograms');
