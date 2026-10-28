// The win screen's celebration: a burst of square confetti (square on purpose: it matches the
// pixel cat), and the XP bar filling up from where it was.
(function () {
    'use strict';

    var stil = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    document.querySelectorAll('.xp-balk[data-van]').forEach(function (balk) {
        var vulling = balk.querySelector('span');
        if (!vulling || stil) {
            return;
        }

        vulling.style.transition = 'none';
        vulling.style.width = balk.dataset.van;

        // Two frames later, so the browser has painted the start width before it animates.
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                vulling.style.transition = '';
                vulling.style.width = balk.dataset.naar;
            });
        });
    });

    if (!document.querySelector('[data-feest]') || stil) {
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
