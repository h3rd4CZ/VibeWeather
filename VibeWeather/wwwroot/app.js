// Small helpers for Razor components (called through IJSRuntime): measurements Blazor can't take itself, and the
// hand-over from the start screen in index.html.
window.vibeWeather = {
    // Width of an element in CSS pixels, to turn a touch position into a chart bar.
    width: element => element.getBoundingClientRect().width,

    // Remembers the appearance (light, dark or auto) for the next start's splash and applies it to the page now.
    saveTheme: mode => {
        try { localStorage.setItem('vw-theme', mode); } catch (e) { }
        window.vwApplyTheme(mode);
    },

    // Remembers how far (CSS px) the page's centre sits below the screen's, so the next splash can make up for it.
    saveSplashShift: px => {
        try { localStorage.setItem('vw-splash-shift', String(px)); } catch (e) { }
    },

    // Fades the start screen out once the app has rendered, but not before its animation has played (1.4 s).
    splashDone: () => {
        const splash = document.getElementById('vw-splash');
        if (!splash || splash.dataset.leaving) return;
        splash.dataset.leaving = '1';
        setTimeout(() => {
            // Only the fade of the splash itself, not an animation of one of its parts.
            splash.addEventListener('animationend', e => { if (e.target === splash) splash.remove(); });
            splash.classList.add('vw-splash-out');
        }, Math.max(0, 1400 - performance.now()));
    },
};
