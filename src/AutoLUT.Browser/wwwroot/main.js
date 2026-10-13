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

// Web Crypto only exists in secure contexts (https, localhost). Without it, the browser's
// own fetch integrity check takes over, at the cost of smooth progress for that resource.
const subtle = globalThis.crypto?.subtle;
const hashAlgorithms = { sha256: "SHA-256", sha384: "SHA-384", sha512: "SHA-512" };

// Parses the boot config's "sha256-<base64>" into a Web Crypto algorithm and expected digest.
function parseIntegrity(integrity) {
    if (!integrity) {
        return null;
    }
    const dash = integrity.indexOf("-");
    const algorithm = dash > 0 ? hashAlgorithms[integrity.slice(0, dash)] : undefined;
    return algorithm ? { algorithm, expected: integrity.slice(dash + 1).trim() } : null;
}

function toBase64(buffer) {
    let binary = "";
    for (const byte of new Uint8Array(buffer)) {
        binary += String.fromCharCode(byte);
    }
    return btoa(binary);
}

async function digestOf(algorithm, chunks, length) {
    const all = new Uint8Array(length);
    let offset = 0;
    for (const chunk of chunks) {
        all.set(chunk, offset);
        offset += chunk.length;
    }
    return toBase64(await subtle.digest(algorithm, all));
}

// Set when a download fails its integrity check. The runtime reports the failure only as a
// generic "Failed to fetch", so the startup error message prefers this one.
let integrityFailure = null;

// A custom resource loader replaces the runtime's own fetch, including its integrity
// check against the boot config hashes, so this loader checks them itself.
function loadResource(type, name, defaultUri, integrity, behavior) {
    // Module scripts must load via URI so the runtime can import() them.
    if (type === "dotnetjs") {
        return defaultUri;
    }

    const check = parseIntegrity(integrity);
    const init = { cache: behavior === "no-cache" ? "no-cache" : "default" };
    if (check && !subtle) {
        init.integrity = integrity;
    }

    return fetch(defaultUri, init).then((response) => {
        if (!response.ok || !response.body) {
            return response;
        }

        const contentLength = Number(response.headers.get("Content-Length"));
        const verify = check && subtle;
        if (!contentLength && !verify) {
            return response;
        }

        const state = { total: contentLength || 0, loaded: 0, done: false };
        if (contentLength) {
            resources.push(state);
            updateBar();
        }

        const chunks = [];
        let length = 0;
        const reader = response.body.getReader();
        const counting = new ReadableStream({
            async pull(controller) {
                const { done, value } = await reader.read();
                if (done) {
                    // Checked before the stream closes, so a consumer that started on the early
                    // bytes (streaming WebAssembly compile) fails instead of running them.
                    if (verify && (await digestOf(check.algorithm, chunks, length)) !== check.expected) {
                        integrityFailure = `${name} failed its integrity check: the download is corrupted or was altered.`;
                        controller.error(new Error(integrityFailure));
                        return;
                    }
                    state.done = true;
                    updateBar();
                    controller.close();
                    return;
                }
                if (verify) {
                    chunks.push(value);
                    length += value.length;
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

// Replaces the splash (or, if the app had already taken over the page, everything) with the
// error, so a failed start never sits on "Loading AutoLUT..." forever.
function showStartupError(error) {
    console.error(error);
    const message = integrityFailure ?? (error instanceof Error ? error.message : String(error));
    const container = document.getElementById("splash") ?? document.getElementById("out") ?? document.body;
    container.replaceChildren();

    const heading = document.createElement("p");
    heading.textContent = "AutoLUT could not start.";
    const detail = document.createElement("p");
    detail.className = "hint";
    detail.textContent = message;
    const advice = document.createElement("p");
    advice.className = "hint";
    advice.textContent = "Reload the page to try again. If it keeps failing, clear this site's data in the browser settings, or use the desktop app.";
    container.append(heading, detail, advice);
}

// The runtime reports a failed asset download (missing file, failed integrity check) as an
// unhandled rejection and leaves create() pending forever, so listen for those while it starts.
function onStartupRejection(event) {
    showStartupError(event.reason);
}
globalThis.addEventListener("unhandledrejection", onStartupRejection);

try {
    // Imported here rather than statically, so a failed download of the runtime itself also
    // reaches the error message instead of leaving the splash up.
    const { dotnet } = await import("./_framework/dotnet.js");
    const dotnetRuntime = await dotnet.withDiagnosticTracing(false).withApplicationArgumentsFromQuery().withResourceLoader(loadResource).create();
    // From here on the app owns the page; a stray rejection must not wipe a working app.
    globalThis.removeEventListener("unhandledrejection", onStartupRejection);

    if (barFill) {
        barFill.style.width = "100%";
    }
    if (label) {
        label.textContent = "Starting...";
    }

    const config = dotnetRuntime.getConfig();

    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    showStartupError(error);
}
