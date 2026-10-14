'use strict';
document.querySelectorAll('[data-showcase-avatar]').forEach(img => {
    const loaded = () => { if (img.naturalWidth > 0) img.classList.add('is-loaded'); };
    img.addEventListener('load', loaded);
    img.addEventListener('error', () => { img.hidden = true; });
    if (img.complete) loaded();
});
document.querySelectorAll('[data-player-showcase]').forEach(root => {
    const select = root.querySelector('[data-showcase-server]');
    const reload = root.querySelector('[data-showcase-reload]');
    const cards = root.querySelector('[data-showcase-cards]');
    const facts = root.querySelector('[data-showcase-facts]');
    const message = root.querySelector('[data-showcase-message]');
    const source = root.querySelector('[data-showcase-source]');
    const rules = root.querySelector('[data-showcase-rules]');
    const player = root.dataset.playerId;
    let sequence = 0, active;
    const element = (tag, className, value) => {
        const node = document.createElement(tag); node.className = className; node.textContent = String(value ?? ''); return node;
    };
    async function load() {
        if (!select || select.disabled || !/^\d+$/.test(select.value) || !/^\d+$/.test(player)) return;
        const current = ++sequence;
        if (active) active.abort();
        active = new AbortController(); const controller = active;
        const timer = setTimeout(() => controller.abort(), 25000);
        cards.replaceChildren(); facts.replaceChildren(); source.textContent = ''; rules.textContent = '';
        message.textContent = 'Statistiken werden geladen …'; root.setAttribute('aria-busy', 'true'); reload.disabled = true;
        root.querySelector('[data-showcase-statistics]').href = `/players/${player}/statistics?server=${select.value}`;
        root.querySelector('[data-showcase-economy]').href = `/players/${player}/economy?server=${select.value}`;
        const ranking = root.querySelector('[data-showcase-ranking]');
        if (ranking) ranking.href = `/servers/${select.value}/ranking`;
        try {
            const response = await fetch(`/players/${player}/highlights?server=${select.value}`, {signal: controller.signal, credentials: 'same-origin', cache: 'no-store', headers: {Accept: 'application/json'}});
            if (!response.ok) throw Error('unavailable');
            const data = await response.json();
            if (current !== sequence) return;
            message.textContent = typeof data.message === 'string' ? data.message : 'Keine Highlights verfügbar.';
            if (data.state !== 'ready') return;
            for (const card of (Array.isArray(data.cards) ? data.cards.slice(0, 3) : [])) {
                const article = element('article', `showcase-card${card.kind === 'ranking' ? ' is-ranking' : ''}`, '');
                article.append(element('span', 'showcase-card-kind', card.kind === 'ranking' ? '★ Serverplatzierung' : '◇ Persönlicher Meilenstein'), element('h4', '', card.label), element('strong', 'showcase-card-value', card.value), element('p', '', card.detail));
                if (typeof card.weapon === 'string' && /^[a-z0-9_]{1,40}$/.test(card.weapon)) {
                    const link = element('a', 'btn btn-secondary btn-small', 'Waffenrangliste öffnen');
                    link.href = `/servers/${select.value}/ranking?weapon=${encodeURIComponent(card.weapon)}#waffen`; article.append(link);
                }
                cards.append(article);
            }
            for (const fact of (Array.isArray(data.facts) ? data.facts.slice(0, 4) : [])) {
                const item = element('div', 'showcase-fact', ''); item.append(element('span', '', fact.label), element('strong', '', fact.value)); facts.append(item);
            }
            source.textContent = [data.statsAt && `Spielerwerte: ${data.statsAt}`, data.rankingAt && `Rangliste: ${data.rankingAt}`, data.weaponRankingAt && `Waffenrangliste: ${data.weaponRankingAt}`, data.since && `Spezialzähler seit ${data.since}`].filter(Boolean).join(' · ') + ' · Europe/Berlin';
            rules.textContent = [data.rules, data.notes].filter(Boolean).join(' ');
        } catch (error) {
            if (current === sequence) message.textContent = 'Statistiken sind gerade nicht erreichbar. Du kannst sie mit „Neu laden“ erneut abrufen.';
        } finally {
            clearTimeout(timer);
            if (current === sequence) {root.removeAttribute('aria-busy'); reload.disabled = false;}
        }
    }
    if (select) select.addEventListener('change', load);
    if (reload) reload.addEventListener('click', load);
    load();
});
