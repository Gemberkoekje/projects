// The front page: tabs, tool filters and the live cluster view. Loaded at the end of <body>, so the
// panels are all visible without JavaScript and hidden here before the first paint.
(() => {
  'use strict';

  const POLL_MS = 30 * 1000;
  const STALE_AFTER_MS = 3 * 60 * 1000;
  const COMMIT_URL = 'https://github.com/Gemberkoekje/gembernodes/commit/';
  const PANELS = ['tools', 'cluster', 'about'];

  const $ = (id) => document.getElementById(id);

  // Tabs ------------------------------------------------------------------------------------------
  const tabs = [...document.querySelectorAll('.tab')];

  function show(id) {
    for (const tab of tabs) {
      const panel = tab.getAttribute('aria-controls');
      tab.setAttribute('aria-selected', String(panel === id));
      $(panel).hidden = panel !== id;
    }
  }

  function select(id) {
    show(id);
    history.replaceState(null, '', '#' + id);
  }

  for (const tab of tabs) {
    tab.addEventListener('click', () => select(tab.getAttribute('aria-controls')));
  }

  document.querySelector('[data-tab-link]').addEventListener('click', (event) => {
    event.preventDefault();
    select('cluster');
    document.querySelector('.tabs').scrollIntoView({ behavior: 'smooth' });
  });

  const fromHash = location.hash.slice(1);
  show(PANELS.includes(fromHash) ? fromHash : 'tools');

  // Tool filters ----------------------------------------------------------------------------------
  const chips = [...document.querySelectorAll('.chip')];
  for (const chip of chips) {
    chip.addEventListener('click', () => {
      for (const other of chips) other.setAttribute('aria-pressed', String(other === chip));
      const filter = chip.dataset.filter;
      for (const card of document.querySelectorAll('#public .card')) {
        card.hidden = filter !== 'all' && card.dataset.cat !== filter;
      }
    });
  }

  // Live cluster view -----------------------------------------------------------------------------
  let snapshot = null;

  function isLive(s) {
    return s !== null && s.available && Date.now() - Date.parse(s.generatedAt) < STALE_AFTER_MS;
  }

  function ago(iso) {
    const seconds = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 1000));
    if (seconds < 5) return 'just now';
    if (seconds < 60) return seconds + 's ago';
    const minutes = Math.round(seconds / 60);
    if (minutes < 60) return minutes + ' min ago';
    const hours = Math.round(minutes / 60);
    if (hours < 48) return hours + ' h ago';
    return Math.round(hours / 24) + ' days ago';
  }

  function element(tag, className, text) {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text !== undefined) el.textContent = text;
    return el;
  }

  function meter(label, percent, known) {
    const fragment = document.createDocumentFragment();
    const row = element('div', 'lbl');
    row.append(element('span', '', label), element('span', '', known ? percent + '%' : '–'));
    const bar = element('div', 'bar');
    const fill = element('i');
    fill.style.width = (known ? percent : 0) + '%';
    bar.append(fill);
    fragment.append(row, bar);
    return fragment;
  }

  function nodeCard(node) {
    const card = element('div', 'node');
    const head = element('div', 'n');
    const ready = element('span', node.ready ? 'ready' : 'ready down');
    ready.append(element('span', 'dot'), document.createTextNode(node.ready ? 'Ready' : 'Not ready'));
    head.append(element('span', '', node.name), ready);
    card.append(head, meter('CPU', node.cpuPercent, node.usageKnown), meter('Memory', node.memoryPercent, node.usageKnown));
    return card;
  }

  function renderHero(live) {
    const bars = $('hero-nodes');
    const nodes = live ? snapshot.nodes : [];
    bars.replaceChildren(...(nodes.length ? nodes : [{}, {}, {}]).map((n) => {
      const bar = element('i', live ? (n.ready ? 'up' : 'down') : '');
      if (live) bar.title = n.name + (n.ready ? ': ready' : ': not ready');
      return bar;
    }));

    const ready = nodes.filter((n) => n.ready).length;
    $('hero-live').classList.toggle('off', !live);
    $('hero-live-text').textContent = live ? 'live' : 'offline';
    $('hero-summary').textContent = live
      ? `${ready}/${nodes.length} nodes · ${snapshot.appsRunning} apps`
      : 'status unavailable';
  }

  function renderDots(live) {
    for (const dot of document.querySelectorAll('[data-app]')) {
      const state = live ? snapshot.apps[dot.dataset.app] : undefined;
      dot.classList.toggle('up', state === 'up');
      dot.classList.toggle('down', state === 'down');
      dot.title = state === 'up' ? 'Running' : state === 'down' ? 'Down' : 'Status unknown';
    }
  }

  function renderCluster(live) {
    $('unavailable').hidden = live;
    $('nodes').hidden = !live;
    $('stats').hidden = !live;
    $('term-live').classList.toggle('off', !live);
    if (!live) {
      $('term-title').textContent = 'gembernodes';
      return;
    }

    const k3s = snapshot.nodes.length ? ' · k3s ' + snapshot.nodes[0].version.replace(/\+k3s\d+$/, '') : '';
    $('term-title').textContent = snapshot.nodes.length + ' nodes' + k3s;
    $('nodes').replaceChildren(...snapshot.nodes.map(nodeCard));
    $('s-apps').textContent = String(snapshot.appsRunning);
    $('s-pods').textContent = String(snapshot.podsRunning);

    const version = snapshot.version;
    if (version.isKnown) {
      const link = element('a', '', version.commit.slice(0, 7));
      link.href = COMMIT_URL + version.commit;
      $('s-version').replaceChildren(link);
      $('s-changed').textContent = ago(version.fetchedAt);
    } else {
      $('s-version').textContent = '–';
      $('s-changed').textContent = '–';
    }
  }

  function renderAgo() {
    $('ago').textContent = isLive(snapshot) ? 'updated ' + ago(snapshot.generatedAt) : 'offline';
  }

  function render() {
    const live = isLive(snapshot);
    renderHero(live);
    renderDots(live);
    renderCluster(live);
    renderAgo();
  }

  async function load() {
    try {
      const response = await fetch('/api/cluster', { cache: 'no-cache' });
      snapshot = response.ok ? await response.json() : null;
    } catch {
      snapshot = null;
    }
    render();
  }

  load();
  setInterval(load, POLL_MS);
  setInterval(renderAgo, 1000);
})();
