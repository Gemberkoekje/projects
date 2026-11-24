// Stand: once the standings are online the button reads "Staat online ✓" and rests. Changing the
// note wakes it, so Dad can put the same standings online again with the new note. Mailing takes a
// few seconds: its buttons say so and cannot be pressed twice.
(function () {
  'use strict';
  document.querySelectorAll('form.bezig').forEach(function (form) {
    form.addEventListener('submit', function () {
      form.querySelectorAll('button').forEach(function (knop) {
        knop.disabled = true;
        knop.textContent = form.dataset.bezig;
      });
    });
  });
})();

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
