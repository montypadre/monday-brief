"use strict";

const RANGE = "30d";

const money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const plain = new Intl.NumberFormat("en-US");
const longDate = new Intl.DateTimeFormat("en-US", { month: "long", day: "numeric", year: "numeric", timeZone: "UTC" });
const shortDate = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" });

let chart = null;

async function getJson(url, options) {
    const response = await fetch(url, options);
    if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.error || body.detail || `Request failed (${response.status})`);
    }
    return response.json();
}

function formatValue(value, format) {
    if (format === "currency") return money.format(value);
    if (format === "percent") return `${value}%`;
    return plain.format(value);
}

function formatDate(iso, formatter) {
    return formatter.format(new Date(`${iso}T00:00:00Z`));
}

function deltaText(card) {
    if (card.changePct === null || card.changePct === undefined) return { text: "no earlier period to compare", cls: "flat" };

    const direction = card.changePct > 0 ? "up" : card.changePct < 0 ? "down" : "flat";
    const symbol = direction === "up" ? "▲" : direction === "down" ? "▼" : "—";
    const size = `${Math.abs(card.changePct)}%`;
    const points = card.changePoints !== null && card.changePoints !== undefined
        ? ` (${card.changePoints > 0 ? "+" : ""}${card.changePoints} points)`
        : "";

    return { text: `${symbol} ${direction} ${size}${points} on the period before`, cls: direction };
}

const metricNames = {
    revenue: "revenue",
    orders: "orders",
    aov: "average order value",
    sessions: "website sessions",
    conversion: "online conversion",
};

const channelNames = { online: "online", instore: "in-store" };

function capitalize(text) {
    return text.charAt(0).toUpperCase() + text.slice(1);
}

function dateSpan(start, end) {
    return start && end ? `${formatDate(start, longDate)} to ${formatDate(end, longDate)}` : "";
}

/** Turns "metric=revenue, start=..." into plain words an owner can read. */
function describeSource(source) {
    const args = Object.fromEntries(
        source.arguments.split(", ").filter(Boolean).map(pair => pair.split("="))
    );

    const metric = metricNames[args.metric] ?? args.metric ?? "";
    const channel = channelNames[args.channel] ? `${channelNames[args.channel]} ` : "";

    let text;
    switch(source.tool) {
        case "get_metric":
            text = `${capitalize(channel + metric)}, ${dateSpan(args.start, args.end)}`;
            break;
        case "compare_periods":
            text = `${capitalize(channel + metric)}, ${dateSpan(period_a_end)} compared with ${dateSpan(args.period_b_start, args.period_b_end)}`;
            break;
        case "top_products":
            text = `${args.direction === "declining" ? "Products falling fastest" : "Best sellers"}, ${dateSpan(args.start, args.end)}`;
            break;
        case "list_alerts":
            text = args.start ? `Alerts, ${dateSpan(args.start, args.end)}` : "Current alerts";
            break;
        default:
            text = source.tool;
    }

    return source.ok ? text : `${text} (no data available)`;
}

async function loadBrief() {
    const body = document.getElementById("brief-body");
    const meta = document.getElementById("brief-meta");

    try {
        const brief = await getJson("/api/brief/latest");
        body.innerHTML = "";
        brief.text.split("\n").filter(line => line.trim()).forEach(line => {
            const paragraph = document.createElement("p");
            paragraph.textContent = line;
            body.appendChild(paragraph);
        });
        meta.textContent = `Week of ${formatDate(brief.weekStart, longDate)} · written by ${brief.model}`;
    } catch (error) {
        body.innerHTML = `<p class="error">No brief yet. ${error.message}</p>`;
    }
}

