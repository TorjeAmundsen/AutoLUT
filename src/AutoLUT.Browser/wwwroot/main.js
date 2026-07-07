import { dotnet } from "./_framework/dotnet.js";

const is_browser = typeof window != "undefined";
if (!is_browser) {
    throw new Error(`Expected to be running in a browser`);
}

const barFill = document.getElementById("splash-bar-fill");
const label = document.getElementById("splash-label");

// Per-resource download state: { total, loaded, done }. Progress is byte-weighted;
// each resource's contribution is clamped to its Content-Length because GitHub Pages
// gzips in transit (Content-Length = compressed size, the body reader yields
// decompressed bytes), which would otherwise overshoot 100%.
const resources = [];

// The runtime discovers resources in stages, so the byte total grows mid-download;
// keep the bar monotonic instead of letting a growing denominator drag it backwards.
let displayedPercent = 0;

function updateBar() {
    let total = 0;
    let loaded = 0;
    for (const r of resources) {
        total += r.total;
        loaded += r.done ? r.total : Math.min(r.loaded, r.total);
    }
    if (total > 0 && barFill) {
        displayedPercent = Math.max(displayedPercent, (100 * loaded) / total);
        barFill.style.width = `${displayedPercent.toFixed(1)}%`;
    }
}

function loadResource(type, name, defaultUri, integrity, behavior) {
    // Module scripts must load via URI so the runtime can import() them.
    if (type === "dotnetjs") {
        return defaultUri;
    }

    return fetch(defaultUri, { cache: behavior === "no-cache" ? "no-cache" : "default" }).then((response) => {
        const contentLength = Number(response.headers.get("Content-Length"));
        if (!response.ok || !response.body || !contentLength) {
            return response;
        }

        const state = { total: contentLength, loaded: 0, done: false };
        resources.push(state);
        updateBar();

        const reader = response.body.getReader();
        const counting = new ReadableStream({
            async pull(controller) {
                const { done, value } = await reader.read();
                if (done) {
                    state.done = true;
                    updateBar();
                    controller.close();
                    return;
                }
                state.loaded += value.length;
                updateBar();
                controller.enqueue(value);
            },
            cancel(reason) {
                return reader.cancel(reason);
            },
        });

        return new Response(counting, {
            status: response.status,
            statusText: response.statusText,
            headers: response.headers,
        });
    });
}

const dotnetRuntime = await dotnet.withDiagnosticTracing(false).withApplicationArgumentsFromQuery().withResourceLoader(loadResource).create();

if (barFill) {
    barFill.style.width = "100%";
}
if (label) {
    label.textContent = "Starting...";
}

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
