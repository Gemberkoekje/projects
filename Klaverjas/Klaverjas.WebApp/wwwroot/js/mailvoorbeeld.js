// "Zo ziet de e-mail eruit": the frame grows to the mail's own height, so the page scrolls, not the frame.
(function () {
  'use strict';
  var frame = document.getElementById('mail-inhoud');
  if (!frame) {
    return;
  }
  function pas() {
    var doc = frame.contentDocument;
    if (doc && doc.documentElement) {
      frame.style.height = (doc.documentElement.scrollHeight + 4) + 'px';
    }
  }
  frame.addEventListener('load', pas);
  window.addEventListener('resize', pas);
  pas();
})();
