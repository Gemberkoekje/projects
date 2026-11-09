// DatumPrikker: everything works without this script; it only adds conveniences.
(() => {
    'use strict';

    // Keep the date rows numbered 0..n-1, which is what model binding needs.
    const renumber = (list) => {
        list.querySelectorAll('[data-optie]').forEach((row, index) => {
            row.querySelectorAll('input').forEach((input) => {
                input.name = input.name.replace(/Options\[[^\]]*\]/, `Options[${index}]`);
            });
        });
    };

    const nextDay = (value) => {
        if (!value) {
            return '';
        }
        const date = new Date(`${value}T12:00:00`);
        date.setDate(date.getDate() + 1);
        return date.toISOString().slice(0, 10);
    };

    const addRow = () => {
        const list = document.querySelector('[data-opties]');
        const template = document.getElementById('optie-sjabloon');
        if (!list || !template) {
            return;
        }

        const rows = list.querySelectorAll('[data-optie]');
        const previous = rows[rows.length - 1];
        const row = template.content.firstElementChild.cloneNode(true);

        // A new row starts the day after the last one, at the same times.
        if (previous) {
            const [date, start, end] = previous.querySelectorAll('input');
            const [newDate, newStart, newEnd] = row.querySelectorAll('input');
            newDate.value = nextDay(date.value);
            newStart.value = start.value;
            newEnd.value = end.value;
        }

        list.appendChild(row);
        renumber(list);
        row.querySelector('input').focus();
    };

    const removeRow = (button) => {
        const list = button.closest('[data-opties]');
        const row = button.closest('[data-optie]');
        if (!list || !row) {
            return;
        }

        if (list.querySelectorAll('[data-optie]').length > 1) {
            row.remove();
        } else {
            row.querySelectorAll('input').forEach((input) => { input.value = ''; });
        }
        renumber(list);
    };

    const copy = async (button) => {
        const text = button.dataset.kopieer;
        const label = button.textContent;
        try {
            await navigator.clipboard.writeText(text);
            button.textContent = 'Gekopieerd!';
        } catch {
            // Not a secure context (plain http): select the link so Ctrl+C works.
            const input = button.parentElement.querySelector('input');
            if (input) {
                input.select();
            }
            button.textContent = 'Selecteer en kopieer';
        }
        setTimeout(() => { button.textContent = label; }, 2000);
    };

    // A little party when an answer was stored or a poll was made; never for reduced motion.
    const party = () => {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || typeof Element.prototype.animate !== 'function') {
            return;
        }

        const colours = ['#6c3bff', '#ff5c7a', '#ffc83d', '#2ec4a0', '#23a866', '#e8960c'];
        const layer = document.createElement('div');
        layer.className = 'confetti';
        layer.setAttribute('aria-hidden', 'true');
        document.body.appendChild(layer);

        for (let i = 0; i < 90; i++) {
            const piece = document.createElement('i');
            piece.style.left = `${Math.random() * 100}%`;
            piece.style.background = colours[i % colours.length];
            if (i % 3 === 0) {
                piece.style.borderRadius = '50%';
                piece.style.width = '0.7rem';
                piece.style.height = '0.7rem';
            }
            layer.appendChild(piece);

            const drift = (Math.random() - 0.5) * 240;
            const fall = window.innerHeight + 60;
            piece.animate(
                [
                    { transform: 'translate(0, 0) rotate(0deg)', opacity: 1 },
                    { transform: `translate(${drift}px, ${fall}px) rotate(${Math.random() * 720 - 360}deg)`, opacity: 0.9 },
                ],
                { duration: 1600 + Math.random() * 1400, delay: Math.random() * 350, easing: 'cubic-bezier(.2,.6,.35,1)', fill: 'forwards' });
        }

        setTimeout(() => layer.remove(), 3600);
    };

    if (document.querySelector('[data-feest]')) {
        party();
    }

    document.addEventListener('click', (event) => {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) {
            return;
        }

        const copyButton = target.closest('[data-kopieer]');
        if (copyButton) {
            copy(copyButton);
            return;
        }

        if (target.closest('[data-voeg-toe]')) {
            addRow();
            return;
        }

        const removeButton = target.closest('[data-verwijder]');
        if (removeButton) {
            removeRow(removeButton);
        }
    });

    document.addEventListener('submit', (event) => {
        const question = event.target.dataset.bevestig;
        if (question && !window.confirm(question)) {
            event.preventDefault();
        }
    });
})();
