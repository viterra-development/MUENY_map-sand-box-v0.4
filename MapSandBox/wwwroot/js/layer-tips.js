// MUENY layer tips — hover/focus descriptions for sidebar layer rows.
// Any element with a data-layer-tip attribute gets a small fixed-position
// tooltip explaining what the layer shows and why it's useful. One shared
// tooltip node; positioned beside the row, clamped to the viewport.
(function () {
    let tip = null;
    let showTimer = null;

    function ensureTip() {
        if (tip && tip.isConnected) return tip;
        tip = document.createElement('div');
        tip.className = 'mueny-layer-tip';
        tip.setAttribute('role', 'tooltip');
        document.body.appendChild(tip);
        return tip;
    }

    function show(target) {
        const text = target.getAttribute('data-layer-tip');
        if (!text) return;
        const node = ensureTip();
        node.textContent = text;
        node.classList.add('visible');
        const rect = target.getBoundingClientRect();
        // Prefer to the right of the sidebar row; flip left if cramped.
        let x = rect.right + 12;
        const width = Math.min(260, window.innerWidth - 24);
        node.style.maxWidth = width + 'px';
        if (x + width > window.innerWidth - 8) x = Math.max(8, rect.left);
        let y = rect.top;
        node.style.left = x + 'px';
        node.style.top = y + 'px';
        // After paint, clamp bottom edge.
        requestAnimationFrame(() => {
            const tr = node.getBoundingClientRect();
            if (tr.bottom > window.innerHeight - 8) {
                node.style.top = Math.max(8, window.innerHeight - tr.height - 8) + 'px';
            }
        });
    }

    function hide() {
        clearTimeout(showTimer);
        if (tip) tip.classList.remove('visible');
    }

    function onOver(e) {
        const target = e.target.closest('[data-layer-tip]');
        if (!target) { hide(); return; }
        clearTimeout(showTimer);
        showTimer = setTimeout(() => show(target), 350);
    }

    document.addEventListener('mouseover', onOver);
    document.addEventListener('mouseout', (e) => {
        if (e.target.closest && e.target.closest('[data-layer-tip]')) hide();
    });
    document.addEventListener('focusin', (e) => {
        const target = e.target.closest && e.target.closest('[data-layer-tip]');
        if (target) show(target);
    });
    document.addEventListener('focusout', hide);
    document.addEventListener('scroll', hide, true);
})();
