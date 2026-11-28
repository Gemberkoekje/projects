// The beheer's script. Without it every form still works; with it:
// - "Nieuw album…" in an album list shows a name field;
// - the upload form shows each chosen picture with its own title, and sends them one at a time with a
//   progress bar, so one failure doesn't lose the rest.
(() => {
  'use strict';

  // ---------- "Nieuw album…" ----------
  document.querySelectorAll('[data-album-select]').forEach(select => {
    const field = document.getElementById(select.id + '-new');
    const sync = () => {
      const isNew = select.value === '__new';
      field.hidden = !isNew;
      field.required = isNew;
      if (isNew) field.focus();
    };
    select.addEventListener('change', sync);
    sync();
  });

  // ---------- Upload ----------
  const form = document.getElementById('upload-form');
  if (!form || !window.FormData || !window.XMLHttpRequest) return;

  const input = document.getElementById('photos');
  const list = document.getElementById('file-list');
  const hint = document.getElementById('upload-hint');
  const button = document.getElementById('upload-btn');
  const csrf = form.querySelector('input[name="csrf"]').value;

  let items = [];
  let busy = false;

  const titleFrom = name => name.replace(/\.[^.]+$/, '').replace(/[\s_\-]+/g, ' ').trim().slice(0, 120) || 'Zonder titel';
  const size = bytes => bytes < 1048576
    ? Math.max(1, Math.round(bytes / 1024)) + ' kB'
    : (bytes / 1048576).toLocaleString('nl-NL', { maximumFractionDigits: 1 }) + ' MB';

  function render() {
    list.innerHTML = '';
    list.hidden = items.length === 0;
    items.forEach((item, i) => {
      const card = document.createElement('div');
      card.className = 'file-card';
      card.innerHTML = `
        <img alt="" src="${item.preview}">
        <div class="file-fields">
          <label for="t-${i}">Titel</label>
          <input type="text" id="t-${i}" maxlength="120" required>
          <p class="file-size muted small"></p>
          <div class="progress" hidden><span></span></div>
          <p class="file-status small" role="status"></p>
        </div>`;
      const title = card.querySelector('input[type="text"]');
      title.value = item.title;
      title.addEventListener('input', () => { item.title = title.value; });
      card.querySelector('.file-size').textContent = item.file.name + ' · ' + size(item.file.size);
      item.card = card;
      list.appendChild(card);
    });
    update();
  }

  function update() {
    const open = items.filter(it => !it.done);
    button.disabled = busy || open.length === 0;
    button.textContent = open.length > 1 ? `Uploaden (${open.length})` : 'Uploaden';
  }

  input.addEventListener('change', () => {
    items.forEach(it => URL.revokeObjectURL(it.preview));
    items = [...input.files].map(file => ({ file, title: titleFrom(file.name), preview: URL.createObjectURL(file), done: false }));
    hint.hidden = true;
    render();
  });

  function send(item) {
    return new Promise(resolve => {
      const data = new FormData();
      data.append('action', 'upload');
      data.append('ajax', '1');
      data.append('csrf', csrf);
      data.append('titles[0]', item.title.trim() || titleFrom(item.file.name));
      data.append('album', form.querySelector('[name="album"]').value);
      data.append('album_new', form.querySelector('[name="album_new"]').value);
      data.append('photos[0]', item.file, item.file.name);

      const bar = item.card.querySelector('.progress');
      const fill = bar.querySelector('span');
      const status = item.card.querySelector('.file-status');
      bar.hidden = false;
      status.className = 'file-status small';
      status.textContent = 'Bezig met uploaden…';

      const xhr = new XMLHttpRequest();
      xhr.open('POST', './');
      xhr.upload.addEventListener('progress', e => {
        if (!e.lengthComputable) return;
        const pct = Math.round(e.loaded / e.total * 100);
        fill.style.width = pct + '%';
        if (pct === 100) status.textContent = 'Verkleinen op de server…';
      });
      xhr.addEventListener('load', () => {
        let body = null;
        try { body = JSON.parse(xhr.responseText); } catch (e) { /* not JSON: a server error page */ }
        if (body && body.ok) {
          item.done = true;
          fill.style.width = '100%';
          status.className = 'file-status small ok';
          status.textContent = 'Staat op de site.';
          item.card.querySelectorAll('input').forEach(el => { el.disabled = true; });
        } else {
          status.className = 'file-status small bad';
          status.textContent = (body && body.error) || `Niet gelukt (de server antwoordde ${xhr.status}). Probeer het nog een keer.`;
        }
        resolve();
      });
      xhr.addEventListener('error', () => {
        status.className = 'file-status small bad';
        status.textContent = 'Geen verbinding met de server. Probeer het nog een keer.';
        resolve();
      });
      xhr.send(data);
    });
  }

  form.addEventListener('submit', async e => {
    e.preventDefault();
    if (busy) return;
    const albumNew = form.querySelector('[name="album_new"]');
    if (!albumNew.hidden && !albumNew.value.trim()) { albumNew.focus(); return; }
    busy = true;
    update();
    for (const item of items) {
      if (!item.done) await send(item);
    }
    busy = false;
    update();
    const failed = items.filter(it => !it.done).length;
    const added = items.length - failed;
    if (failed === 0) {
      hint.hidden = false;
      hint.className = 'hint ok';
      hint.textContent = items.length === 1 ? 'Het beeld staat op de site.' : `Alle ${items.length} beelden staan op de site.`;
      setTimeout(() => { window.location.href = './?p=beelden'; }, 1200);
    } else {
      hint.hidden = false;
      hint.className = 'hint bad';
      const done = added === 0 ? '' : (added === 1 ? 'Eén beeld staat op de site. ' : `${added} beelden staan op de site. `);
      const notDone = failed === 1 ? 'Eén beeld is niet gelukt; zie de melding erbij.' : `${failed} beelden zijn niet gelukt; zie de meldingen.`;
      hint.textContent = done + notDone + ' Druk op Uploaden om het opnieuw te proberen, of ';
      const refresh = document.createElement('a');
      refresh.href = './?p=beelden';
      refresh.textContent = 'ververs de lijst';
      hint.append(refresh, '.');
      button.disabled = false;
    }
  });
})();
