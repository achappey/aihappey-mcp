class ChatAppCard extends HTMLElement {
  static get observedAttributes() { return ["title", "description", "image"]; }

  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    this.shadowRoot.innerHTML = `
      <style>
        :host {
          display: flex;
          flex-direction: column;
          font-family: var(--card-font, system-ui, sans-serif);
          background: var(--card-bg, #fff);
          color: var(--card-color, #111);
          border-radius: var(--card-radius, 12px);
          box-shadow: var(--card-shadow, 0 2px 6px rgba(0,0,0,0.08));
          overflow: hidden;
          margin-right: 8px;
          transition: box-shadow .2s ease;
        }
        :host(:hover) {
          box-shadow: var(--card-shadow-hover, 0 4px 12px rgba(0,0,0,0.12));
        }
        .header {
          display: flex;
          align-items: center;
          gap: 12px;
          padding: 8px;
        }
        .header img {
          width: 64px; height: 64px;
          border-radius: 4px;
          object-fit: cover;
          background: #f2f2f2;
        }
        .header-text {
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .title {
          font-weight: 600;
          font-size: 1.1em;
          line-height: 1.3;
          display: -webkit-box;
          -webkit-line-clamp: 2; /* limit to 2 lines */
          -webkit-box-orient: vertical;
          overflow: hidden;
          text-overflow: ellipsis;
          word-break: break-word;
        }
        .description {
          font-size: .9em;
          color: var(--card-muted, #666);
        }
        .content {
          flex: 1;
          padding-left: 8px;
          padding-right: 8px;
        }
        ::slotted(p), ::slotted(div) {
          margin: 0 0 0.5em 0;
        }
        .actions {
          display: flex;
          justify-content: flex-start;
          gap: 8px;
          padding: 8px;
          margin-top: auto;  
        }
        ::slotted(button) {
          padding: 6px 12px;
          border: none;
          border-radius: 6px;
          background-color: transparent;
          color: var(--button-color, #111);
          cursor: pointer;
          font-size: .9em;
          transition: background .2s;
        }
      </style>

      <div class="header">
        <img class="img" part="image" />
        <div class="header-text">
          <div class="title" part="title"></div>
          <div class="description" part="description"></div>
        </div>
      </div>
      <div class="content">
        <slot name="content"></slot>
      </div>
      <div class="actions">
        <slot name="actions"></slot>
      </div>
    `;
  }

  connectedCallback() {
    this._update();
  }

  attributeChangedCallback() {
    this._update();
  }

  _update() {
    const img = this.getAttribute("image");
    const title = this.getAttribute("title");
    const desc = this.getAttribute("description");

    const imgEl = this.shadowRoot.querySelector(".img");
    const titleEl = this.shadowRoot.querySelector(".title");
    const descEl = this.shadowRoot.querySelector(".description");

    if (img) {
      imgEl.src = img;
      imgEl.style.display = "block";
    } else {
      imgEl.style.display = "none";
    }

    titleEl.textContent = title || "";
    descEl.textContent = desc || "";
  }
}

customElements.define("chatapp-card", ChatAppCard);
