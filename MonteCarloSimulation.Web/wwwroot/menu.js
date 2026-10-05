// The landing page's header menu: the hamburger menu (Scenario runner, Model Info, Observe) and the Observe
// passphrase flyout. Wrapped in its own scope so it shares no names with optimal.js, which loads after it.
(function () {
    // User-supplied text is echoed back into the passphrase result panel, so it has to be escaped
    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function initObserveMenu() {
        const hamburgerToggle = document.getElementById('hamburger-toggle');
        const hamburgerMenu = document.getElementById('hamburger-menu');
        const observeMenuItem = document.getElementById('observe-menu-item');
        const header = hamburgerToggle.closest('.page-header');

        const observeFlyout = document.getElementById('observe-flyout');
        const observeCancel = document.getElementById('observe-cancel');
        const observeCloseButton = document.getElementById('observe-close');
        const observeResult = document.getElementById('observe-result');
        const observeSubmitButton = observeFlyout.querySelector('button[type="submit"]');

        let badPassphraseCloseTimer = null;

        function openMenu() {
            hamburgerMenu.hidden = false;
            hamburgerToggle.setAttribute('aria-expanded', 'true');
        }

        function closeMenu() {
            hamburgerMenu.hidden = true;
            hamburgerToggle.setAttribute('aria-expanded', 'false');
        }

        function openPassphraseFlyout() {
            closeMenu();
            observeFlyout.hidden = false;
            observeFlyout.querySelector('input').focus();
        }

        function closePassphraseFlyout() {
            if (badPassphraseCloseTimer !== null) {
                clearTimeout(badPassphraseCloseTimer);
                badPassphraseCloseTimer = null;
            }
            observeFlyout.hidden = true;
            observeFlyout.reset();
            observeResult.innerHTML = '';
            hamburgerToggle.focus();
        }

        hamburgerToggle.addEventListener('click', () => {
            if (hamburgerMenu.hidden) openMenu(); else closeMenu();
        });

        observeMenuItem.addEventListener('click', openPassphraseFlyout);
        observeCancel.addEventListener('click', closePassphraseFlyout);
        observeCloseButton.addEventListener('click', closePassphraseFlyout);

        document.addEventListener('keydown', (e) => {
            if (e.key !== 'Escape') return;
            if (!hamburgerMenu.hidden) closeMenu();
            if (!observeFlyout.hidden) closePassphraseFlyout();
        });

        document.addEventListener('click', (e) => {
            if (header.contains(e.target)) return;
            if (!hamburgerMenu.hidden) closeMenu();
            if (!observeFlyout.hidden) closePassphraseFlyout();
        });

        observeFlyout.addEventListener('submit', async (e) => {
            e.preventDefault();

            const passphrase = observeFlyout.elements.passphrase.value;

            observeSubmitButton.disabled = true;
            observeResult.innerHTML = '<p class="loading">Checking passphrase&hellip;</p>';

            try {
                const response = await fetch('/api/observe-access', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ passphrase })
                });

                // The rate limiter rejects before the endpoint runs, so there's no JSON body to read.
                if (response.status === 429) {
                    observeResult.innerHTML = '<div class="error-box"><p>Too many attempts from this address. Try again later.</p></div>';
                    return;
                }

                const data = await response.json().catch(() => ({}));

                if (!response.ok) {
                    observeResult.innerHTML = `<div class="error-box"><p>${escapeHtml(data.message || 'Incorrect passphrase.')}</p></div>`;
                    // Notifies the visitor, then drops them back to the plain landing page rather
                    // than leaving the form open for silent retries.
                    badPassphraseCloseTimer = setTimeout(closePassphraseFlyout, 2000);
                    return;
                }

                sessionStorage.setItem('observeToken', data.token);
                window.location.href = 'observe.html';
            } catch (err) {
                observeResult.innerHTML = `<div class="error-box"><p>Request failed: ${escapeHtml(err.message)}</p></div>`;
            } finally {
                observeSubmitButton.disabled = false;
            }
        });

        // A direct visit to observe.html without a valid session token bounces back here with
        // this query flag, so the passphrase flyout reopens immediately for the visitor.
        if (new URLSearchParams(location.search).get('observe') === 'denied') {
            openPassphraseFlyout();
            history.replaceState(null, '', location.pathname);
        }
    }

    initObserveMenu();
})();
