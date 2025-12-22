// --- THEME: apply ASAP and keep in sync, independent of widgets ---
(function () {
    const root = document.documentElement;

    function setTheme(theme) {
        const t = (theme ?? window.openai?.theme ?? '').toLowerCase();
        if (t === 'dark') root.setAttribute('data-theme', 'dark');
        else if (t === 'light') root.removeAttribute('data-theme'); // light = defaults
        else {
            // Fallback to system preference
            const dark = matchMedia('(prefers-color-scheme: dark)').matches;
            dark ? root.setAttribute('data-theme', 'dark') : root.removeAttribute('data-theme');
        }
    }

    // expose
    window.widgetBase = window.widgetBase || {};
    window.widgetBase.setTheme = setTheme;

    setTheme();

    window.widgetBase.syncOpenAI = function (ctx) {
        window.addEventListener('message', (e) => {
            const m = e?.data;
            if (!m) return;

            if (m.type === 'toolInput') {
                ctx.input = m.payload;
                if (typeof ctx.load === 'function') ctx.load();
                else if (ctx.$nextTick) ctx.$nextTick(() => { });
                // ctx.load?.();
                //this.$nextTick(() => { });
            }

            if (m.type === 'toolOutput') {
                ctx.data = m.payload;
                if (typeof ctx.load === 'function') ctx.load();
                else if (ctx.$nextTick) ctx.$nextTick(() => { });
                //   ctx.load?.();
                //  this.$nextTick(() => { });
            }

            if (m.type === 'openai:set_globals') {
                const g = m.globals || {};
                if (g.theme) setTheme(g.theme);
                if (g.locale && window.Alpine) Alpine.store('i18n').locale = g.locale;
                if (g.displayMode && window.Alpine) Alpine.store('ui').displayMode = g.displayMode;
                if (g.maxHeight != null) {
                    if (window.Alpine) Alpine.store('ui').maxHeight = g.maxHeight;
                    document.documentElement.style.setProperty('--widget-max-height', g.maxHeight + 'px');
                }
            }
        });
    };

    window.askFollowup = function (prompt) {
        window.openai?.sendFollowupTurn?.({ prompt: prompt });
    }

    window.callTool = function (name, args) {
        return window.openai?.callTool?.(name, args);
    }

    window.markdownToText = function (md) {
        if (!md) return '';
        return md
            // remove code blocks
            .replace(/```[\s\S]*?```/g, '')
            // remove inline code
            .replace(/`([^`]+)`/g, '$1')
            // remove links but keep text
            .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '$1')
            // remove bold and italics
            .replace(/(\*\*|__)(.*?)\1/g, '$2')
            .replace(/(\*|_)(.*?)\1/g, '$2')
            // remove headings, blockquotes, lists
            .replace(/^#+\s*(.*)/gm, '$1')
            .replace(/^>\s*(.*)/gm, '$1')
            .replace(/^([-*+]|\d+\.)\s+/gm, '')
            // collapse multiple newlines
            .replace(/\n{2,}/g, '\n')
            .trim();
    };



    window.addEventListener('message', (e) => {
        const g = e?.data?.globals;
        if (e?.data?.type === 'openai:set_globals' && g && 'theme' in g) {
            setTheme(g.theme);
        }
    });

    // --- Alpine stores remain as-is ---
    window.widgetBase.initAlpine = function () {
        if (!window.Alpine) return;
        if (!Alpine.store('i18n')) Alpine.store('i18n', { locale: window.openai?.locale, dict: {}, t: (k) => k });
        if (!Alpine.store('ui')) Alpine.store('ui', {
            displayMode: window.openai?.displayMode ?? 'inline',
            theme: window.openai?.theme,
            maxHeight: window.openai?.maxHeight,
        });


        const ui = Alpine.store('ui');
        setTheme(ui.theme);
        if (ui.maxHeight) document.documentElement.style.setProperty('--widget-max-height', ui.maxHeight + 'px');
    };

    document.addEventListener('alpine:init', () => window.widgetBase.initAlpine());
})();