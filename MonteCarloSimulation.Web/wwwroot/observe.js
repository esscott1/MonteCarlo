// Verifies the session token issued by the passphrase flyout on the landing page before
// revealing anything here. A missing/invalid/expired token - including a direct visit to
// this URL, or one forged in DevTools - bounces straight back to the landing page, which
// reopens the passphrase flyout automatically (see menu.js's `observe=denied` handling).
(async function verifyObserveAccess() {
    const token = sessionStorage.getItem('observeToken');

    try {
        const response = await fetch('/api/observe-access/verify', {
            headers: token ? { 'X-Observe-Token': token } : {}
        });

        if (response.ok) {
            document.getElementById('observe-content').hidden = false;
            return;
        }
    } catch {
        // Falls through to the redirect below - a network error is treated the same as denied.
    }

    window.location.replace('/?observe=denied');
})();
