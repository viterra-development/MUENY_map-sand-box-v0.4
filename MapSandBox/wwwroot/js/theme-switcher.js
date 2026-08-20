// MUENY theme switcher. Pick a taste theme with ?theme=glass|minimal|base
// (persists in localStorage). Runs synchronously in <head>, right after the
// theme <link> tags, so the page never flashes the wrong theme.
(function () {
    const VALID = ['glass', 'minimal', 'base'];
    let theme = null;
    try {
        const fromUrl = new URLSearchParams(location.search).get('theme');
        if (fromUrl && VALID.includes(fromUrl)) {
            theme = fromUrl;
            localStorage.setItem('mueny_theme', theme);
        } else {
            theme = localStorage.getItem('mueny_theme');
        }
    } catch (_) { /* private mode etc. */ }
    if (!VALID.includes(theme)) theme = 'glass';

    const glass = document.getElementById('theme-glass');
    const minimal = document.getElementById('theme-minimal');
    if (glass) glass.disabled = theme !== 'glass';
    if (minimal) minimal.disabled = theme !== 'minimal';
})();
