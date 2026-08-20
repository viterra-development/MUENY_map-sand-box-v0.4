// MUENY landing page behavior: count-up stats + reveal-on-scroll.
// External file (no inline scripts under the CSP); degrades to static
// content when JS is unavailable or reduced motion is requested.
(function () {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ---- count-up stats ----
    function finishCount(el) {
        el.textContent = Number(el.dataset.target).toLocaleString();
    }
    function animateCount(el) {
        const target = Number(el.dataset.target);
        const duration = 1400;
        const start = performance.now();
        function tick(now) {
            const t = Math.min((now - start) / duration, 1);
            const eased = 1 - Math.pow(1 - t, 3);
            el.textContent = Math.round(target * eased).toLocaleString();
            if (t < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
    }

    const counters = document.querySelectorAll('.count[data-target]');
    if (reduced || !('IntersectionObserver' in window)) {
        counters.forEach(finishCount);
        document.querySelectorAll('.reveal').forEach(el => el.classList.add('in'));
        return;
    }

    const countObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                animateCount(entry.target);
                countObserver.unobserve(entry.target);
            }
        });
    }, { threshold: 0.4 });
    counters.forEach(el => countObserver.observe(el));

    // ---- reveal-on-scroll ----
    const revealObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('in');
                revealObserver.unobserve(entry.target);
            }
        });
    }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
    document.querySelectorAll('.reveal').forEach(el => revealObserver.observe(el));
})();
