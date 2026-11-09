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
