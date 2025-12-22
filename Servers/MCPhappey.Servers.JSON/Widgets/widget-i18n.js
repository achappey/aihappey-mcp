// widget-i18n.js (no hardcoded translations)
(() => {
  const GLOBAL = "__WIDGET_I18N_STATE__";
  const DEFAULTS = { locale: "en", fallback: "en", storeName: "i18n", messages: {} };

  // Keep mutable state in a single place
  const state = (window[GLOBAL] = window[GLOBAL] || { ...DEFAULTS });

  // Utilities
  const deepMerge = (t, s) => {
    if (!s || typeof s !== "object") return t;
    for (const k of Object.keys(s)) {
      if (s[k] && typeof s[k] === "object" && !Array.isArray(s[k])) {
        t[k] = deepMerge(t[k] || {}, s[k]);
      } else {
        t[k] = s[k];
      }
    }
    return t;
  };
  const get = (obj, path) => path.split(".").reduce((acc, key) => (acc == null ? acc : acc[key]), obj);
  const format = (str, vars = {}) => String(str).replace(/\{(\w+)\}/g, (_, k) => (k in vars ? vars[k] : `{${k}}`));

  // Alpine hookup
  let alpineRegistered = false;
  let alpine, storeRef;

  function registerWithAlpine() {
    if (alpineRegistered) return;
    if (!window.Alpine) return;
    alpine = window.Alpine;

    // Create reactive store
    const reactive = alpine.reactive({
      locale: state.locale,
      fallback: state.fallback,
      messages: state.messages
    });

    alpine.store(state.storeName, reactive);

    // $t magic
    alpine.magic("t", () => (key, vars = {}) => {
      const m = alpine.store(state.storeName);
      const msg = get(m.messages[m.locale] || {}, key) ?? get(m.messages[m.fallback] || {}, key) ?? key;
      return format(msg, vars);
    });

    storeRef = reactive;
    alpineRegistered = true;
  }

  // Public API
  function initWidgetI18n(cfg = {}) {
    // Apply config immediately to state
    const { messages, locale, fallback, storeName } = cfg;
    if (storeName) state.storeName = storeName;
    if (typeof locale === "string") state.locale = locale;
    if (typeof fallback === "string") state.fallback = fallback;
    if (messages && typeof messages === "object") {
      state.messages = deepMerge(state.messages || {}, messages);
    }

    // If Alpine already running, reflect into the store
    if (alpineRegistered && storeRef) {
      if (messages) storeRef.messages = deepMerge(storeRef.messages || {}, messages);
      if (typeof locale === "string") storeRef.locale = locale;
      if (typeof fallback === "string") storeRef.fallback = fallback;
    }
  }

  function mergeWidgetI18n(cfg = {}) {
    if (cfg.messages) {
      state.messages = deepMerge(state.messages || {}, cfg.messages);
      if (alpineRegistered && storeRef) {
        storeRef.messages = deepMerge(storeRef.messages || {}, cfg.messages);
      }
    }
  }

  function setWidgetLocale(locale) {
    state.locale = locale;
    if (alpineRegistered && storeRef) storeRef.locale = locale;
  }

  function getWidgetI18n() {
    // Returns the live Alpine store (if available) or a plain snapshot
    if (alpineRegistered) return window.Alpine.store(state.storeName);
    return { locale: state.locale, fallback: state.fallback, messages: state.messages };
  }

  // Expose API
  window.initWidgetI18n = initWidgetI18n;
  window.mergeWidgetI18n = mergeWidgetI18n;
  window.setWidgetLocale = setWidgetLocale;
  window.getWidgetI18n = getWidgetI18n;

  // Register now or when Alpine initializes
  if (window.Alpine) {
    registerWithAlpine();
  } else {
    document.addEventListener("alpine:init", registerWithAlpine, { once: true });
  }
})();
