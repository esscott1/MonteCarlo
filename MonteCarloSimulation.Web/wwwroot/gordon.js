// Gordon on the Scenario runner: a chat panel that walks a visitor through the inputs, fills them in and runs the plan.
// Shown only while the Observe page's "Enable Gordon" switch is on (GET /api/me's gordon, read by paywall.js).
//
// Three parts, so the scripted conversation (gordon-script.js) can later be swapped for the AI version without the rest
// changing:
// - page: the actions that read and fill the inputs and run the plan (the AI's tools later), each showing its field
// - digest: what a finished run means, as plain facts (what the AI will be told about results)
// - the panel: launcher, messages, quick-reply buttons and the text box, talking to an engine
window.Gordon = (() => {
    const form = document.getElementById('run-form');
    const { parseNumber, formatWithCommas } = Inputs;
    const reduceMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
    const wait = (ms) => new Promise((resolve) => setTimeout(resolve, reduceMotion() ? Math.min(ms, 120) : ms));

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // --- page: reading and filling the Scenario runner's inputs ---

    const el = form.elements;
    const num = (name) => parseNumber(el[name].value) || 0;
    const cents = (value) => Math.round(value * 100) / 100;
    const locked = (input) => input.classList.contains('locked');

    // Sets a field as typing would (its input and change events run the page's own updates), unless it's locked for this
    // visitor; the field it set, to show
    function setValue(name, value) {
        const input = el[name];
        if (locked(input)) return null;
        input.value = value;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        return input;
    }
    const setMoney = (name, value) => setValue(name, formatWithCommas(String(cents(value))));

    // Roth and Brokerage: a Free visitor's one total (the page splits it), or a Plus visitor's basis and gain fields,
    // split the way a Free total is (GET /api/me's freeDefaults), which the Your Money tab then shows
    function setAccount(account, amount) {
        const shares = Paywall.me.freeDefaults ?? { rothBasisShare: 1, brokerageGainShare: 0.5 };
        const [total, basis, gain, gainShare] = account === 'roth'
            ? ['rothTotal', 'initialRothBasis', 'initialRothUnrealizedGain', 1 - shares.rothBasisShare]
            : ['brokerageTotal', 'initialBrokerageBasis', 'initialBrokerageUnrealizedGain', shares.brokerageGainShare];
        if (!Paywall.can('account-basis-split')) return setMoney(total, amount);
        const gainAmount = cents(amount * gainShare);
        setMoney(gain, gainAmount);
        return setMoney(basis, amount - gainAmount);
    }

    const page = {
        can: (feature) => Paywall.can(feature),
        monthYear: (isoDate) => I18n.monthYear(new Date(`${isoDate}T00:00:00Z`)),
        // The share of the benefit at full retirement age (67) a claim starting on this date gets, by SSA's shape (app.js,
        // the same as Optimize and Optimize Social Security read a claim)
        socialSecurityShare: (birthdate, startDate) => ssShareOnDate(birthdate, startDate),

        get() {
            return {
                birthdate: el['birthdate'].value,
                retirementDate: el['retirementDate'].value,
                filingStatus: form.querySelector('input[name="filingStatus"]:checked')?.value ?? 'single',
                taxDeferred: num('initialTaxableBalance'),
                roth: num('initialRothBasis') + num('initialRothUnrealizedGain'),
                brokerage: num('initialBrokerageBasis') + num('initialBrokerageUnrealizedGain'),
                inheritance: num('newMoney'),
                inheritanceYears: Number(el['yearNewMoney'].value),
                socialSecurityMonthly: num('socialSecurityMonthlyAmount'),
                socialSecurityStart: el['socialSecurityStartDate'].value,
                mix: { stocks: Number(el['stockAllocation'].value), bonds: Number(el['bondAllocation'].value), cash: Number(el['cashAllocation'].value) },
                spending: num('withdrawal'),
                years: Number(el['years'].value),
                iterations: Number(el['iterations'].value),
            };
        },

        // One action ({ name, value }); the element it filled, to show, or null
        apply({ name, value }) {
            switch (name) {
                case 'setBirthdate': return setValue('birthdate', value);
                case 'setRetirementDate': return setValue('retirementDate', value);
                case 'setHorizon': return setValue('years', value);
                case 'setRuns': return setValue('iterations', value);
                case 'setFilingStatus':
                    Inputs.setFilingStatus(form, value);
                    return form.querySelector('fieldset.filing-status');
                case 'setTaxDeferred': return setMoney('initialTaxableBalance', value);
                case 'setRoth': return setAccount('roth', value);
                case 'setBrokerage': return setAccount('brokerage', value);
                case 'setInheritance':
                    setValue('yearNewMoney', value.years);
                    return setMoney('newMoney', value.amount);
                case 'setSocialSecurityAmount': return setMoney('socialSecurityMonthlyAmount', value);
                // Its input event marks the date as chosen, so it no longer follows the birthdate
                case 'setSocialSecurityStart': return setValue('socialSecurityStartDate', value);
                case 'setMix':
                    setValue('stockAllocation', value.stocks);
                    setValue('bondAllocation', value.bonds);
                    setValue('cashAllocation', value.cash);
                    Inputs.updateAllocation(form);
                    return el['stockAllocation'].closest('fieldset');
                case 'setSpending': return setMoney('withdrawal', value);
                case 'showResults':
                    document.querySelector('#run-status .see-results')?.click();
                    return null;
                case 'run':
                    form.requestSubmit();
                    return null;
                // The page's Optimize (app.js initOptimize): it fills in its answer, then runs
                case 'optimize':
                    document.getElementById('optimize-run').click();
                    return null;
                default: return null;
            }
        },
    };

    // Brings a filled field into view - opening the inputs and its tab - and flashes it
    function reveal(target) {
        const section = document.getElementById('inputs-section');
        if (!section.open) section.open = true;
        const panel = target.closest('.tab-panel');
        if (panel?.hidden) document.querySelector(`[role="tab"][aria-controls="${panel.id}"]`)?.click();
        target.classList.remove('gordon-filled');
        void target.offsetWidth;
        target.classList.add('gordon-filled');
        setTimeout(() => target.classList.remove('gordon-filled'), 1800);
        if (matchMedia('(min-width: 901px)').matches) target.scrollIntoView({ block: 'nearest', behavior: reduceMotion() ? 'auto' : 'smooth' });
    }

    // --- digest: a finished run, as facts ---

    function digest(view) {
        if (view?.kind === 'errors') {
            const first = Object.values(view.errors ?? {}).flat()[0];
            return { ok: false, message: first ? I18n.translateServerMessage(first) : null };
        }
        if (view?.kind !== 'results') return { ok: false, message: view?.message ?? null };
        const { parameters: p, output: o } = view;
        const bands = o.balanceBands;
        const last = bands[bands.length - 1];
        const ageAt = (isoDate) => Math.floor((Date.parse(isoDate) - Date.parse(p.birthdate)) / (365.25 * 864e5));
        const survived = p.iterations - o.result.outOfMoneyCount;
        // A typical market and the weakest 10%: the runs at the 50th and 10th percentiles, ranked by how soon they fell
        // short, then by what they ended with. One that fell short says when and by how much in that year (app.js's
        // yearShortfall, as its year table says); the money doesn't run dry, since Social Security and any annuity pay on
        const severity = (run) => (run.failed ? run.years.length : Number.MAX_SAFE_INTEGER);
        const ranked = [...o.result.runs].sort((a, b) => severity(a) - severity(b) || a.endingBalance - b.endingBalance);
        const shortAt = (share) => {
            const run = ranked[Math.floor((ranked.length - 1) * share)];
            if (!run?.failed) return { year: null, age: null, short: null, share: null };
            const year = run.years[run.years.length - 1];
            const s = yearShortfall(year);
            return { year: year.calendarYear, age: Math.floor(year.ageInYear + 0.005), short: s.short, share: s.share };
        };
        const typical = shortAt(0.5);
        const weak = shortAt(0.1);
        return {
            ok: true,
            total: p.iterations,
            survived,
            rate: survived / p.iterations,
            years: p.years,
            median: last.middle,
            lower: last.lower,
            endYear: last.date.slice(0, 4),
            endAge: ageAt(last.date),
            lowerShortYear: weak.year,
            lowerShortAge: weak.age,
            lowerShort: weak.short,
            lowerShortShare: weak.share,
            medianShortYear: typical.year,
            medianShortAge: typical.age,
            medianShort: typical.short,
            medianShortShare: typical.share,
            monthly: p.withdrawal / 12,
        };
    }

    // --- the panel ---

    const launcher = document.createElement('button');
    launcher.type = 'button';
    launcher.className = 'gordon-launcher';
    launcher.hidden = true;
    launcher.setAttribute('aria-expanded', 'false');
    launcher.setAttribute('aria-controls', 'gordon-panel');

    const panel = document.createElement('section');
    panel.className = 'gordon-panel';
    panel.id = 'gordon-panel';
    panel.hidden = true;
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-labelledby', 'gordon-title');
    panel.innerHTML = `
        <div class="gordon-header">
            <span class="gordon-avatar" aria-hidden="true">G</span>
            <div class="gordon-heading" tabindex="0">
                <h2 id="gordon-title">Gordon</h2>
                <p class="gordon-subtitle"></p>
            </div>
            <button type="button" class="gordon-restart secondary-button"></button>
            <button type="button" class="flyout-close gordon-minimize" aria-expanded="true">&minus;</button>
            <button type="button" class="flyout-close gordon-close">&times;</button>
        </div>
        <div class="gordon-log" role="log" aria-live="polite"></div>
        <div class="gordon-choices"></div>
        <form class="gordon-input">
            <input type="text" autocomplete="off" enterkeyhint="send">
            <button type="submit"></button>
        </form>`;

    const log = panel.querySelector('.gordon-log');
    const choiceBox = panel.querySelector('.gordon-choices');
    const inputForm = panel.querySelector('.gordon-input');
    const textBox = inputForm.querySelector('input');
    const header = panel.querySelector('.gordon-header');
    const heading = panel.querySelector('.gordon-heading');
    const minimizeButton = panel.querySelector('.gordon-minimize');

    let engine = null;
    let entries = [];          // what's been said: { who: 'gordon', message } or { who: 'visitor', text }
    let choices = [];
    let busy = false;
    let awaitingRun = false;
    let awaitingOptimize = false;
    let quietRun = false;      // the example's run: only "typing" dots until its one report
    let progress = null;       // Optimize's { completed, total } while it works, shown as a live message
    let session = 0;           // a restart or a close ends what was playing
    let playing = Promise.resolve();
    let minimized = false;     // folded to its header bar, so the page shows; the conversation carries on

    // Turns play one after another: Optimize's answer and the run that follows it can arrive close together
    const enqueue = (next) => { playing = playing.then(() => play(next)); };

    function renderText(message) {
        const values = message.values ? Object.fromEntries(Object.entries(message.values).map(([k, v]) => [k, escapeHtml(v)])) : undefined;
        return t(message.key, values);
    }

    function entryHtml(entry) {
        if (entry.who === 'visitor') return `<div class="gordon-message visitor">${escapeHtml(entry.text)}</div>`;
        const m = entry.message;
        if (m.kind === 'summary') {
            const bullets = m.lines.map((line) => `<li>${renderText(line)}</li>`).join('');
            if (!m.lead && !m.after) return `<ul class="gordon-message gordon-summary">${bullets}</ul>`;
            // One message: its lead line, the bullets, and a closing line
            return `<div class="gordon-message">${m.lead ? `<p>${renderText({ key: m.lead })}</p>` : ''}`
                + `<ul class="gordon-list">${bullets}</ul>${m.after ? `<p>${renderText({ key: m.after })}</p>` : ''}</div>`;
        }
        return `<div class="gordon-message">${renderText(m)}</div>`;
    }

    const scrollLog = () => { log.scrollTop = log.scrollHeight; };

    function addEntry(entry) {
        // A summary reads the form when it's shown; kept as it was then, so a redraw shows the same
        if (typeof entry.message?.lines === 'function') entry = { ...entry, message: { ...entry.message, lines: entry.message.lines() } };
        entries.push(entry);
        log.insertAdjacentHTML('beforeend', entryHtml(entry));
        scrollLog();
        if (minimized && entry.who === 'gordon') {
            panel.classList.add('unread');
            renderMinimizeLabel();
        }
    }

    function renderChoices() {
        choiceBox.innerHTML = choices.map((c, i) => `<button type="button" class="gordon-choice" data-i="${i}">${renderText(c)}</button>`).join('');
        choiceBox.hidden = choices.length === 0;
        scrollLog();
    }

    function setBusy(on) {
        busy = on;
        textBox.disabled = on;
        inputForm.querySelector('button').disabled = on;
    }

    // The fixed text: re-applied when the language changes, along with everything already said
    function renderStatic() {
        launcher.innerHTML = `<span class="gordon-avatar" aria-hidden="true">G</span><span>${escapeHtml(t('gordon.launcher'))}</span>`;
        panel.querySelector('.gordon-subtitle').textContent = t('gordon.subtitle');
        panel.querySelector('.gordon-restart').textContent = t('gordon.restart');
        panel.querySelector('.gordon-close').setAttribute('aria-label', t('gordon.close'));
        heading.title = t('gordon.moveHint');
        heading.setAttribute('aria-label', `Gordon. ${t('gordon.moveHint')}`);
        renderMinimizeLabel();
        textBox.placeholder = t('gordon.placeholder');
        textBox.setAttribute('aria-label', t('gordon.placeholder'));
        inputForm.querySelector('button').textContent = t('gordon.send');
        log.innerHTML = entries.map(entryHtml).join('');
        renderProgress();
        renderChoices();
    }

    // Optimize's progress, one live message at the end of the conversation while it works; for the example's quiet run,
    // "typing" dots until the run is done
    function renderProgress() {
        let line = log.querySelector('.gordon-progress');
        if (quietRun ? !awaitingRun : !awaitingOptimize || !progress) {
            line?.remove();
            return;
        }
        if (!line) {
            line = document.createElement('div');
            line.className = 'gordon-message gordon-progress';
            log.appendChild(line);
        }
        if (quietRun) {
            if (!line.classList.contains('gordon-typing')) {
                line.classList.add('gordon-typing');
                line.setAttribute('aria-hidden', 'true');
                line.innerHTML = '<span></span><span></span><span></span>';
            }
        } else {
            line.textContent = progress.total > 0
                ? t('gordon.optimizing.progress', { completed: progress.completed, total: progress.total })
                : t('gordon.optimizing.start');
        }
        scrollLog();
    }

    // Fills the turn's fields one by one, each shown as it's filled; false when a restart or close ended the turn
    async function applyActions(actions, mine) {
        for (const action of actions) {
            const target = page.apply(action);
            if (target) {
                reveal(target);
                await wait(260);
            }
            if (mine !== session) return false;
        }
        return true;
    }

    // Plays one turn: each message after a short "typing" pause, the fields it fills once the reply to the visitor's
    // answer has been said (before the next question), then its buttons
    async function play(next) {
        const mine = session;
        setBusy(true);
        choices = [];
        renderChoices();
        const lead = next.lead ?? next.say.length;
        for (const [i, message] of next.say.entries()) {
            if (i === lead && !(await applyActions(next.actions, mine))) return;
            const typing = document.createElement('div');
            typing.className = 'gordon-message gordon-typing';
            typing.setAttribute('aria-hidden', 'true');
            typing.innerHTML = '<span></span><span></span><span></span>';
            log.appendChild(typing);
            scrollLog();
            const length = message.kind === 'summary' ? 200 : t(message.key).length;
            await wait(Math.min(1100, 350 + length * 6));
            typing.remove();
            if (mine !== session) return;
            addEntry({ who: 'gordon', message });
        }
        if (lead >= next.say.length && !(await applyActions(next.actions, mine))) return;
        // Show the full results: the page has scrolled to them, so fold out of their way
        if (next.actions.some((a) => a.name === 'showResults')) setMinimized(true, { focus: false });
        if (next.run || next.optimize) {
            // A field the browser refuses stops the run before it's sent, so there'd be no result to wait for
            if (!form.checkValidity()) {
                form.reportValidity();
                enqueue(engine.result({ ok: false, message: t('gordon.result.invalid') }));
                return;
            }
            awaitingRun = true;
            awaitingOptimize = !!next.optimize;
            quietRun = !!next.quiet;
            renderProgress();
            page.apply({ name: next.optimize ? 'optimize' : 'run' });
            return;
        }
        // Optimize's answer: still busy, until the run it starts has been explained
        if (next.hold) return;
        choices = next.choices;
        setBusy(false);
        renderChoices();
        if (!panel.hidden && !minimized && matchMedia('(pointer: fine)').matches) textBox.focus({ preventScroll: true });
    }

    function send(input, label) {
        if (busy || !engine) return;
        addEntry({ who: 'visitor', text: label });
        enqueue(engine.reply(input));
    }

    function begin() {
        session++;
        awaitingRun = false;
        awaitingOptimize = false;
        quietRun = false;
        progress = null;
        entries = [];
        log.innerHTML = '';
        textBox.value = '';
        engine = engine ?? GordonScript.create(page);
        enqueue(engine.start());
    }

    function open() {
        panel.hidden = false;
        launcher.setAttribute('aria-expanded', 'true');
        launcher.classList.add('open');
        setMinimized(false, { focus: false });
        place();
        if (entries.length === 0) begin();
        else if (!busy && matchMedia('(pointer: fine)').matches) textBox.focus({ preventScroll: true });
    }

    function close() {
        clearTimeout(autoMinimize);
        panel.hidden = true;
        launcher.setAttribute('aria-expanded', 'false');
        launcher.classList.remove('open');
    }

    // --- minimize: the panel folds to its header bar, which a new message marks with a dot ---

    function renderMinimizeLabel() {
        minimizeButton.textContent = minimized ? '+' : '−';
        minimizeButton.setAttribute('aria-expanded', String(!minimized));
        minimizeButton.setAttribute('aria-label', t(!minimized ? 'gordon.minimize'
            : panel.classList.contains('unread') ? 'gordon.restoreUnread' : 'gordon.restore'));
    }

    function setMinimized(on, { focus = true } = {}) {
        clearTimeout(autoMinimize);
        minimized = on;
        panel.classList.toggle('minimized', on);
        if (!on) panel.classList.remove('unread');
        renderMinimizeLabel();
        place();
        if (on) {
            if (focus) minimizeButton.focus();
        } else {
            scrollLog();
            if (focus && !busy && matchMedia('(pointer: fine)').matches) textBox.focus({ preventScroll: true });
        }
    }

    // Once a run Gordon started is explained, a pause to read it, then the page scrolls to the results and Gordon folds
    // out of their way - unless the visitor has touched the panel meanwhile
    const READ_PAUSE_MS = 5000;
    let autoMinimize = null;
    function minimizeAfterReading(mine) {
        clearTimeout(autoMinimize);
        autoMinimize = setTimeout(() => {
            if (mine !== session || panel.hidden || minimized) return;
            page.apply({ name: 'showResults' });
            setMinimized(true, { focus: false });
        }, READ_PAUSE_MS);
    }
    ['pointerdown', 'keydown'].forEach((type) => panel.addEventListener(type, () => clearTimeout(autoMinimize)));

    // --- moving: drag the header (or its heading with the arrow keys); a double-click or Home puts it back in the corner.
    // Where it was put is kept for the tab's session; it's always shown inside the window. A phone keeps the sheet. ---

    const POSITION_KEY = 'gordonPosition';
    const sheet = matchMedia('(max-width: 600px)');
    let position = null;       // { left, top } in pixels, chosen by the visitor; null = the bottom-right corner
    try { position = JSON.parse(sessionStorage.getItem(POSITION_KEY)); } catch { /* none kept */ }

    function savePosition() {
        try {
            if (position) sessionStorage.setItem(POSITION_KEY, JSON.stringify(position));
            else sessionStorage.removeItem(POSITION_KEY);
        } catch { /* not kept */ }
    }

    // The chosen spot, moved inside the window if the panel (or the window) no longer fits there
    function place() {
        if (!position || sheet.matches || panel.hidden) {
            panel.style.left = panel.style.top = panel.style.right = panel.style.bottom = '';
            return;
        }
        const { width, height } = panel.getBoundingClientRect();
        const left = Math.max(0, Math.min(position.left, innerWidth - width));
        const top = Math.max(0, Math.min(position.top, innerHeight - height));
        Object.assign(panel.style, { left: `${left}px`, top: `${top}px`, right: 'auto', bottom: 'auto' });
    }

    function moveTo(left, top) {
        position = { left: Math.round(left), top: Math.round(top) };
        place();
        // Kept as shown, so a later move starts from where it is on screen
        const rect = panel.getBoundingClientRect();
        position = { left: Math.round(rect.left), top: Math.round(rect.top) };
        savePosition();
    }

    function resetPosition() {
        position = null;
        savePosition();
        place();
    }

    let drag = null;           // { id, dx, dy, startX, startY, moved }
    header.addEventListener('pointerdown', (e) => {
        if (sheet.matches || e.button !== 0 || e.target.closest('button')) return;
        const rect = panel.getBoundingClientRect();
        drag = { id: e.pointerId, dx: e.clientX - rect.left, dy: e.clientY - rect.top, startX: e.clientX, startY: e.clientY, moved: false };
        header.setPointerCapture(e.pointerId);
    });
    header.addEventListener('pointermove', (e) => {
        if (!drag || e.pointerId !== drag.id) return;
        if (!drag.moved && Math.hypot(e.clientX - drag.startX, e.clientY - drag.startY) < 4) return;
        drag.moved = true;
        panel.classList.add('dragging');
        moveTo(e.clientX - drag.dx, e.clientY - drag.dy);
    });
    let dragged = false;       // the click that ends a drag isn't a click on the bar
    const endDrag = (e) => {
        if (!drag || e.pointerId !== drag.id) return;
        dragged = drag.moved;
        drag = null;
        panel.classList.remove('dragging');
    };
    header.addEventListener('pointerup', endDrag);
    header.addEventListener('pointercancel', endDrag);
    // A click on the folded bar opens it again
    header.addEventListener('click', (e) => {
        if (dragged) {
            dragged = false;
            return;
        }
        if (minimized && !e.target.closest('button')) setMinimized(false);
    });
    header.addEventListener('dblclick', (e) => {
        if (!e.target.closest('button')) resetPosition();
    });
    heading.addEventListener('keydown', (e) => {
        const step = e.shiftKey ? 80 : 20;
        const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
        if (e.key === 'Home') {
            e.preventDefault();
            resetPosition();
        } else if ((e.key === 'Enter' || e.key === ' ') && minimized) {
            e.preventDefault();
            setMinimized(false);
        } else if (moves[e.key] && !sheet.matches) {
            e.preventDefault();
            const rect = panel.getBoundingClientRect();
            moveTo(rect.left + moves[e.key][0], rect.top + moves[e.key][1]);
        }
    });
    addEventListener('resize', place);
    sheet.addEventListener('change', place);

    launcher.addEventListener('click', () => (panel.hidden ? open() : close()));
    minimizeButton.addEventListener('click', () => setMinimized(!minimized));
    panel.querySelector('.gordon-close').addEventListener('click', () => { close(); launcher.focus(); });
    panel.querySelector('.gordon-restart').addEventListener('click', begin);
    panel.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { close(); launcher.focus(); }
    });
    choiceBox.addEventListener('click', (e) => {
        const button = e.target.closest('.gordon-choice');
        if (!button) return;
        const c = choices[Number(button.dataset.i)];
        if (c) send({ choice: c.value }, renderText(c).replace(/<[^>]+>/g, ''));
    });
    inputForm.addEventListener('submit', (e) => {
        e.preventDefault();
        const text = textBox.value.trim();
        if (!text) return;
        textBox.value = '';
        send({ text }, text);
    });

    // Optimize, started by Gordon, is working (app.js): its progress
    document.addEventListener('runner:optimizing', (e) => {
        if (!awaitingOptimize) return;
        progress = e.detail;
        renderProgress();
    });

    // Optimize has filled in its answer and is starting the run: show the fields it changed; what it decided is told with
    // the run's result, so until then only "typing" dots
    document.addEventListener('runner:optimized', (e) => {
        if (!awaitingOptimize) return;
        awaitingOptimize = false;
        quietRun = true;
        progress = null;
        renderProgress();
        // A Free visitor's teaser changes only the spend
        (e.detail.kind === 'optimized' ? [el['withdrawal'], el['socialSecurityStartDate']] : [el['withdrawal']]).forEach(reveal);
        enqueue({ ...engine.optimized(e.detail), hold: true });
    });

    // The run Gordon started has finished (app.js) - or Optimize stopped before one: say what it means
    document.addEventListener('runner:result', (e) => {
        if (!awaitingRun) return;
        awaitingRun = false;
        awaitingOptimize = false;
        quietRun = false;
        progress = null;
        renderProgress();
        const facts = digest(e.detail);
        enqueue(engine.result(facts));
        // The results are on the page: once they're explained, Gordon makes room for them
        const mine = session;
        if (facts.ok) playing = playing.then(() => minimizeAfterReading(mine));
    });

    // Shown while the Observe switch is on; it's read again when the tab regains focus (paywall.js)
    function sync() {
        const on = !!Paywall.me.gordon;
        launcher.hidden = !on;
        if (!on) close();
    }

    document.body.append(launcher, panel);
    I18n.ready.then(renderStatic);
    document.addEventListener('i18n:change', renderStatic);
    Paywall.ready.then(sync);
    document.addEventListener('paywall:change', sync);

    return { open, close, page, digest };
})();
