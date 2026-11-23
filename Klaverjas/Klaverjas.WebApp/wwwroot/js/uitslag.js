// Uitslag invullen: live hints per row, the status switch, and Enter moves to the next box.
// Everything also works without this script; it only makes typing quicker and mistakes visible.
(function () {
  'use strict';

  var form = document.getElementById('uitslag');
  if (!form) {
    return;
  }

  var afwezigScore = form.dataset.afwezig;
  var rijen = Array.prototype.slice.call(form.querySelectorAll('tr[data-rij]'));

  function status(rij) {
    var gekozen = rij.querySelector('input[type=radio]:checked');
    return gekozen ? gekozen.value : 'Speelt';
  }

  function probleem(rij) {
    var st = status(rij);
    var pts = rij.querySelector('input.pts').value.trim();
    var pits = rij.querySelector('input.pits').value.trim();
    var ronde = rij.querySelector('select.round').value;

    if (st === 'Afwezig') {
      return { tekst: 'Afwezig: krijgt ' + afwezigScore + ' punten', soort: 'info' };
    }
    if (pts === '') {
      return { tekst: 'Punten nog invullen', soort: 'warn', veld: 'pts' };
    }
    if (!/^\d+$/.test(pts)) {
      return { tekst: 'Alleen cijfers, graag', soort: 'bad', veld: 'pts' };
    }
    var totaal = Number(pts);
    if (totaal < 2500) {
      return { tekst: 'Klopt dit? Erg weinig punten', soort: 'warn', veld: 'pts' };
    }
    if (totaal > 8000) {
      return { tekst: 'Klopt dit? Erg veel punten', soort: 'warn', veld: 'pts' };
    }
    if (pits === '') {
      return { tekst: 'Pitten leeg (telt als 0)', soort: 'warn', veld: 'pits' };
    }
    if (!/^\d+$/.test(pits)) {
      return { tekst: 'Alleen cijfers, graag', soort: 'bad', veld: 'pits' };
    }
    if (Number(pits) > 12) {
      return { tekst: 'Klopt dit? Erg veel pitten', soort: 'warn', veld: 'pits' };
    }
    if (st === 'Slaper') {
      return { tekst: 'Slaper in ronde ' + ronde + ' (de 1600 staat al in het totaal)', soort: 'info' };
    }
    if (pts === afwezigScore) {
      return { tekst: 'Precies ' + afwezigScore + ' gespeeld: wordt niet groen', soort: 'info' };
    }
    return null;
  }

  function toon(rij) {
    var st = status(rij);
    var pts = rij.querySelector('input.pts');
    var pits = rij.querySelector('input.pits');
    var afwezig = st === 'Afwezig';

    rij.className = 'is-' + st.toLowerCase();
    rij.querySelector('select.round').hidden = st !== 'Slaper';

    if (afwezig && !pts.disabled) {
      pts.dataset.bewaard = pts.value;
      pits.dataset.bewaard = pits.value;
      pts.value = afwezigScore;
      pits.value = '';
    } else if (!afwezig && pts.disabled) {
      pts.value = pts.dataset.bewaard || '';
      pits.value = pits.dataset.bewaard || '';
    }
    pts.disabled = afwezig;
    pits.disabled = afwezig;

    var p = probleem(rij);
    var hint = rij.querySelector('td.hint');
    hint.className = 'hint' + (p ? ' ' + p.soort : '');
    hint.textContent = p ? p.tekst : '';
    pts.classList.remove('flag', 'bad');
    pits.classList.remove('flag', 'bad');
    if (p && p.veld) {
      rij.querySelector('input.' + p.veld).classList.add(p.soort === 'bad' ? 'bad' : 'flag');
    }
  }

  function feiten() {
    var gespeeld = 0, slapers = 0, afwezig = 0, open = 0;
    rijen.forEach(function (rij) {
      var st = status(rij);
      if (st === 'Afwezig') {
        afwezig++;
        return;
      }
      gespeeld++;
      if (st === 'Slaper') {
        slapers++;
      }
      if (rij.querySelector('input.pts').value.trim() === '') {
        open++;
      }
    });
    var html = '<span><b>' + gespeeld + '</b> gespeeld</span><span><b>' + slapers + '</b> slapers</span><span><b>' + afwezig + '</b> afwezig</span>';
    if (open > 0) {
      html += '<span class="todo">Nog ' + open + ' keer punten invullen</span>';
    }
    document.getElementById('feiten').innerHTML = html;
  }

  rijen.forEach(toon);
  feiten();

  form.addEventListener('change', function (e) {
    var rij = e.target.closest('tr[data-rij]');
    if (!rij) {
      return;
    }
    toon(rij);
    feiten();
    if (e.target.type === 'radio') {
      var pts = rij.querySelector('input.pts');
      if (!pts.disabled) {
        pts.focus();
        pts.select();
      }
    }
  });

  form.addEventListener('input', function (e) {
    var rij = e.target.closest('tr[data-rij]');
    if (rij) {
      toon(rij);
      feiten();
    }
  });

  // Enter moves on instead of submitting; in the new-substitute box it adds the substitute.
  form.addEventListener('keydown', function (e) {
    if (e.key !== 'Enter' || e.target.tagName === 'BUTTON' || e.target.tagName === 'A') {
      return;
    }
    e.preventDefault();
    if (e.target.name === 'NieuweInvaller') {
      form.querySelector('button[formaction*="handler=Invaller"]').click();
      return;
    }
    if (!e.target.classList.contains('num')) {
      return;
    }
    var vakjes = Array.prototype.slice.call(form.querySelectorAll('input.num:not(:disabled)'));
    var i = vakjes.indexOf(e.target);
    if (i >= 0 && i < vakjes.length - 1) {
      vakjes[i + 1].focus();
      vakjes[i + 1].select();
    } else {
      document.getElementById('opslaan').focus();
    }
  });
})();
