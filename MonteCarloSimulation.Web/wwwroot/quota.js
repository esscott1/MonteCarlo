// "When can I submit again?" for the passphrase forms limited per network: the change-request pencil (Scenario runner)
// and the Observe passphrase (landing page). The server counts requests over a rolling hour (RequestQuota) and says
// exactly when the next one is allowed: a 429 carries retryAt, and every other response carries the X-Quota-* headers.
// A refusal is remembered in localStorage, so reopening the form or reloading the page still shows when it reopens.
window.QuotaNotice = (function () {
    function readStored(key) {
        try {
            const stored = JSON.parse(localStorage.getItem(key) || 'null');
            return stored && new Date(stored.retryAt) > new Date() ? { retryAt: new Date(stored.retryAt), limit: stored.limit } : null;
        } catch {
            return null;
        }
    }

    function writeStored(key, value) {
        try {
            if (value) localStorage.setItem(key, JSON.stringify({ retryAt: value.retryAt.toISOString(), limit: value.limit }));
            else localStorage.removeItem(key);
        } catch {
            // Storage unavailable: the notice still shows until this page is closed
        }
    }

    function clockTime(date) {
        return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    }

    function relativeTime(date) {
        const ms = date - Date.now();
        return ms <= 60_000 ? 'in less than a minute' : `in ${Math.ceil(ms / 60_000)} min`;
    }

    // storageKey: where a refusal is remembered. notice: the <p> that shows the message. submitButton: disabled while
    // refused. blockedText(limit, clock, relative) and remainingText(remaining, limit) word the two messages.
    function create({ storageKey, notice, submitButton, blockedText, remainingText }) {
        let blocked = readStored(storageKey);
        let timer = null;

        function render() {
            clearTimeout(timer);
            if (blocked && blocked.retryAt <= new Date()) {
                blocked = null;
                writeStored(storageKey, null);
                notice.hidden = true;
                notice.textContent = '';
                notice.classList.remove('blocked');
                submitButton.disabled = false;
                return;
            }
            if (!blocked) return;
            notice.textContent = blockedText(blocked.limit, clockTime(blocked.retryAt), relativeTime(blocked.retryAt));
            notice.classList.add('blocked');
            notice.hidden = false;
            submitButton.disabled = true;
            // Recount the minutes; wake exactly at the reopening time if that comes sooner
            timer = setTimeout(render, Math.min(15_000, Math.max(0, blocked.retryAt - Date.now()) + 50));
        }

        function block(retryAt, limit) {
            blocked = { retryAt, limit };
            writeStored(storageKey, blocked);
            render();
        }

        return {
            isBlocked: () => blocked !== null && blocked.retryAt > new Date(),

            // Show any remembered refusal, e.g. when the form opens
            restore: render,

            // After any response: a 429 blocks until its retryAt; anything else shows what's left, and using the last
            // one blocks right away, so the form doesn't have to be refused once to find out.
            update(response, body) {
                const limit = Number(response.headers.get('X-Quota-Limit')) || (body && body.limit) || 5;
                if (response.status === 429 && body && body.retryAt) {
                    block(new Date(body.retryAt), limit);
                    return;
                }
                const remaining = response.headers.get('X-Quota-Remaining');
                const nextSlot = response.headers.get('X-Quota-Next-Slot-At');
                if (remaining === null) return;
                if (Number(remaining) === 0 && nextSlot) {
                    block(new Date(nextSlot), limit);
                    return;
                }
                notice.classList.remove('blocked');
                notice.textContent = remainingText(Number(remaining), limit);
                notice.hidden = false;
            },
        };
    }

    return { create };
})();
