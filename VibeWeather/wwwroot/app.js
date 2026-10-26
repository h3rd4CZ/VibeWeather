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

            // The thumb is as wide as the visible share of the chart, but at least 32 px to be grabbed; the chart's
            // scroll range maps onto the track less the thumb's width, so both ends line up.
            const thumbWidth = () => Math.max(32, track.clientWidth * Math.min(1, scroller.clientWidth / scroller.scrollWidth));
            const range = () => ({ chart: scroller.scrollWidth - scroller.clientWidth, track: track.clientWidth - thumbWidth() });

            const update = () => {
                if (!scroller.scrollWidth) return;
                const r = range();
                thumb.style.width = `${thumbWidth()}px`;
                thumb.style.left = `${r.chart > 0 ? scroller.scrollLeft / r.chart * r.track : 0}px`;
                // The day in the middle of the chart's visible part is the current one (56 px per hour column).
                const at = Math.floor((scroller.scrollLeft + scroller.clientWidth / 2) / 56);
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

            // Dragging the thumb moves the chart by the same share of its scroll range.
            let drag = null;
            thumb.addEventListener('pointerdown', e => {
                drag = { x: e.clientX, left: scroller.scrollLeft };
                thumb.setPointerCapture(e.pointerId);
                e.stopPropagation();
            });
            thumb.addEventListener('pointermove', e => {
                if (!drag) return;
                const r = range();
                if (r.track > 0) scroller.scrollLeft = drag.left + (e.clientX - drag.x) / r.track * r.chart;
            });
            const release = () => { drag = null; };
            thumb.addEventListener('pointerup', release);
            thumb.addEventListener('pointercancel', release);

            // A tap beside the thumb moves the thumb's middle there.
            track.addEventListener('pointerdown', e => {
                if (e.target === thumb) return;
                const r = range();
                const x = e.clientX - track.getBoundingClientRect().left - thumbWidth() / 2;
                if (r.track > 0) scroller.scrollTo({ left: x / r.track * r.chart, behavior: 'smooth' });
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