async function loadKpis() {
    const list = document.getElementById("cards");

    try {
        const kpis = await getJson(`/api/kpis?range=${RANGE}`);

        document.getElementById("kpi-range").textContent =
            `${formatDate(kpis.range.start, longDate)} to ${formatDate(kpis.range.end, longDate)}, against the ${kpis.range.days} days before.`;

        list.innerHTML = "";
        kpis.cards.forEach(card => {
            const delta = deltaText(card);
            const item = document.createElement("li");
            item.className = "card";
            item.innerHTML =
                `<span class="card-label">${card.label}</span>` +
                `<span class="card-value">${formatValue(card.value, card.format)}</span>` +
                `<span class="card-delta ${delta.cls}">${delta.text}</span>`;
            list.appendChild(item);
        });

        fillTable("top-products", kpis.topProducts, product => [
            product.name,
            { value: money.format(product.revenue), numeric: true },
            { value: plain.format(product.units), numeric: true },
        ]);

        fillTable("decliners", kpis.decliners, product => [
            product.name,
            { value: plain.format(product.units), numeric: true },
            { value: plain.format(product.previousUnits), numeric: true },
            { value: `${product.changePct}%`, numeric: true, cls: product.changePct < 0 ? "down" : "up" },
        ]);
    } catch (error) {
        list.innerHTML = `<li class="error">Couldn't load the figures. ${error.message}<li>`;
    }
}

function fillTable(id, rows, toCells) {
    const body = document.querySelector(`#${id} tbody`);
    body.innerHTML = "";

    if (!rows.length) {
        const row = document.createElement("tr");
        const cell = document.createElement("td");
        cell.colSpan = document.querySelectorAll(`#${id} thread th`).length;
        cell.textContent = "Nothing to show for this period.";
        row.appendChild(cell);
        body.appendChild(row);
        return;
    }

    rows.forEach(rowData => {
        const row = document.createElement("tr");
        toCells(rowData).forEach((cellData, index) => {
            const cell = document.createElement(index === 0 ? "th" : "td");
            if (index === 0) cell.scope = "row";

            if (typeof cellData === "string") {
                cell.textContent = cellData;
            } else {
                cell.textContent = cellData.value;
                if (cellData.numeric) cell.className = "num";
                if (cellData.cls) cell.className += ` ${cellData.cls}`;
            }
            row.appendChild(cell);
        });
        body.appendChild(row);
    });
}

async function loadChart(split) {
    const query = `/api/timeseries?metric=revenue&bucket=day&range=${RANGE}` + (split === "channel" ? "&by=channel" : "");

    try {
        const data = await getJson(query);
        const labels = data.series[0].points.map(point => formatDate(point.date, shortDate));
        const colors = ["#1f4d46", "#a9741b"];

        const datasets = data.series.map((series, index) => ({
            label: series.label,
            data: series.points.map(point => point.value),
            borderColor: colors[index % colors.length],
            backgroundColor: colors[index % colors.length],
            borderDash: index === 1 ? [6, 4] : [],
            borderWidth: 2,
            pointRadius: 0,
            pointHoverRadius: 4,
            tension: 0.15,
        }));

        if (chart) chart.destroy();

        chart = new Chart(document.getElementById("chart"), {
            type: "line",
            data: { labels, datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? false : { duration: 300 },
                interaction: { mode: "index", intersect: false },
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: { callback: value => money.format(value).replace(/\.00$/, "") },
                        grid: { color: "#d5dbd7" },
                    },
                    x: { grid: { display: false }, ticks: { maxTicksLimit: 8 } },
                },
                plugins: {
                    legend: { display: data.series.length > 1, labels: { boxWidth: 12 } },
                    tooltip: { callbacks: { label: item => `${item.dataset.label}: ${money.format(item.raw)}` } },
                },
            },
        });

        document.getElementById("chart").setAttribute(
            "aria-label",
            `Daily revenue, ${formatDate(data.range.start, longDate)} to ${formatDate(data.range.end, longDate)}. Table below`);

        buildChartTable(data);
    } catch (error) {
        document.getElementById("chart-table").querySelector("tbody").innerHTML =
            `<tr><td class="error">Couldn't load the chart. ${error.message}</td></tr>`;
    }
}

