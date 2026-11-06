/*! vortex-tools-style v2.0.0 | vortex-theme.js: one light/dark choice for every Vortex tool */
/*
 * Load it synchronously in <head>, so the theme is set before the first paint:
 *
 *     <script src=".../vortex-theme.js"></script>
 *
 * The choice is "system", "light" or "dark". "light" and "dark" live in the cookie `vortex-theme`;
 * on a *.the-vortex.nl host the cookie is set for the whole domain, so every tool shares one
 * choice. No cookie means: follow the system setting. The effective theme lands on <html> as
 * data-bs-theme (Bootstrap does the rest), the choice as data-vtx-theme-choice (the toggle's icon).
 *
 * Every element with data-vortex-theme-toggle cycles system -> light -> dark on click. Its
 * aria-label and title come from its data-label-system / -light / -dark attributes, so each tool
 * keeps its own language.
 */
(function () {
    'use strict';

    var COOKIE = 'vortex-theme';
    var SHARED_DOMAIN = 'the-vortex.nl';
    var MAX_AGE = 34560000; // 400 days: the longest a browser keeps a cookie.

    // Where earlier versions of the tools kept the choice. Adopted once, then removed.
    var LEGACY_KEYS = ['werkplaats-thema', 'vortex-theme'];

    var NEXT = { system: 'light', light: 'dark', dark: 'system' };

    var root = document.documentElement;
    var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
    var printing = false;
    var choice = readCookie() || adoptLegacy() || 'system';

    function readCookie() {
        var match = /(?:^|;\s*)vortex-theme=(light|dark)(?:;|$)/.exec(document.cookie);
        return match ? match[1] : null;
    }

    function writeCookie(value) {
        var host = window.location.hostname;
        var parts = [COOKIE + '=' + (value || '')];

        // Localhost and other hosts get a host-only cookie; a Domain attribute they do not
        // belong to would make the browser drop the cookie altogether.
        if (host === SHARED_DOMAIN || host.slice(-SHARED_DOMAIN.length - 1) === '.' + SHARED_DOMAIN) {
            parts.push('Domain=' + SHARED_DOMAIN);
        }

        parts.push('Path=/', 'SameSite=Lax', 'Max-Age=' + (value ? MAX_AGE : 0));

        if (window.location.protocol === 'https:') {
            parts.push('Secure');
        }

        document.cookie = parts.join('; ');
    }

    function adoptLegacy() {
        var found = null;

        try {
            for (var i = 0; i < LEGACY_KEYS.length; i++) {
                var value = window.localStorage.getItem(LEGACY_KEYS[i]);

                if (value !== null) {
                    window.localStorage.removeItem(LEGACY_KEYS[i]);

                    if (!found && (value === 'light' || value === 'dark')) {
                        found = value;
                    }
                }
            }
        } catch (e) {
            // Storage blocked (private mode): nothing to adopt.
        }

        if (found) {
            writeCookie(found);
        }

        return found;
    }

    function effectiveTheme() {
        if (printing) {
            return 'light';
        }

        if (choice === 'system') {
            return media && media.matches ? 'dark' : 'light';
        }

        return choice;
    }

    function apply() {
        root.setAttribute('data-bs-theme', effectiveTheme());
        root.setAttribute('data-vtx-theme-choice', choice);
        labelToggles();
    }

    function labelToggles() {
        var toggles = document.querySelectorAll('[data-vortex-theme-toggle]');

        for (var i = 0; i < toggles.length; i++) {
            var label = toggles[i].getAttribute('data-label-' + choice);

            if (label) {
                toggles[i].setAttribute('aria-label', label);
                toggles[i].setAttribute('title', label);
            }
        }
    }

    function choose(value) {
        choice = value;
        writeCookie(value === 'system' ? null : value);
        apply();
    }

    document.addEventListener('click', function (event) {
        var target = event.target;
        var toggle = target && target.closest ? target.closest('[data-vortex-theme-toggle]') : null;

        if (toggle) {
            event.preventDefault();
            choose(NEXT[choice]);
        }
    });

    if (media) {
        var followSystem = function () {
            if (choice === 'system') {
                apply();
            }
        };

        if (media.addEventListener) {
            media.addEventListener('change', followSystem);
        } else if (media.addListener) {
            media.addListener(followSystem);
        }
    }

    // Another tool, or another tab, may have changed the shared cookie meanwhile.
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            var current = readCookie() || 'system';

            if (current !== choice) {
                choice = current;
                apply();
            }
        }
    });

    // A framework that renders on its own may undo what apply() did. Blazor replaces the
    // prerendered markup once its circuit starts, so the toggle comes back without its label, and
    // a DOM patch of the whole document can drop the attributes on <html>. Put both back.
    if (window.MutationObserver) {
        new MutationObserver(function (records) {
            var relabel = false;

            for (var i = 0; i < records.length; i++) {
                var record = records[i];

                if (record.type === 'attributes') {
                    if (record.target === root
                            && (root.getAttribute('data-bs-theme') !== effectiveTheme()
                                || root.getAttribute('data-vtx-theme-choice') !== choice)) {
                        apply();
                        return;
                    }
                } else if (record.addedNodes.length) {
                    relabel = true;
                }
            }

            if (relabel) {
                labelToggles();
            }
        }).observe(root, {
            attributes: true,
            attributeFilter: ['data-bs-theme', 'data-vtx-theme-choice'],
            childList: true,
            subtree: true
        });
    }

    // Paper is white.
    window.addEventListener('beforeprint', function () {
        printing = true;
        apply();
    });

    window.addEventListener('afterprint', function () {
        printing = false;
        apply();
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', apply);
    }

    apply();

    window.VortexTheme = {
        choice: function () {
            return choice;
        },
        set: function (value) {
            if (Object.prototype.hasOwnProperty.call(NEXT, value)) {
                choose(value);
            }
        }
    };
})();
