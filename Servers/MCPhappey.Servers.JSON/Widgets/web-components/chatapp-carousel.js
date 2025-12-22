class ChatAppCarousel extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    this.shadowRoot.innerHTML = `
      <style>
        :host {
          display: block;
          position: relative;
          overflow: hidden;
          height: 100%;
          width: 100%;
        }
        .track {
          display: flex;
          overflow: hidden;
          scroll-behavior: smooth;
          scroll-snap-type: x mandatory;
          -webkit-overflow-scrolling: touch;
        }
        ::slotted(*) {
          flex: 0 0 100%;
          scroll-snap-align: center;
          scroll-snap-align: center;
          box-sizing: border-box;
          height: 100%;
        }
        button {
          position: absolute;
          top: 50%;
          transform: translateY(-50%);
          background: var(--carousel-button-bg, rgba(0,0,0,0.5));
          color: var(--carousel-button-color, #fff);
          border: none;
          width: 32px;
          height: 32px;
          border-radius: 50%;
          cursor: pointer;
        }
        .prev { left: 8px; }
        .next { right: 8px; }
        .track { height: 100%; }
      </style>
      <div class="track">
        <slot></slot>
      </div>
      <button class="prev"><</i></button>
      <button class="next">></i></button>
    `;
  }

  connectedCallback() {
    // Wait until the slot content is rendered before selecting
    requestAnimationFrame(() => {
      this.track = this.shadowRoot.querySelector(".track");
      const prev = this.shadowRoot.querySelector(".prev");
      const next = this.shadowRoot.querySelector(".next");

      if (!this.track || !prev || !next) return;

      prev.addEventListener("click", () => {
        this.track.scrollBy({ left: -this.track.clientWidth, behavior: "smooth" });
      });

      next.addEventListener("click", () => {
        this.track.scrollBy({ left: this.track.clientWidth, behavior: "smooth" });
      });

      this._resizeObserver = new ResizeObserver(() => this._updateCardWidths());
      this._resizeObserver.observe(this);

      this._updateCardWidths();
    });
  }

  disconnectedCallback() {
    this._resizeObserver?.disconnect();
  }


  _updateCardWidths() {
    const width = this.offsetWidth;
    let cardsPerView = 3;
    if (width < 600) cardsPerView = 1;
    else if (width < 900) cardsPerView = 2;
    const cardWidth = (100 / cardsPerView); // leave small gap
    this.shadowRoot.querySelectorAll("::slotted(*)");
    const slot = this.shadowRoot.querySelector("slot");
    slot.assignedElements().forEach(el => {
      el.style.flex = `0 0 ${cardWidth}%`;
    });
  }

  scroll(direction) {
    const card = this.track.firstElementChild;
    const step = card ? card.getBoundingClientRect().width : 300;
    this.track.scrollBy({ left: direction * step, behavior: "smooth" });
  }
}

customElements.define("chatapp-carousel", ChatAppCarousel);
