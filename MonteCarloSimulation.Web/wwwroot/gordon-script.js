// Gordon's guided setup: a fixed conversation that asks a few questions about a household's finances and fills in the
// Scenario runner's inputs. It stands in for the AI version (docs/plan-gordon-chat-bot.md) and is shaped like it: the
// engine only decides what to say and which page actions to take ({ name, value }, gordon.js's page.apply), so a model
// calling the same actions as tools can replace it behind the same panel.
//
// An engine answers each turn with { say: [message], lead, actions: [{ name, value }], choices: [{ key, value }], run }:
// the actions are taken after the first lead messages (the reply to the visitor's answer), before the rest.
// A message is { key, values } (text from t(), values already formatted and escaped) or { kind: 'summary', lines }.
window.GordonScript = (() => {
    const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const money = (value) => usd.format(value);
    const say = (key, values) => ({ key, values });
    const choice = (key, value) => ({ key, value });

    // --- Reading answers ---

    const MONTHS = [
        ['january', 'jan', 'enero', 'ene'], ['february', 'feb', 'febrero'], ['march', 'mar', 'marzo'],
        ['april', 'apr', 'abril', 'abr'], ['may', 'mayo'], ['june', 'jun', 'junio'], ['july', 'jul', 'julio'],
        ['august', 'aug', 'agosto', 'ago'], ['september', 'sep', 'sept', 'septiembre', 'setiembre'],
        ['october', 'oct', 'octubre'], ['november', 'nov', 'noviembre'], ['december', 'dec', 'diciembre', 'dic'],
    ];

    const words = (text) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
    const has = (text, pattern) => pattern.test(words(text));

    // "400k", "$1.2M", "1,200,000", "1.2 million", "half a million", "none": whole dollars, or null when there's no amount
    function parseMoney(text) {
        const s = words(text).replace(/[$,]/g, '').trim();
        if (/^(none|nothing|zero|no|nope|nada|ninguno|ninguna|n\/?a)\b/.test(s) || /\b(don'?t|do not) have\b|\bno tengo\b/.test(s)) return 0;
        if (/half a million|medio millon/.test(s)) return 500000;
        const m = s.match(/(\d+(?:\.\d+)?)\s*(k|thousand|mil|grand|m|mm|million|millones|millon)?\b/);
        if (!m) return null;
        const scale = { k: 1e3, thousand: 1e3, mil: 1e3, grand: 1e3, m: 1e6, mm: 1e6, million: 1e6, millones: 1e6, millon: 1e6 }[m[2]] ?? 1;
        return Math.round(Number(m[1]) * scale);
    }

    // A date in what was typed: { year, month, day }, month and day null when not given; null without a year
    function parseDate(text) {
        const s = words(text);
        let m = s.match(/\b(\d{4})-(\d{1,2})-(\d{1,2})\b/);
        if (m) return { year: +m[1], month: +m[2], day: +m[3] };
        m = s.match(/\b(\d{1,2})\/(\d{1,2})\/(\d{4})\b/);
        if (m) return I18n.language === 'es' ? { year: +m[3], month: +m[2], day: +m[1] } : { year: +m[3], month: +m[1], day: +m[2] };
        m = s.match(/\b(\d{1,2})\/(\d{4})\b/);
        if (m) return { year: +m[2], month: +m[1], day: null };
        const year = s.match(/\b(19|20)\d{2}\b/);
        if (!year) return null;
        const month = MONTHS.findIndex((names) => names.some((name) => new RegExp(`\\b${name}\\b`).test(s)));
        const rest = s.replace(year[0], ' ');
        const day = month >= 0 ? rest.match(/\b(\d{1,2})(st|nd|rd|th)?\b/) : null;
        return { year: +year[0], month: month >= 0 ? month + 1 : null, day: day ? +day[1] : null };
    }

    // An age in what was typed ("at 62", "when I'm 65", "a los 67", or just "65"), within a range
    function parseAge(text, low, high) {
        const m = words(text).match(/\b(\d{2,3})\b/);
        const age = m ? Number(m[1]) : NaN;
        return age >= low && age <= high ? age : null;
    }

    const pad = (n) => String(n).padStart(2, '0');
    const iso = (year, month, day) => {
        const date = new Date(Date.UTC(year, month - 1, day));
        // Feb 30 and the like roll back to the month's last day
        if (date.getUTCMonth() !== month - 1) date.setUTCDate(0);
        return `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`;
    };
    const today = () => {
        const now = new Date();
        return iso(now.getFullYear(), now.getMonth() + 1, now.getDate());
    };
    const addYears = (date, years) => {
        const [y, m, d] = date.split('-').map(Number);
        return iso(y + years, m, d);
    };
    const ageOn = (birthdate, date) => (Date.parse(date) - Date.parse(birthdate)) / (365.25 * 864e5);

    // --- The conversation ---

    const MIXES = {
        conservative: { stocks: 40, bonds: 50, cash: 10 },
        balanced: { stocks: 60, bonds: 30, cash: 10 },
        growth: { stocks: 80, bonds: 15, cash: 5 },
    };
    const MIX_NAMES = { conservative: 'gordon.mix.conservative', balanced: 'gordon.mix.balanced', growth: 'gordon.mix.growth' };
    const PLAN_TO_AGE = 95;

    // "Show me an example": a household whose answers type themselves in, so a demo never depends on typing
    const EXAMPLE = {
        birth: { text: 'June 1967' },
        retire: { text: 'January 2028' },
        filing: { choice: 'married' },
        taxDeferred: { text: '$900k' },
        roth: { text: '150k' },
        brokerage: { text: '250,000' },
        inheritance: { choice: 'none' },
        ssAmount: { text: '$4,800' },
        ssStart: { choice: 67 },
        mix: { choice: 'balanced' },
        spending: { choice: 'suggested' },
        confirm: { choice: 'run' },
    };

    function create(page) {
        let step = 'intro';
        let backTo = null;       // a step changed from the summary goes back to it
        let history = [];
        let example = false;
        const answers = {};

        const ORDER = ['birth', 'retire', 'filing', 'taxDeferred', 'roth', 'brokerage', 'inheritance', 'ssAmount', 'ssStart', 'mix', 'spending', 'confirm'];
        // Inheritance is a Plus input: a Free visitor's stays at $0, so it isn't asked
        const asks = (name) => name !== 'inheritance' || page.can('inheritance');
        const after = (name) => ORDER.slice(ORDER.indexOf(name) + 1).find(asks) ?? 'confirm';

        // Each step: what Gordon asks (ask), the buttons under it (choices), and how an answer is read (answer). An answer
        // returns { say, actions, next } when it's understood (next defaults to the following step) or { retry } when not.
        const STEPS = {
            intro: {
                ask: () => [say('gordon.intro.hello'), say('gordon.intro.how')],
                choices: () => [choice('gordon.choice.start', 'start'), choice('gordon.choice.example', 'example')],
                answer(input) {
                    if (input.choice === 'example' || has(input.text ?? '', /example|ejemplo|demo|show/)) example = true;
                    return { say: example ? [say('gordon.intro.example')] : [], next: 'birth' };
                },
            },
            birth: {
                ask: () => [say('gordon.ask.birth')],
                answer(input) {
                    const thisYear = new Date().getFullYear();
                    const date = parseDate(input.text ?? '');
                    if (date && date.year >= thisYear - 110 && date.year <= thisYear - 18) {
                        const birthdate = iso(date.year, date.month ?? 7, date.day ?? 1);
                        answers.birthdate = birthdate;
                        return {
                            say: [say(date.month ? 'gordon.ack.birth' : 'gordon.ack.birthYearOnly', { date: page.monthYear(birthdate) })],
                            actions: [{ name: 'setBirthdate', value: birthdate }],
                        };
                    }
                    const age = parseAge(input.text ?? '', 18, 110);
                    if (age) {
                        const now = new Date();
                        const birthdate = iso(now.getFullYear() - age, now.getMonth() + 1, 1);
                        answers.birthdate = birthdate;
                        return { say: [say('gordon.ack.birthFromAge', { age, date: page.monthYear(birthdate) })], actions: [{ name: 'setBirthdate', value: birthdate }] };
                    }
                    return { retry: 'gordon.retry.birth' };
                },
            },
            retire: {
                ask: () => [say('gordon.ask.retire')],
                choices: () => [choice('gordon.choice.retireNow', 'now'), choice('gordon.choice.at62', 62), choice('gordon.choice.at65', 65), choice('gordon.choice.at67', 67)],
                answer(input) {
                    const birthdate = answers.birthdate ?? page.get().birthdate;
                    const text = input.text ?? '';
                    let date = null;
                    if (input.choice === 'now' || has(text, /\b(now|today|right away|already|ahora|hoy|ya)\b/)) date = today();
                    else if (typeof input.choice === 'number') date = addYears(birthdate, input.choice);
                    else if (has(text, /next year|proximo ano|ano que viene/)) date = iso(new Date().getFullYear() + 1, 1, 1);
                    else {
                        const inYears = words(text).match(/\b(?:in|en)\s+(\d{1,2})\s+(?:years?|anos?)\b|\b(\d{1,2})\s+(?:years?|anos?)\s+from now\b/);
                        const parsed = parseDate(text);
                        const age = parseAge(text, 40, 85);
                        if (inYears) date = addYears(today(), Number(inYears[1] ?? inYears[2]));
                        else if (parsed) date = iso(parsed.year, parsed.month ?? 1, parsed.day ?? 1);
                        else if (age) date = addYears(birthdate, age);
                    }
                    // The model starts in 2026 and runs to age 100 at most
                    if (!date || date < '2026-01-01' || date <= birthdate || ageOn(birthdate, date) >= 99) return { retry: 'gordon.retry.retire' };
                    if (date < today()) date = today();
                    answers.retirementDate = date;
                    const age = Math.floor(ageOn(birthdate, date));
                    const years = Math.max(1, Math.round(PLAN_TO_AGE - ageOn(birthdate, date)));
                    return {
                        say: [say('gordon.ack.retire', { date: page.monthYear(date), age }), say('gordon.ack.horizon', { years, age: PLAN_TO_AGE })],
                        // The most runs the page allows, for the steadiest answer
                        actions: [{ name: 'setRetirementDate', value: date }, { name: 'setHorizon', value: years }, { name: 'setRuns', value: 99 }],
                    };
                },
            },
            filing: {
                ask: () => [say('gordon.ask.filing')],
                choices: () => [choice('gordon.choice.single', 'single'), choice('gordon.choice.married', 'married')],
                answer(input) {
                    const text = input.text ?? '';
                    const status = input.choice
                        ?? (has(text, /married|joint|spouse|wife|husband|partner|we\b|casad|espos|pareja/) ? 'married'
                            : has(text, /single|just me|alone|widow|divorc|solter|viud|solo|sola/) ? 'single' : null);
                    if (!status) return { retry: 'gordon.retry.filing' };
                    answers.filingStatus = status;
                    return {
                        say: [say(status === 'married' ? 'gordon.ack.married' : 'gordon.ack.single')],
                        actions: [{ name: 'setFilingStatus', value: status }],
                    };
                },
            },
            taxDeferred: moneyStep('taxDeferred', 'setTaxDeferred', 'gordon.ask.taxDeferred', 'gordon.ack.taxDeferred'),
            roth: moneyStep('roth', 'setRoth', 'gordon.ask.roth', 'gordon.ack.roth'),
            brokerage: moneyStep('brokerage', 'setBrokerage', 'gordon.ask.brokerage', 'gordon.ack.brokerage'),
            inheritance: {
                ask: () => [say('gordon.ask.inheritance')],
                choices: () => [choice('gordon.choice.none', 'none')],
                answer(input) {
                    const amount = input.choice === 'none' ? 0 : parseMoney(input.text ?? '');
                    if (amount === null) return { retry: 'gordon.retry.money' };
                    answers.inheritance = amount;
                    if (amount === 0) return { say: [say('gordon.ack.noInheritance')], actions: [{ name: 'setInheritance', value: { amount: 0, years: 0 } }] };
                    return { say: [say('gordon.ack.inheritance', { amount: money(amount) })], next: 'inheritanceWhen' };
                },
            },
            inheritanceWhen: {
                ask: () => [say('gordon.ask.inheritanceWhen')],
                answer(input) {
                    const m = words(input.text ?? '').match(/\b(\d{1,2})\b/);
                    if (!m) return { retry: 'gordon.retry.years' };
                    const years = Number(m[1]);
                    answers.inheritanceYears = years;
                    return {
                        say: [say('gordon.ack.inheritanceWhen', { years })],
                        actions: [{ name: 'setInheritance', value: { amount: answers.inheritance, years } }],
                        next: backTo ?? after('inheritance'),
                    };
                },
            },
            ssAmount: {
                ask: () => [say(page.get().filingStatus === 'married' ? 'gordon.ask.ssMarried' : 'gordon.ask.ss')],
                choices: () => [choice('gordon.choice.dontKnow', 'unknown'), choice('gordon.choice.noSs', 'none')],
                // The start date is asked next whenever there's a benefit, even when this was changed from the summary
                answer(input) {
                    if (input.choice === 'unknown' || has(input.text ?? '', /don'?t know|not sure|no idea|no se|ni idea/)) {
                        answers.ssMonthly = page.get().socialSecurityMonthly;
                        return { say: [say('gordon.ack.ssDefault', { amount: money(answers.ssMonthly) })], next: 'ssStart' };
                    }
                    let amount = input.choice === 'none' ? 0 : parseMoney(input.text ?? '');
                    if (amount === null) return { retry: 'gordon.retry.money' };
                    // A yearly figure, most likely
                    const yearly = amount > 12000;
                    if (yearly) amount = Math.round(amount / 12);
                    answers.ssMonthly = amount;
                    return {
                        say: [say(yearly ? 'gordon.ack.ssYearly' : 'gordon.ack.ss', { amount: money(amount) })],
                        actions: [{ name: 'setSocialSecurityAmount', value: amount }],
                        next: amount === 0 ? (backTo ?? after('ssStart')) : 'ssStart',
                    };
                },
            },
            ssStart: {
                ask: () => [say('gordon.ask.ssStart')],
                choices: () => [choice('gordon.choice.ss62', 62), choice('gordon.choice.ss67', 67), choice('gordon.choice.ss70', 70), choice('gordon.choice.notSure', 'unknown')],
                answer(input) {
                    const birthdate = answers.birthdate ?? page.get().birthdate;
                    const text = input.text ?? '';
                    let age = typeof input.choice === 'number' ? input.choice
                        : input.choice === 'unknown' || has(text, /not sure|don'?t know|full retirement|fra|no se|edad plena/) ? 67
                            : parseAge(text, 62, 70);
                    let date = age ? addYears(birthdate, age) : null;
                    const parsed = !date ? parseDate(text) : null;
                    if (parsed) {
                        const candidate = iso(parsed.year, parsed.month ?? 1, parsed.day ?? 1);
                        const at = ageOn(birthdate, candidate);
                        if (at >= 62 && at <= 70) { date = candidate; age = Math.floor(at); }
                    }
                    if (!date) return { retry: 'gordon.retry.ssStart' };
                    answers.ssStart = date;
                    return {
                        say: [say(input.choice === 'unknown' ? 'gordon.ack.ssStartDefault' : 'gordon.ack.ssStart', { age, date: page.monthYear(date) })],
                        actions: [{ name: 'setSocialSecurityStart', value: date }],
                    };
                },
            },
            mix: {
                ask: () => [say('gordon.ask.mix')],
                choices: () => [choice('gordon.mix.conservative', 'conservative'), choice('gordon.mix.balanced', 'balanced'), choice('gordon.mix.growth', 'growth')],
                answer(input) {
                    const text = input.text ?? '';
                    const id = input.choice
                        ?? (has(text, /conserv|cautious|safe|careful|prudent/) ? 'conservative'
                            : has(text, /balanc|moderate|middle|equilibr|moderad/) ? 'balanced'
                                : has(text, /growth|aggress|stock|crecim|agresiv|accion/) ? 'growth' : null);
                    if (!id) return { retry: 'gordon.retry.mix' };
                    answers.mix = id;
                    const mix = MIXES[id];
                    return {
                        say: [say('gordon.ack.mix', { name: t(MIX_NAMES[id]), stocks: mix.stocks, bonds: mix.bonds, cash: mix.cash })],
                        actions: [{ name: 'setMix', value: mix }],
                    };
                },
            },
            spending: {
                ask: () => [say('gordon.ask.spending'), say('gordon.ask.spendingHint', { amount: money(suggestedSpending()) })],
                choices: () => [{ key: 'gordon.choice.useSuggested', value: 'suggested', values: { amount: money(suggestedSpending()) } }],
                answer(input) {
                    let amount = input.choice === 'suggested' ? suggestedSpending() : parseMoney(input.text ?? '');
                    if (!amount) return { retry: 'gordon.retry.spending' };
                    const yearly = amount > 50000;
                    if (yearly) amount = Math.round(amount / 12);
                    answers.spending = amount;
                    return {
                        say: [say(yearly ? 'gordon.ack.spendingYearly' : 'gordon.ack.spending', { amount: money(amount) })],
                        actions: [{ name: 'setSpending', value: amount }],
                    };
                },
            },
            confirm: {
                // The summary's lines are read when it's shown, once the last answer is on the form
                ask: () => [say('gordon.confirm.lead'), { kind: 'summary', lines: summaryLines }, say('gordon.confirm.ask')],
                choices: () => [choice('gordon.choice.run', 'run'), choice('gordon.choice.change', 'change')],
                answer(input) {
                    const text = input.text ?? '';
                    if (input.choice === 'change' || has(text, /change|edit|fix|cambi|correg/)) return { next: 'change' };
                    if (input.choice === 'run' || has(text, /\b(run|go|yes|yep|sure|ok|okay|si|dale|adelante|corre)\b/)) {
                        return { say: [say('gordon.running', { runs: page.get().iterations })], run: true, next: 'running' };
                    }
                    return { retry: 'gordon.retry.confirm' };
                },
            },
            change: {
                ask: () => [say('gordon.ask.change')],
                choices: () => [
                    choice('gordon.topic.birth', 'birth'), choice('gordon.topic.retire', 'retire'), choice('gordon.topic.filing', 'filing'),
                    choice('gordon.topic.taxDeferred', 'taxDeferred'), choice('gordon.topic.roth', 'roth'), choice('gordon.topic.brokerage', 'brokerage'),
                    ...(asks('inheritance') ? [choice('gordon.topic.inheritance', 'inheritance')] : []),
                    choice('gordon.topic.ss', 'ssAmount'), choice('gordon.topic.mix', 'mix'), choice('gordon.topic.spending', 'spending'),
                ],
                answer(input) {
                    if (!STEPS[input.choice]) return { retry: 'gordon.retry.change' };
                    backTo = 'confirm';
                    return { next: input.choice };
                },
            },
            running: {
                ask: () => [],
                answer: () => ({ retry: 'gordon.retry.running' }),
            },
            results: {
                ask: () => [],
                choices: () => [choice('gordon.choice.details', 'details'), choice('gordon.choice.changeAndRerun', 'change'), choice('gordon.choice.startOver', 'restart')],
                answer(input) {
                    if (input.choice === 'details') return { actions: [{ name: 'showResults' }], next: 'results', quiet: true };
                    if (input.choice === 'restart') return { restart: true };
                    return { next: 'change' };
                },
            },
        };

        function moneyStep(name, action, askKey, ackKey) {
            return {
                ask: () => [say(askKey)],
                choices: () => [choice('gordon.choice.none', 'none')],
                answer(input) {
                    const amount = input.choice === 'none' ? 0 : parseMoney(input.text ?? '');
                    if (amount === null) return { retry: 'gordon.retry.money' };
                    answers[name] = amount;
                    return { say: [say(ackKey, { amount: money(amount) })], actions: [{ name: action, value: amount }] };
                },
            };
        }

        // A starting point, not advice: 4% of savings a year plus Social Security averaged over the years it's paid, less
        // about 15% for taxes, rounded to $100 a month. (A household of $1.3M, $4,800 from 67 and retiring at 60 gets
        // about $7,000, which survived about 88% of markets in October 2026.)
        function suggestedSpending() {
            const now = page.get();
            const savings = now.taxDeferred + now.roth + now.brokerage;
            const beforeBenefit = Math.max(0, ageOn(now.birthdate, now.socialSecurityStart) - ageOn(now.birthdate, now.retirementDate));
            const benefitShare = now.years > 0 ? Math.max(0, now.years - beforeBenefit) / now.years : 1;
            const monthly = 0.85 * (savings * 0.04 / 12 + now.socialSecurityMonthly * benefitShare);
            return Math.max(100, Math.round(monthly / 100) * 100);
        }

        function summaryLines() {
            const now = page.get();
            const lines = [
                say('gordon.summary.born', { date: page.monthYear(now.birthdate) }),
                say('gordon.summary.retire', { date: page.monthYear(now.retirementDate), age: Math.floor(ageOn(now.birthdate, now.retirementDate)), years: now.years }),
                say(now.filingStatus === 'married' ? 'gordon.summary.married' : 'gordon.summary.single'),
                say('gordon.summary.savings', {
                    taxDeferred: money(now.taxDeferred), roth: money(now.roth), brokerage: money(now.brokerage),
                    total: money(now.taxDeferred + now.roth + now.brokerage),
                }),
            ];
            if (asks('inheritance')) {
                lines.push(now.inheritance > 0
                    ? say('gordon.summary.inheritance', { amount: money(now.inheritance), years: now.inheritanceYears })
                    : say('gordon.summary.noInheritance'));
            }
            lines.push(now.socialSecurityMonthly > 0
                ? say('gordon.summary.ss', { amount: money(now.socialSecurityMonthly), date: page.monthYear(now.socialSecurityStart), age: Math.floor(ageOn(now.birthdate, now.socialSecurityStart) + 0.01) })
                : say('gordon.summary.noSs'));
            lines.push(say('gordon.summary.mix', { stocks: now.mix.stocks, bonds: now.mix.bonds, cash: now.mix.cash }));
            lines.push(say('gordon.summary.spending', { amount: money(now.spending) }));
            return lines;
        }

        // lead: how many of the messages answer the last question; the page actions come right after them, before the next
        function turn(name, lead = []) {
            step = name;
            const s = STEPS[name];
            return { say: [...lead, ...s.ask()], lead: lead.length, choices: s.choices ? s.choices() : [], actions: [] };
        }

        function start() {
            step = 'intro';
            backTo = null;
            history = [];
            example = false;
            Object.keys(answers).forEach((key) => delete answers[key]);
            return turn('intro');
        }

        function reply(input) {
            const text = input.text ?? '';
            // Words that work at any step
            if (!input.choice && has(text, /^(start over|restart|reset|empezar de nuevo|reiniciar)\b/)) return start();
            if (!input.choice && has(text, /^(back|go back|undo|atras|volver)\b/) && history.length) {
                return turn(history.pop(), [say('gordon.back')]);
            }
            if (!input.choice && has(text, /^(skip|pass|saltar|omitir)\b/) && !['intro', 'confirm', 'change', 'running', 'results'].includes(step)) {
                const next = backTo ?? after(step);
                if (next === 'confirm') backTo = null;
                return turn(next, [say('gordon.skipped')]);
            }

            const result = STEPS[step].answer(input);
            if (result.restart) return start();
            if (result.retry) return { say: [say(result.retry)], choices: STEPS[step].choices ? STEPS[step].choices() : [], actions: [] };

            if (!['change', 'running', 'results'].includes(step)) history.push(step);
            let next = result.next ?? (backTo ?? after(step));
            // A step changed from the summary goes straight back to it; inheritance's follow-up question still comes first
            if (next === 'confirm') backTo = null;
            if (result.quiet) {
                step = next;
                return { say: [], actions: result.actions ?? [], choices: STEPS[next].choices ? STEPS[next].choices() : [] };
            }
            return { ...turn(next, result.say ?? []), actions: result.actions ?? [], run: !!result.run };
        }

        // What a finished run means, in a few sentences (digest: gordon.js's reading of the results)
        function result(digest) {
            step = 'results';
            const lines = [];
            if (!digest.ok) {
                lines.push(digest.message ? say('gordon.result.problem', { message: digest.message }) : say('gordon.result.failed'));
            } else {
                // The tip follows the percent as shown, so 79.8% reads "80%" and counts as in the range
                const percent = Math.round(digest.rate * 100);
                const about = (value) => money(Math.round(value / 10000) * 10000);
                lines.push(digest.survived === digest.total
                    ? say('gordon.result.allSurvived', { total: digest.total, years: digest.years })
                    : say('gordon.result.survived', { survived: digest.survived, total: digest.total, years: digest.years, rate: percent }));
                lines.push(digest.medianOutYear
                    ? say('gordon.result.medianRunsOut', { year: digest.medianOutYear, age: digest.medianOutAge })
                    : say('gordon.result.median', { amount: about(digest.median), year: digest.endYear, age: digest.endAge }));
                lines.push(digest.lowerOutYear
                    ? say('gordon.result.lowerRunsOut', { year: digest.lowerOutYear, age: digest.lowerOutAge })
                    : say('gordon.result.lowerHolds', { amount: about(digest.lower), year: digest.endYear }));
                lines.push(say(percent < 80 ? 'gordon.tip.lower' : percent < 90 ? 'gordon.tip.inRange' : 'gordon.tip.more', { amount: money(digest.monthly) }));
            }
            return { say: lines, choices: STEPS.results.choices(), actions: [] };
        }

        // The example household's answer at this step, while "Show me an example" plays
        function exampleAnswer() {
            if (!example) return null;
            const answer = EXAMPLE[step];
            if (!answer) return null;
            if (step === 'confirm') example = false;
            return answer;
        }

        return { start, reply, result, exampleAnswer, get step() { return step; } };
    }

    return { create, parseMoney, parseDate, parseAge };
})();
