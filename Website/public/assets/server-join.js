'use strict';
document.addEventListener('DOMContentLoaded', () => {
  const pending = new Map();
  document.querySelectorAll('[data-server-join]').forEach(host => {
    const id = host.dataset.serverJoin;
    if (!/^\d+$/.test(id)) return;
    const link = host.querySelector('a');
    if (!pending.has(id)) pending.set(id, fetch(`/servers/${id}/join?format=json`, {
      credentials: 'same-origin', cache: 'no-store', headers: {Accept: 'application/json'}, signal: AbortSignal.timeout(8000)
    }).then(r => { if (!r.ok) throw new Error('unavailable'); return r.json(); }));
    pending.get(id).then(data => {
      // Build the external URI from a restricted address, never accept arbitrary URL schemes.
      if (!/^(?:[0-9.]+|\[[0-9a-f:]+\]):[0-9]{1,5}$/i.test(data.address)) return;
      link.href = `steam://connect/${data.address}`;
      link.title = `Mit Steam verbinden: ${data.address}`;
      host.querySelector('[data-join-address]').textContent = data.address;
    }).catch(() => { link.title = 'Spieladresse prüfen und mit Steam verbinden'; });
  });
});
