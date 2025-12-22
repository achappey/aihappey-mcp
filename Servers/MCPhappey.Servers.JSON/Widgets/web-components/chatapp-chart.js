// chart-js.js — generic Chart.js web component (no opinions, no datasets)
class ChartJsElement extends HTMLElement {
  static get observedAttributes() { return ["type","data","options","height"]; }

  constructor() {
    super();
    this._chart = null;
    this._pending = false;
    this._connected = false;

    // default config is empty; caller MUST provide data/options
    this._config = { type: "bar", data: { labels: [], datasets: [] }, options: { responsive:true, maintainAspectRatio:false } };

    const root = this.attachShadow({ mode:"open" });
    const style = document.createElement("style");
    style.textContent = `
      :host { display:block; position:relative; }
      canvas { display:block; width:100% !important; height:auto !important; }
    `;
    this._canvas = document.createElement("canvas");
    root.append(style, this._canvas);

    // Robustness: wait for size/visibility changes
    this._ro = new ResizeObserver(() => this._scheduleRender());
    this._io = new IntersectionObserver((entries)=> {
      if (entries.some(e=>e.isIntersecting)) this._scheduleRender();
    }, { threshold:0 });

    // Load Chart.js only if needed
    this._ready = (async () => {
      if (!window.Chart) await import("https://cdn.jsdelivr.net/npm/chart.js@4.5.1/dist/chart.umd.js");
      return window.Chart;
    })();
  }

  connectedCallback() {
    this._connected = true;
    ["type","data","options","height"].forEach(a => this._applyAttr(a, this.getAttribute(a)));
    this._ro.observe(this);
    this._io.observe(this);
    this._scheduleRender();
  }

  disconnectedCallback() {
    this._connected = false;
    this._ro.disconnect(); this._io.disconnect();
    this._destroy();
  }

  attributeChangedCallback(name, _old, val) { this._applyAttr(name, val); this._scheduleRender(); }

  // Public API
  update({ type, data, options, height } = {}) {
    if (type) this._config.type = type;
    if (data) this._config.data = data;
    if (options) this._config.options = options;
    if (height) this.style.setProperty("--chart-height", /^\d+$/.test(String(height)) ? `${height}px` : String(height));
    this._scheduleRender();
  }
  get config(){ return this._config; } // optional getter

  // Internals
  _applyAttr(name, value){
    if (value==null) return;
    if (name==="type") this._config.type = String(value);
    else if (name==="data") this._config.data = this._parseJSON(value, this._config.data);
    else if (name==="options") this._config.options = this._parseJSON(value, this._config.options);
    else if (name==="height") this.style.setProperty("--chart-height", /^\d+$/.test(value) ? `${value}px` : value);
  }
  _parseJSON(v,f){ try { return typeof v==="string" ? JSON.parse(v) : (v ?? f); } catch { return f; } }
  _scheduleRender(){ if (this._pending) return; this._pending=true; requestAnimationFrame(()=>{ this._pending=false; this._render(); }); }

  async _render(){
    if (!this._connected) return;
    await this._ready;
    const el = this._canvas;
    if (!el || !el.isConnected) return;

    const rect = this.getBoundingClientRect();
    if (rect.width===0 || rect.height===0) return; // wait until visible

    const hasDatasets = Array.isArray(this._config?.data?.datasets) && this._config.data.datasets.length>0;
    const hasLabels   = Array.isArray(this._config?.data?.labels) && this._config.data.labels.length>0;
    if (!hasDatasets && !hasLabels) return; // nothing to draw yet

    const ctx = el.getContext("2d");
    if (!ctx) return; // environment refused 2D context

    if (!this._chart) {
      this._chart = new Chart(ctx, this._config);
      return;
    }
    if (this._chart.config.type !== this._config.type) {
      this._destroy(); this._chart = new Chart(ctx, this._config);
    } else {
      this._chart.data = this._config.data;
      this._chart.options = this._config.options;
      this._chart.update();
    }
  }

  _destroy(){ if (this._chart) { try { this._chart.destroy(); } finally { this._chart=null; } } }
}
customElements.define("chatapp-chart", ChartJsElement);
