// Stand: once the standings are online the button reads "Staat online ✓" and rests. Changing the
// note wakes it, so Dad can put the same standings online again with the new note.
(function () {
  'use strict';
  var knop = document.getElementById('online-knop');
  var bericht = document.getElementById('bericht');
  if (!knop || !bericht || !knop.disabled) {
    return;
  }
  var begin = bericht.value;
  bericht.addEventListener('input', function () {
    var anders = bericht.value !== begin;
    knop.disabled = !anders;
    knop.textContent = anders ? knop.dataset.opnieuw : 'Staat online ✓';
  });
})();
