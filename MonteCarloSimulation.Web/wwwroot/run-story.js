// A finished Scenario runner run in plain words: what the results summary says under its first two lines, and what
// Gordon says about a run, from one place so the two always read the same. No DOM, no text of its own: facts(view)
// reads app.js's view, and survival/markets/tip give messages ({ key, values }, values already formatted) for t().
window.RunStory = (() => {
    const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const money = (value) => usd.format(value);
    const say = (key, values) => ({ key, values });

    // A failed year's shortfall (a year table's last row, and the markets below): what the year needed after tax
    // (spending plus any Medicare surcharge), what Social Security and the annuity still paid after their taxes, and how
    // far short of the need that leaves it
    function yearShortfall(yd) {
        const need = yd.withdrawal + (yd.irmaaSurcharge ?? 0);
        const ss = Math.max(0, yd.socialSecurityIncome - yd.socialSecurityTax);
        const annuity = Math.max(0, yd.annuityIncome - yd.annuityTax);
        const short = Math.max(0, need - ss - annuity);
        return { need, ss, annuity, net: ss + annuity, short, share: need > 0 ? short / need : 0 };
    }

    // A finished run (app.js's view) as facts; { ok: false, message } when it didn't run
    function facts(view) {
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
        // short, then by what they ended with. One that fell short says when and by how much in that year (as its year
        // table says); the money doesn't run dry, since Social Security and any annuity pay on
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

    // How many markets lasted; counts: whether it says how many were run
    function survival(f, { counts }) {
        const percent = Math.round(f.rate * 100);
        const all = f.survived === f.total;
        return counts
            ? (all ? say('story.allSurvived', { total: f.total, years: f.years })
                : say('story.survived', { survived: f.survived, total: f.total, years: f.years, rate: percent }))
            : (all ? say('story.allSurvivedRate', { years: f.years })
                : say('story.survivedRate', { years: f.years, rate: percent }));
    }

    // A typical market, then the weakest 10%: what's left at the end, or from when and by how much the money falls short
    // of the plan (neither runs dry: Social Security and any annuity still pay)
    function markets(f) {
        const about = (value) => money(Math.round(value / 10000) * 10000);
        const aboutYear = (value) => money(Math.round(value / 1000) * 1000);
        return [
            f.medianShortYear
                ? say('story.medianShort', {
                    year: f.medianShortYear, age: f.medianShortAge,
                    amount: aboutYear(f.medianShort), percent: Math.round(f.medianShortShare * 100),
                })
                : say('story.median', { amount: about(f.median), year: f.endYear, age: f.endAge }),
            f.lowerShortYear
                ? say('story.lowerShort', {
                    year: f.lowerShortYear, age: f.lowerShortAge,
                    amount: aboutYear(f.lowerShort), percent: Math.round(f.lowerShortShare * 100),
                })
                : say('story.lowerHolds', { amount: about(f.lower), year: f.endYear }),
        ];
    }

    // Against the 80-85% range the app aims for. It follows the percent as shown, so 79.8% reads "80%" and is in range.
    function tip(f) {
        const percent = Math.round(f.rate * 100);
        const key = percent < 80 ? 'story.tip.lower' : percent < 90 ? 'story.tip.inRange' : 'story.tip.more';
        return say(key, { amount: money(f.monthly) });
    }

    return { yearShortfall, facts, survival, markets, tip };
})();
