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
        const quota = window.QuotaNotice.create({
            storageKey: 'quota:observe-access',
            notice: document.getElementById('observe-quota'),
            submitButton: observeSubmitButton,
            blockedText: (limit, clock, relative) =>
                `You've used all ${limit} passphrase attempts for this hour from this network. You can try again at ${clock} (${relative}).`,
            remainingText: (remaining, limit) => `${remaining} of ${limit} attempts left this hour.`,
        });

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
            quota.restore();
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

            // Still refused: say until when, without spending an attempt
            if (quota.isBlocked()) {
                quota.restore();
                return;
            }

            observeSubmitButton.disabled = true;
            observeResult.innerHTML = '<p class="loading">Checking passphrase&hellip;</p>';

            try {
                const response = await fetch('/api/observe-access', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ passphrase })
                });

                const data = await response.json().catch(() => ({}));
                // Every response says how many attempts are left; a 429 says when the next one is allowed
                quota.update(response, data);
                if (response.status === 429) {
                    observeResult.innerHTML = '';
                    return;
                }

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
                observeSubmitButton.disabled = quota.isBlocked();
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
