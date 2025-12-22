
class ChatAppHeader extends HTMLElement {
  static get observedAttributes() { return ["text", "no-fullscreen"]; }

  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    this.mode = "inline";
    this.shadowRoot.innerHTML = `
      <style>
        :host {
          display: flex;
          align-items: center;
          justify-content: space-between;
          color: var(--text, #fff);
          font-family: var(--header-font, system-ui, sans-serif);
          box-sizing: border-box;
          padding-bottom: 4px;
        }
        h1 {
          margin: 0;
          font-size: 1rem;
          font-weight: 600;
        }
        button {
          background: transparent;
          color: inherit;
          border: none;
          font-size: 0.85rem;
          padding: 0.4rem 0.75rem;
          cursor: pointer;
          transition: background 0.2s ease, transform 0.1s ease;
        }
        button:hover {
          background: rgba(255, 255, 255, 0.1);
          transform: scale(1.05);
        }
      </style>
      <h1></h1>
      <button id="toggleBtn">Fullscreen</button>
    `;
  }

  connectedCallback() {
    this.update();
    this.shadowRoot.getElementById("toggleBtn").addEventListener("click", () => this.toggleMode());
  }

  attributeChangedCallback() {
    this.update();
  }

  update() {
    const title = this.getAttribute("text") || "";
    const hideButton = this.hasAttribute("no-fullscreen");
    this.shadowRoot.querySelector("h1").textContent = title;
    this.shadowRoot.getElementById("toggleBtn").style.display = hideButton ? "none" : "inline-block";
  }

  toggleMode() {
    this.mode = this.mode === "inline" ? "fullscreen" : "inline";
    const btn = this.shadowRoot.getElementById("toggleBtn");
    btn.textContent = this.mode === "fullscreen" ? "Inline" : "Fullscreen";
    this.dispatchEvent(new CustomEvent("modechange", { detail: { mode: this.mode } }));
  }
}

customElements.define("chatapp-header", ChatAppHeader);