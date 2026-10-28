// A little burst of square confetti on the win screen. Square on purpose: it matches the pixel cat.
(function () {
    'use strict';

    if (!document.querySelector('[data-feest]')) {
        return;
    }

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        return;
    }

    var kleuren = ['#f2a65a', '#2f9e74', '#f49ca6', '#f3cf86', '#8f86e8', '#ffffff'];

    for (var i = 0; i < 48; i++) {
        var stukje = document.createElement('span');
        var maat = 6 + Math.round(Math.random() * 6);

        stukje.className = 'confetti';
        stukje.style.left = Math.round(Math.random() * 100) + 'vw';
        stukje.style.width = maat + 'px';
        stukje.style.height = maat + 'px';
        stukje.style.background = kleuren[i % kleuren.length];
        stukje.style.animationDelay = Math.round(Math.random() * 400) + 'ms';
        stukje.style.setProperty('--duur', (1.4 + Math.random() * 1.2).toFixed(2) + 's');
        stukje.style.setProperty('--drift', Math.round(Math.random() * 120 - 60) + 'px');
        stukje.style.setProperty('--draai', Math.round(Math.random() * 720 - 360) + 'deg');
        stukje.addEventListener('animationend', function () { this.remove(); });

        document.body.appendChild(stukje);
    }
})();
