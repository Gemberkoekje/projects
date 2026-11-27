// The public site's screens, filled from the JSON that index.php puts in #site-data:
// home, Alles / Foto's / AI (one list, filtered), Albums, the viewer (an album, or a filtered list one
// picture at a time, with the filmstrip and full screen) and Over. Every screen has a #hash, so a link
// to it can be shared; #p-<id> opens one picture.
(() => {
  'use strict';
  const D = JSON.parse(document.getElementById('site-data').textContent);
  const KIND = { ai: 'AI-beeld', foto: 'Foto' };
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const pad = n => String(n).padStart(2, '0');

  const photos = D.photos; // newest first
  const byId = new Map(photos.map(p => [p.id, p]));
  // Albums with at least one picture, the one with the newest picture first.
  const albums = D.albums
    .map(a => ({ ...a, photos: photos.filter(p => p.album === a.id) }))
    .filter(a => a.photos.length)
    .sort((x, y) => photos.indexOf(x.photos[0]) - photos.indexOf(y.photos[0]));
  const albumById = new Map(albums.map(a => [a.id, a]));

  const FILTERS = {
    alles: { title: 'Alle beelden', lead: 'Foto’s en AI-beelden, de nieuwste eerst.', test: () => true },
    fotos: { title: 'Foto’s', lead: 'Alle foto’s, de nieuwste eerst.', test: p => p.kind === 'foto' },
    ai: { title: 'AI-beelden', lead: 'Alle beelden gemaakt met AI, de nieuwste eerst.', test: p => p.kind === 'ai' }
  };

  const tag = p => p.kind === 'ai' ? '<span class="tag">AI</span>' : '';
  const count = list => {
    const ai = list.filter(p => p.kind === 'ai').length, foto = list.length - ai;
    return [foto && `${foto} ${foto === 1 ? 'foto' : 'foto’s'}`, ai && `${ai} ${ai === 1 ? 'AI-beeld' : 'AI-beelden'}`].filter(Boolean).join(' · ');
  };
  const kinds = list => {
    const k = new Set(list.map(p => p.kind));
    return k.size === 2 ? 'Foto’s en AI-beelden' : k.has('ai') ? 'AI-beelden' : 'Foto’s';
  };
  const img = (p, size, extra = '') => `<img src="${esc(size === 'large' ? p.large : p.thumb)}" alt="${esc(p.title)}, ${KIND[p.kind]}" loading="lazy"${extra}>`;
  // A link that opens a picture in the viewer, in the list it was clicked in.
  const opener = (p, ctx) => `href="#p-${esc(p.id)}" data-open="${esc(p.id)}" data-ctx="${esc(ctx)}"`;

  /* ---------- Home ---------- */
  const featured = byId.get(D.featured) || photos[0];
  if (featured) {
    $('#hero-label').textContent = (featured.id === D.featured ? 'Uitgelicht · ' : 'Nieuw · ') + KIND[featured.kind];
    $('#hero-title').textContent = featured.title;
    const album = albumById.get(featured.album);
    $('#hero-text').textContent = featured.caption || (album ? `Uit het album ${album.title}.` : `${KIND[featured.kind]}, ${featured.year}.`);
    $('#hero-links').innerHTML = `<a class="more" ${opener(featured, 'alles')}>Bekijk het beeld →</a><a class="more" href="#alles" data-go="alles">Alle beelden →</a>`;
    const open = $('#hero-open');
    open.setAttribute('href', `#p-${featured.id}`);
    open.dataset.open = featured.id;
    open.dataset.ctx = 'alles';
    $('#hero-img').src = featured.large;
    $('#hero-img').alt = `${featured.title}, ${KIND[featured.kind]}`;
    $('#hero-cap').textContent = `${featured.title}, ${featured.year} · ${KIND[featured.kind]}`;
    $('#hero-print').hidden = false;

    $('#latest').innerHTML = photos.slice(0, 8).map(p =>
      `<a class="thumb" ${opener(p, 'alles')} aria-label="${esc(p.title)}, ${KIND[p.kind]}"><img src="${esc(p.thumb)}" alt="" loading="lazy">${tag(p)}</a>`).join('');
    $('#band-latest').hidden = false;

    const newestFoto = photos.find(p => p.kind === 'foto'), newestAi = photos.find(p => p.kind === 'ai');
    if (newestFoto && newestAi) {
      $('#pair-foto').src = newestFoto.thumb;
      $('#pair-ai').src = newestAi.thumb;
      $('#band-pair').hidden = false;
    }
  } else {
    $('#hero').classList.add('only-text');
  }

  let albumCarousel = { refresh() {} };
  if (albums.length) {
    $('#car-albums .car-track').innerHTML = albums.map(a => `<a class="car-card linkcard" href="#a-${esc(a.id)}" data-go="a-${esc(a.id)}">
        <span class="thumb">${img(a.photos[0], 'large', ' style="aspect-ratio:3/2;object-fit:cover"')}${tag(a.photos[0])}</span>
        <h3>${esc(a.title)}</h3>
        <p class="meta">${esc(count(a.photos))}</p>
        ${a.description ? `<p>${esc(a.description)}</p>` : ''}
      </a>`).join('');
    albumCarousel = carousel($('#car-albums'));
    $('#band-albums').hidden = false;

    $('#album-list').innerHTML = albums.map(a => `<article class="album-row">
        <a class="thumb" href="#a-${esc(a.id)}" data-go="a-${esc(a.id)}">${img(a.photos[0], 'large')}${tag(a.photos[0])}</a>
        <div class="txt">
          <h2>${esc(a.title)}</h2>
          <p class="meta">${esc(count(a.photos))}</p>
          ${a.description ? `<p>${esc(a.description)}</p>` : ''}
          <div class="minis">${a.photos.slice(0, 6).map((p, i) => `<a href="#a-${esc(a.id)}" data-go="a-${esc(a.id)}" data-photo="${i}" aria-label="${esc(p.title)}"><img src="${esc(p.thumb)}" alt="" loading="lazy"></a>`).join('')}</div>
          <a class="more" href="#a-${esc(a.id)}" data-go="a-${esc(a.id)}">Bekijk het album →</a>
        </div>
      </article>`).join('');
  }

  function carousel(root) {
    const track = $('.car-track', root);
    const cards = [...track.children];
    const prev = $('[data-prev]', root), next = $('[data-next]', root), out = $('[data-count]', root);
    let i = 0;
    const set = n => {
      i = Math.max(0, Math.min(cards.length - 1, n));
      track.style.transform = `translateX(${-cards[i].offsetLeft}px)`;
      out.textContent = `${i + 1} / ${cards.length}`;
      prev.disabled = i === 0;
      next.disabled = i === cards.length - 1;
    };
    prev.addEventListener('click', () => set(i - 1));
    next.addEventListener('click', () => set(i + 1));
    let x0 = null;
    track.addEventListener('touchstart', e => { x0 = e.touches[0].clientX; }, { passive: true });
    track.addEventListener('touchend', e => {
      if (x0 === null) return;
      const dx = e.changedTouches[0].clientX - x0;
      if (Math.abs(dx) > 40) set(i + (dx < 0 ? 1 : -1));
      x0 = null;
    });
    addEventListener('resize', () => set(i));
    set(0);
    return { refresh: () => set(i) };
  }

  /* ---------- Alles, Foto's, AI ---------- */
  function renderIndex(filter) {
    const f = FILTERS[filter];
    const list = photos.filter(f.test);
    $('#ix-title').textContent = f.title;
    $('#ix-lead').textContent = f.lead;
    $('#chips').innerHTML = [['alles', 'Alles'], ['fotos', 'Foto’s'], ['ai', 'AI-beelden']].map(([id, t]) =>
      `<button class="chip" type="button" data-go="${id}" aria-pressed="${filter === id}">${t} (${photos.filter(FILTERS[id].test).length})</button>`).join('');
    $('#masonry').innerHTML = list.map(p => {
      const album = albumById.get(p.album);
      return `<figure>
        <a ${opener(p, filter)}>
          <span class="thumb"><img src="${esc(p.thumb)}" alt="${esc(p.title)}, ${KIND[p.kind]}" width="${p.w}" height="${p.h}" loading="lazy">${tag(p)}</span>
          <figcaption><b>${esc(p.title)}</b>, ${p.year} · ${KIND[p.kind]}${album ? `<br>${esc(album.title)}` : ''}</figcaption>
        </a>
      </figure>`;
    }).join('');
    $('#ix-empty').hidden = list.length > 0;
  }

  /* ---------- The viewer ---------- */
  // A context is the list the viewer walks through: an album, or Alles / Foto's / AI.
  function context(key) {
    if (key.startsWith('a-')) {
      const a = albumById.get(key.slice(2));
      if (!a) return null;
      return {
        key, list: a.photos, label: 'Album', title: a.title, essay: a.description ? [a.description] : [],
        specs: [['Beelden', count(a.photos)], ['Soort', kinds(a.photos)]], back: ['albums', '← Alle albums']
      };
    }
    const f = FILTERS[key] || FILTERS.alles;
    const list = photos.filter(f.test);
    return { key: FILTERS[key] ? key : 'alles', list, label: '', title: f.title, essay: [], specs: [['Beelden', count(list)]], back: [FILTERS[key] ? key : 'alles', '← Terug naar het overzicht'] };
  }

  const cur = { ctx: null, i: 0 };
  function openViewer(ctx, i) {
    cur.ctx = ctx;
    $('#v-label').textContent = ctx.label;
    $('#v-label').hidden = !ctx.label;
    $('#v-title').textContent = ctx.title;
    $('#v-essay').innerHTML = ctx.essay.map(t => `<p>${esc(t)}</p>`).join('');
    $('#v-specs').innerHTML = ctx.specs.map(([k, v]) => `<div><dt>${esc(k)}</dt><dd>${esc(v)}</dd></div>`).join('');
    const back = $('#v-back');
    back.setAttribute('href', '#' + ctx.back[0]);
    back.dataset.go = ctx.back[0];
    back.textContent = ctx.back[1];
    $('#v-strip').innerHTML = ctx.list.map((p, n) =>
      `<button type="button" data-thumb="${n}" aria-label="${pad(n + 1)}: ${esc(p.title)}"><img src="${esc(p.thumb)}" alt=""></button>`).join('');
    showPhoto(i);
  }

  function showPhoto(i) {
    const list = cur.ctx.list, n = list.length;
    if (!n) return;
    cur.i = ((i % n) + n) % n;
    const p = list[cur.i];
    const image = $('#v-img');
    image.src = p.large;
    image.alt = `${p.title}, ${p.year}, ${KIND[p.kind]}`;
    const line = `<b>${pad(cur.i + 1)} / ${esc(p.title)}, ${p.year}</b> — ${KIND[p.kind]}` + (p.caption ? `<span class="sub">${esc(p.caption)}</span>` : '');
    $('#v-cap').innerHTML = line;
    $('#v-count').textContent = `${cur.i + 1} / ${n}`;
    $$('#v-strip button').forEach((b, k) => b.setAttribute('aria-current', String(k === cur.i)));
    const strip = $('#v-strip'), thumb = strip.children[cur.i];
    if (thumb) strip.scrollTo({ left: thumb.offsetLeft - strip.clientWidth / 2 + thumb.clientWidth / 2, behavior: 'smooth' });
    $('#lb-img').src = p.large;
    $('#lb-img').alt = image.alt;
    $('#lb-cap').innerHTML = line;
    $('#lb-count').textContent = `${cur.ctx.title} · ${cur.i + 1} / ${n}`;
    // Outside an album, the address names the picture, so it can be shared.
    if (!cur.ctx.key.startsWith('a-')) setHash('p-' + p.id);
    [1, -1].forEach(d => { const q = list[(cur.i + d + n) % n]; if (q) new Image().src = q.large; });
  }

  $('#v-prev').addEventListener('click', () => showPhoto(cur.i - 1));
  $('#v-next').addEventListener('click', () => showPhoto(cur.i + 1));
  $('#v-strip').addEventListener('click', e => {
    const b = e.target.closest('[data-thumb]');
    if (b) showPhoto(+b.dataset.thumb);
  });

  const lb = $('#lightbox');
  const openLb = () => { lb.hidden = false; $('#lb-close').focus(); };
  const closeLb = () => { lb.hidden = true; $('#v-open').focus(); };
  $('#v-open').addEventListener('click', openLb);
  $('#v-full').addEventListener('click', openLb);
  $('#lb-close').addEventListener('click', closeLb);
  $('#lb-prev').addEventListener('click', () => showPhoto(cur.i - 1));
  $('#lb-next').addEventListener('click', () => showPhoto(cur.i + 1));

  /* ---------- Menu (phones) ---------- */
  const menu = $('#menu');
  const closeMenu = () => { menu.hidden = true; $('#menu-open').setAttribute('aria-expanded', 'false'); };
  $('#menu-open').addEventListener('click', () => { menu.hidden = false; $('#menu-open').setAttribute('aria-expanded', 'true'); });
  $('#menu-close').addEventListener('click', closeMenu);

  /* ---------- Screens ---------- */
  const screens = $$('[data-screen]');
  let current = null;

  function setHash(name) {
    current = name;
    if (location.hash.slice(1) !== name) history.replaceState(null, '', '#' + name);
  }

  function show(name, opts = {}) {
    let screen = name;
    let ctx = null, index = opts.photo || 0;
    if (name.startsWith('a-')) {
      ctx = context(name);
      screen = ctx ? 'viewer' : 'home';
    } else if (name.startsWith('p-')) {
      // One picture: walk through the list it was opened from (Alles by default).
      const p = byId.get(name.slice(2));
      ctx = p ? context(opts.ctx || 'alles') : null;
      if (ctx && !ctx.list.includes(p)) ctx = context('alles');
      index = ctx ? ctx.list.indexOf(p) : 0;
      screen = ctx ? 'viewer' : 'home';
    } else if (name in FILTERS) {
      screen = 'index';
    } else if (name === 'contact') {
      screen = 'over';
    } else if (name === 'albums' && !albums.length) {
      screen = 'home';
    }
    if (!screens.some(s => s.dataset.screen === screen)) screen = 'home';
    if (screen === 'home') name = 'home';

    screens.forEach(s => { s.hidden = s.dataset.screen !== screen; });
    if (screen === 'viewer') openViewer(ctx, index);
    if (screen === 'index') renderIndex(name);
    if (screen === 'home') albumCarousel.refresh();

    const navKey = screen === 'viewer' ? (ctx.key.startsWith('a-') ? 'albums' : ctx.key) : name;
    $$('.primary a, .menu a.big').forEach(a => {
      if (a.dataset.go === navKey) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
    });

    closeMenu();
    if (!lb.hidden) lb.hidden = true;
    if (name === 'contact') requestAnimationFrame(() => $('#contact').scrollIntoView());
    else if (!opts.keepScroll) window.scrollTo(0, 0);
    if (screen !== 'viewer' || name.startsWith('a-')) {
      current = name;
      if (location.hash.slice(1) !== name) location.hash = name;
    }
  }

  document.addEventListener('click', e => {
    const open = e.target.closest('[data-open]');
    if (open) {
      e.preventDefault();
      show('p-' + open.dataset.open, { ctx: open.dataset.ctx });
      return;
    }
    const go = e.target.closest('[data-go]');
    if (!go) return;
    e.preventDefault();
    // The filter chips switch the list in place, without jumping to the top.
    show(go.dataset.go, { photo: +go.dataset.photo || 0, keepScroll: go.classList.contains('chip') });
  });
  addEventListener('hashchange', () => {
    const h = location.hash.slice(1);
    if (h && h !== current) show(h);
  });

  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') {
      if (!lb.hidden) closeLb();
      else if (!menu.hidden) closeMenu();
      return;
    }
    if (/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)) return;
    if ($('[data-screen="viewer"]').hidden) return;
    if (e.key === 'ArrowLeft') showPhoto(cur.i - 1);
    if (e.key === 'ArrowRight') showPhoto(cur.i + 1);
  });

  /* ---------- Header shrinks once the page scrolls ---------- */
  const site = $('#site');
  addEventListener('scroll', () => {
    const y = scrollY;
    if (y > 90) site.classList.add('compact');
    else if (y < 20) site.classList.remove('compact');
  }, { passive: true });

  show(location.hash.slice(1) || 'home');
})();
