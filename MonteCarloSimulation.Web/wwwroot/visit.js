// Counts this browser session as one visit to the site, for the Observe page's Visits section. Loaded on the public pages
// only (not Observe or Translations), it reports once per tab session; later pages in the same tab see the flag and
// stay quiet. Automated browsers aren't counted. If storage is unavailable the visit is still counted, just once a page.
(function countVisit() {
    if (navigator.webdriver) return;
    try {
        if (sessionStorage.getItem('visitCounted')) return;
        sessionStorage.setItem('visitCounted', '1');
    } catch {
        // Private windows or blocked storage: count it anyway
    }
    if (navigator.sendBeacon) {
        navigator.sendBeacon('/api/visits');
    } else {
        fetch('/api/visits', { method: 'POST', keepalive: true }).catch(() => {});
    }
})();