function buildChartTable(data) {
    const table = document.getElementById("chart-table");
    const head = table.querySelector("thead tr");
    const body = table.querySelector("tbody");

    head.innerHTML = '<th scope="col">Date</th>';
    data.series.forEach(series => {
        const cell = document.createElement("th");
        cell.scope = "col";
        cell.className = "num";
        cell.textContent = series.label;
        head.appendChild(cell);
    });

    body.innerHTML = "";
    data.series[0].points.forEach((point, index) => {
        const row = document.createElement("tr");
        const dateCell = document.createElement("th");
        dateCell.scope = "row";
        dateCell.textContent = formatDate(point.date, shortDate);
        row.appendChild(dateCell);

        data.series.forEach(series => {
            const cell = document.createElement("td");
            cell.className = "num";
            cell.textContent = money.format(series.points[index].value);
            row.appendChild(cell);
        });

        body.appendChild(row);
    });
}

async function loadAlerts() {
    const list = document.getElementById("alerts");

    try {
        const alerts = await getJson("/api/alerts");
        list.innerHTML = "";

        if (!alerts.length) {
            list.innerHTML = "<li>Nothing has crossed a threshold this week.</li>";
            return;
        }

        alerts.forEach(alert => {
            const item = document.createElement("li");
            item.innerHTML =
                `<span class="alert-subject">${alert.subject}</span>` +
                `<p class="alert-message">${alert.message}</p>`;
            list.appendChild(item);
        });
    } catch (error) {
        list.innerHTML = `<li class="error">Couldn't check the thresholds. ${error.message}</li>`;
    }
}

function setUpAsk() {
    const form = document.getElementById("ask-form");
    const input = document.getElementById("question");
    const passcode = document.getElementById("passcode");
    const button = document.getElementById("ask-button");
    const output = document.getElementById("answer");

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const question = input.value.trim();
        if (!question) return;

        button.disabled = true;
        button.textContent = "Looking...";
        output.innerHTML = '<p class="loading">Checking your numbers...</p>';

        try {
            const result = await getJson("/api/ask", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Ask-Passcode": passcode.value,
                },
                body: JSON.stringify({ question }),
            });

            output.innerHTML = "";
            const answer = document.createElement("p");
            answer.textContent = result.answer;
            output.appendChild(answer);

            if (result.sources.length) {
                const heading = document.createElement("p");
                heading.className = "sources";
                heading.textContent = "Sources";

                const sources = document.createElement("ul");
                sources.className = "sources";

                result.sources.forEach(source => {
                    const item = document.createElement("li");
                    // Plain words for the owner; the raw call stays available on hover.
                    item.textContent = describeSource(source);
                    item.title = `${source.tool}(${source.arguments})`;
                    sources.appendChild(item);
                });

                output.appendChild(heading);
                output.appendChild(sources);
            }
        } catch (error) {
            output.innerHTML = "";
            const message = document.createElement("p");
            message.className = "error";
            message.textContent = error.message;
            output.appendChild(message);
        } finally {
            button.disabled = false;
            button.textContent = "Ask";
        }
    });
}

async function start() {
    const health = await getJson("/api/health").catch(() => null);
    if (health) {
        document.getElementById("asof").textContent = `Figures as of ${formatDate(health.asOfDate, longDate)}`;
        
        if (health.askRequiresPasscode) {
            document.getElementById("passcode-row").hidden = false;
            document.querySelector(".ask .hint").textContent = 
                "Every figure in an answer comes from your own numbers. Live questions in this demo need a passcode; the video shows it in action.";
        }
    }

    document.querySelectorAll('input[name="split"]').forEach(radio => {
        radio.addEventListener("change", event => loadChart(event.target.value));
    });

    setUpAsk();
    loadBrief();
    loadKpis();
    loadChart("all");
    loadAlerts();
}

start();