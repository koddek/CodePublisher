import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(true)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();
const host = document.getElementById('out');

function synchroniseCanvasSize() {
    const canvas = host?.querySelector('canvas');
    if (!canvas || !host) {
        return;
    }

    const bounds = host.getBoundingClientRect();
    const scale = globalThis.devicePixelRatio || 1;
    const width = Math.max(1, Math.round(bounds.width * scale));
    const height = Math.max(1, Math.round(bounds.height * scale));
    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }
}

if (host) {
    globalThis.addEventListener('resize', synchroniseCanvasSize);
    if (globalThis.ResizeObserver) {
        new ResizeObserver(synchroniseCanvasSize).observe(host);
    }
    for (const delay of [0, 100, 500, 1500]) {
        globalThis.setTimeout(synchroniseCanvasSize, delay);
    }
}

try {
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
    synchroniseCanvasSize();
}
catch (error) {
    console.error('.NET App Publisher failed to start.', error);
    const splash = document.querySelector('.avalonia-splash');
    if (splash) {
        splash.classList.remove('splash-close');
        splash.querySelector('h2').textContent = '.NET App Publisher could not start. See the browser console for details.';
    }
    throw error;
}
