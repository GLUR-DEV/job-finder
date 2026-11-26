// Leaflet map for the Map page. The Blazor page calls these functions through JS interop.
window.jobMap = (() => {
    // Light yellow (few jobs) to deep red (most jobs).
    const RAMP = ['#ffffb2', '#fed976', '#fd8d3c', '#e31a1c', '#8c0b1a'];
    const POINT_RADIUS_M = 3000;

    let map = null;
    let dotNet = null;
    let legend = null;
    let counts = {};
    let max = 0;
    let bins = [];   // [{ lo, hi, color }] built from the current counts
    const districts = new Map();   // name -> { layer, matched }

    // Splits the districts into up to five groups holding roughly the same number of districts each (quantiles),
    // not five equal slices of 1..max. One very busy district (such as Auckland Central) then can't turn
    // every other district the lightest colour.
    function buildBins() {
        bins = [];
        const vals = [];
        for (const name of districts.keys()) {
            const c = counts[name] || 0;
            if (c > 0) vals.push(c);
        }
        if (vals.length === 0) return;
        vals.sort((a, b) => a - b);

        const steps = Math.min(RAMP.length, new Set(vals).size);
        const uppers = [];
        for (let i = 1; i <= steps; i++) {
            const upper = vals[Math.ceil(vals.length * i / steps) - 1];
            if (uppers.length === 0 || upper > uppers[uppers.length - 1]) uppers.push(upper);
        }

        let lo = 1;
        uppers.forEach((hi, i) => {
            // With fewer than five groups, spread the colours across the whole yellow-to-red range.
            const idx = uppers.length === 1 ? RAMP.length - 1 : Math.round(i * (RAMP.length - 1) / (uppers.length - 1));
            bins.push({ lo, hi, color: RAMP[idx] });
            lo = hi + 1;
        });
    }

    function colorFor(count) {
        const bin = bins.find(b => count <= b.hi) || bins[bins.length - 1];
        return bin.color;
    }

    function styleFor(name) {
        const count = counts[name] || 0;
        if (count === 0) {
            return { color: '#7b8a93', weight: 1, opacity: 0.5, fillColor: '#9aa7ae', fillOpacity: 0.12 };
        }
        return { color: '#5c1a10', weight: 1.5, opacity: 0.9, fillColor: colorFor(count), fillOpacity: 0.65 };
    }

    function tooltipFor(name) {
        const count = counts[name] || 0;
        const box = document.createElement('div');
        const title = document.createElement('strong');
        title.textContent = name;
        box.append(title, document.createElement('br'),
                   document.createTextNode(count + (count === 1 ? ' job' : ' jobs')));

        const matched = districts.get(name)?.matched;
        if (matched) {
            const small = document.createElement('div');
            small.className = 'map-matched';
            small.textContent = 'Matched: ' + matched;
            box.append(small);
        }
        return box;
    }

    function restyle() {
        max = 0;
        for (const name of districts.keys()) max = Math.max(max, counts[name] || 0);
        buildBins();
        for (const [name, d] of districts) d.layer.setStyle(styleFor(name));
        renderLegend();
    }

    function renderLegend() {
        if (!legend) return;
        const el = legend.getContainer();
        el.replaceChildren();

        const title = document.createElement('strong');
        title.textContent = 'Jobs per district';
        el.append(title);

        if (max === 0) {
            const none = document.createElement('div');
            none.textContent = 'No jobs match';
            el.append(none);
            return;
        }

        for (const b of bins) {
            const row = document.createElement('div');
            row.className = 'map-legend-row';
            const swatch = document.createElement('i');
            swatch.style.background = b.color;
            row.append(swatch, document.createTextNode(b.lo === b.hi ? String(b.lo) : b.lo + '\u2013' + b.hi));
            el.append(row);
        }
    }

    function area(layer) {
        const b = layer.getBounds();
        return (b.getNorth() - b.getSouth()) * (b.getEast() - b.getWest());
    }

    // Biggest shapes at the back, so a suburb is always clickable on top of a city-wide outline.
    function stack() {
        [...districts.values()]
            .sort((a, b) => area(b.layer) - area(a.layer))
            .forEach(d => d.layer.bringToFront());
    }

    function build(item) {
        let layer;
        if (item.status === 'outline' && item.geoJson) {
            layer = L.geoJSON(JSON.parse(item.geoJson), { style: () => styleFor(item.name) });
        } else if (item.lat != null && item.lon != null) {
            layer = L.circle([item.lat, item.lon], { radius: POINT_RADIUS_M, ...styleFor(item.name) });
        } else {
            return null;
        }

        layer.bindTooltip(() => tooltipFor(item.name), { sticky: true });
        layer.on('click', () => {
            // Nothing to show on the jobs page when the district has no matching jobs.
            if ((counts[item.name] || 0) > 0) dotNet.invokeMethodAsync('DistrictClicked', item.name);
        });
        return layer;
    }

    return {
        init(element, ref) {
            this.dispose();
            dotNet = ref;
            map = L.map(element).setView([-36.85, 174.76], 10);
            L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            }).addTo(map);

            legend = L.control({ position: 'bottomright' });
            legend.onAdd = () => L.DomUtil.create('div', 'map-legend');
            legend.addTo(map);
            renderLegend();
        },

        // Adds or replaces districts. Each item: { name, status, geoJson, lat, lon, matched }.
        upsert(items) {
            for (const item of items) {
                const old = districts.get(item.name);
                if (old) old.layer.remove();

                const layer = build(item);
                if (!layer) { districts.delete(item.name); continue; }

                layer.addTo(map);
                districts.set(item.name, { layer, matched: item.matched });
            }
            restyle();
            stack();
        },

        // counts: { "Penrose, Auckland": 12, ... }
        setCounts(newCounts) {
            counts = newCounts || {};
            restyle();
        },

        fit() {
            if (districts.size === 0) return;
            const bounds = L.featureGroup([...districts.values()].map(d => d.layer)).getBounds();
            if (bounds.isValid()) map.fitBounds(bounds.pad(0.05));
        },

        // Removes every district and count, for switching between the SEEK and Trade Me maps.
        clear() {
            for (const d of districts.values()) d.layer.remove();
            districts.clear();
            counts = {};
            max = 0;
            bins = [];
            renderLegend();
        },

        dispose() {
            if (map) { map.remove(); map = null; }
            districts.clear();
            counts = {};
            max = 0;
            bins = [];
            legend = null;
            dotNet = null;
        }
    };
})();
