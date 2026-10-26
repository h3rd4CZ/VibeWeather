// Small helpers for Razor components (called through IJSRuntime): measurements Blazor can't take itself, and the
// hand-over from the start screen in index.html.
window.vibeWeather = {
    // Width of an element in CSS pixels, to turn a touch position into a chart bar.
    width: element => element.getBoundingClientRect().width,

    // The day scrubber under the hourly chart (see Hourly.razor): the thumb shows the part of the chart on screen and
    // follows its scrolling; dragging the thumb scrolls the chart, a tap on the track centres the chart there, a tap on
    // a day goes to its first hour. Handled here so scrolling never waits on .NET.
    hourScrubber: {
        // Called after every render; sets up once per scrubber element, later only refreshes the thumb.
        attach: (scroller, scrubber) => {
            if (scrubber.vwUpdate) { scrubber.vwUpdate(); return; }
            const track = scrubber.querySelector('.scrub-track');
            const thumb = scrubber.querySelector('.scrub-thumb');

            const update = () => {
                const total = scroller.scrollWidth;
                if (!total) return;
                thumb.style.left = `${scroller.scrollLeft / total * 100}%`;
                thumb.style.width = `${Math.min(1, scroller.clientWidth / total) * 100}%`;
                // The day at the chart's left edge is the current one (56 px per hour column).
                const at = Math.round(scroller.scrollLeft / 56);
                let current = null;
                for (const day of scrubber.querySelectorAll('.scrub-day')) {
                    day.classList.remove('current');
                    if (+day.dataset.column <= at) current = day;
                }
                current?.classList.add('current');
            };
            scrubber.vwUpdate = update;
            scroller.addEventListener('scroll', update, { passive: true });
            // New data or a rotated screen change the widths without a scroll.
            const resized = new ResizeObserver(update);
            resized.observe(scroller);
            resized.observe(scroller.firstElementChild);
            update();

            scrubber.addEventListener('click', e => {
                const day = e.target.closest('.scrub-day');
                if (day) vibeWeather.hourScrubber.scrollToColumn(scroller, +day.dataset.column, true);
            });

            // Dragging the thumb moves the chart by as much of its length as the thumb moves along the track.
            let drag = null;
            thumb.addEventListener('pointerdown', e => {
                drag = { x: e.clientX, left: scroller.scrollLeft };
                thumb.setPointerCapture(e.pointerId);
                e.stopPropagation();
            });
            thumb.addEventListener('pointermove', e => {
                if (drag) scroller.scrollLeft = drag.left + (e.clientX - drag.x) / track.clientWidth * scroller.scrollWidth;
            });
            const release = () => { drag = null; };
            thumb.addEventListener('pointerup', release);
            thumb.addEventListener('pointercancel', release);

            track.addEventListener('pointerdown', e => {
                if (e.target === thumb) return;
                const rect = track.getBoundingClientRect();
                const x = (e.clientX - rect.left) / rect.width * scroller.scrollWidth;
                scroller.scrollTo({ left: x - scroller.clientWidth / 2, behavior: 'smooth' });
            });
        },

        // Puts an hour column at the chart's left edge (56 px per column).
        scrollToColumn: (scroller, column, smooth) =>
            scroller.scrollTo({ left: column * 56, behavior: smooth ? 'smooth' : 'auto' }),
    },

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
