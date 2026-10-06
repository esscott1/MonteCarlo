// The Scenario runner's balance chart, drawn as plain SVG: the runs' 90th percentile, median and 10th percentile over
// time, green shading between the upper two lines and orange between the lower two. The caller passes the points and
// every piece of text (already translated), so this file holds no wording of its own.
window.BalanceChart = (() => {
    const MARGIN = { top: 12, right: 16, bottom: 28, left: 60 };
    let last = null;

    const escape = (value) => String(value).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    const time = (iso) => { const [y, m, d] = iso.split('-').map(Number); return Date.UTC(y, m - 1, d); };

    // $500K, $2.5M, $10M
    function compactDollars(value) {
        const [divisor, suffix] = value >= 1e9 ? [1e9, 'B'] : value >= 1e6 ? [1e6, 'M'] : value >= 1e3 ? [1e3, 'K'] : [1, ''];
        return `$${Number((value / divisor).toFixed(value / divisor < 10 ? 1 : 0))}${suffix}`;
    }

    // A round step that gives about `count` ticks up to max: 1, 2, 2.5 or 5 times a power of ten
    function niceStep(max, count) {
        const rough = max / count;
        const power = 10 ** Math.floor(Math.log10(rough));
        return [1, 2, 2.5, 5, 10].map((m) => m * power).find((step) => step >= rough);
    }

    // points: [{ date: 'yyyy-mm-dd', lower, middle, upper }]
    // text: { ariaLabel, names: { upper, middle, lower }, formatDate(iso), formatMoney(value) }
    function render(container, points, text) {
        last = { container, points, text };
        const width = Math.max(260, container.clientWidth);
        const height = width < 500 ? 240 : 300;
        const plotWidth = width - MARGIN.left - MARGIN.right;
        const plotHeight = height - MARGIN.top - MARGIN.bottom;

        const t0 = time(points[0].date);
        const t1 = time(points[points.length - 1].date);
        const x = (iso) => MARGIN.left + ((time(iso) - t0) / Math.max(1, t1 - t0)) * plotWidth;
        const highest = Math.max(1, ...points.map((p) => p.upper));
        const step = niceStep(highest, 5);
        const top = Math.ceil(highest / step) * step;
        const y = (value) => MARGIN.top + plotHeight - (value / top) * plotHeight;
        const xy = (p, key) => `${x(p.date).toFixed(1)},${y(p[key]).toFixed(1)}`;

        // Y axis: gridlines and compact dollar labels from $0
        let grid = '';
        for (let value = 0; value <= top + step / 2; value += step) {
            grid += `<line class="chart-grid" x1="${MARGIN.left}" x2="${width - MARGIN.right}" y1="${y(value)}" y2="${y(value)}"/>`
                + `<text class="chart-label" x="${MARGIN.left - 6}" y="${y(value)}" text-anchor="end" dominant-baseline="middle">${compactDollars(value)}</text>`;
        }

        // X axis: a tick at every January 1, labelled every 1, 2, 5 or 10 years so the labels don't collide
        const firstYear = new Date(t0).getUTCFullYear() + (new Date(t0).getUTCMonth() === 0 && new Date(t0).getUTCDate() === 1 ? 0 : 1);
        const lastYear = new Date(t1).getUTCFullYear();
        const yearWidth = plotWidth / Math.max(1, (t1 - t0) / (365.25 * 864e5));
        const labelEvery = [1, 2, 5, 10, 20].find((n) => n * yearWidth >= 40) ?? 50;
        let ticks = '';
        for (let year = firstYear; year <= lastYear; year++) {
            const tx = x(`${year}-01-01`);
            ticks += `<line class="chart-tick" x1="${tx}" x2="${tx}" y1="${MARGIN.top + plotHeight}" y2="${MARGIN.top + plotHeight + 4}"/>`;
            if ((year - firstYear) % labelEvery === 0) {
                ticks += `<text class="chart-label" x="${tx}" y="${height - 8}" text-anchor="middle">${year}</text>`;
            }
        }

        const line = (key) => points.map((p) => xy(p, key)).join(' ');
        const band = (upperKey, lowerKey) =>
            [...points.map((p) => xy(p, upperKey)), ...[...points].reverse().map((p) => xy(p, lowerKey))].join(' ');

        container.innerHTML = `
            <svg class="balance-chart" viewBox="0 0 ${width} ${height}" width="${width}" height="${height}" role="img" aria-label="${escape(text.ariaLabel)}">
                ${grid}
                <line class="chart-axis" x1="${MARGIN.left}" x2="${width - MARGIN.right}" y1="${MARGIN.top + plotHeight}" y2="${MARGIN.top + plotHeight}"/>
                ${ticks}
                <polygon class="chart-band chart-band-upper" points="${band('upper', 'middle')}"/>
                <polygon class="chart-band chart-band-lower" points="${band('middle', 'lower')}"/>
                <polyline class="chart-line chart-line-upper" points="${line('upper')}"/>
                <polyline class="chart-line chart-line-middle" points="${line('middle')}"/>
                <polyline class="chart-line chart-line-lower" points="${line('lower')}"/>
                <g class="chart-hover" visibility="hidden">
                    <line class="chart-guide" y1="${MARGIN.top}" y2="${MARGIN.top + plotHeight}"/>
                    <circle class="chart-dot chart-line-upper" r="3.5"/>
                    <circle class="chart-dot chart-line-middle" r="3.5"/>
                    <circle class="chart-dot chart-line-lower" r="3.5"/>
                </g>
                <rect class="chart-hit" x="${MARGIN.left}" y="${MARGIN.top}" width="${plotWidth}" height="${plotHeight}"/>
            </svg>
            <div class="chart-tooltip" hidden></div>`;

        // Hover or touch: a guide at the nearest point and its three values
        const svg = container.querySelector('svg');
        const hover = svg.querySelector('.chart-hover');
        const tooltip = container.querySelector('.chart-tooltip');
        const xs = points.map((p) => x(p.date));
        const show = (event) => {
            const box = svg.getBoundingClientRect();
            const px = (event.clientX - box.left) * (width / box.width);
            let i = 0;
            xs.forEach((value, j) => { if (Math.abs(value - px) < Math.abs(xs[i] - px)) i = j; });
            const p = points[i];
            hover.setAttribute('visibility', 'visible');
            hover.querySelector('.chart-guide').setAttribute('x1', xs[i]);
            hover.querySelector('.chart-guide').setAttribute('x2', xs[i]);
            ['upper', 'middle', 'lower'].forEach((key) => {
                const dot = hover.querySelector(`.chart-dot.chart-line-${key}`);
                dot.setAttribute('cx', xs[i]);
                dot.setAttribute('cy', y(p[key]));
            });
            tooltip.innerHTML = `<strong>${escape(text.formatDate(p.date))}</strong>`
                + ['upper', 'middle', 'lower'].map((key) =>
                    `<span class="chart-tip-${key}">${escape(text.names[key])}: ${escape(text.formatMoney(p[key]))}</span>`).join('');
            tooltip.hidden = false;
            const left = (xs[i] / width) * box.width;
            tooltip.style.left = `${Math.min(Math.max(left - tooltip.offsetWidth / 2, 0), box.width - tooltip.offsetWidth)}px`;
        };
        const hide = () => { hover.setAttribute('visibility', 'hidden'); tooltip.hidden = true; };
        const hit = svg.querySelector('.chart-hit');
        hit.addEventListener('pointermove', show);
        hit.addEventListener('pointerdown', show);
        hit.addEventListener('pointerleave', hide);
    }

    // The chart's width follows its tile
    let resizeTimer = null;
    window.addEventListener('resize', () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => { if (last) render(last.container, last.points, last.text); }, 150);
    });

    return { render };
})();
