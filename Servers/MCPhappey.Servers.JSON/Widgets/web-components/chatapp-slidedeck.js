class ChatAppSlidedeck extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: "open" });
    this.shadowRoot.innerHTML = `
      <style>
        :host { display: block; position: relative; overflow: hidden; width: 100%; height: 100%; background: var(--deck-bg, #111); color: var(--deck-color, #fff); }
        .slides { position: relative; width: 100%; height: 100%; }
        ::slotted(*) { position: absolute; inset: 0; opacity: 0; transition: opacity .6s ease; display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center; padding: 2rem; }
        ::slotted(.active) { opacity: 1; position: relative; }
        button { position: absolute; top: 50%; transform: translateY(-50%); background: rgba(0,0,0,0.5); border: none; color: white; width: 40px; height: 40px; border-radius: 50%; cursor: pointer; }
        .prev { left: 12px; } .next { right: 12px; }
      </style>
      <div class="slides"><slot></slot></div>
      <button class="prev">‹</button>
      <button class="next">›</button>
    `;
  }

  connectedCallback() {
    const slot = this.shadowRoot.querySelector("slot");
    const prev = this.shadowRoot.querySelector(".prev");
    const next = this.shadowRoot.querySelector(".next");
    const slides = () => slot.assignedElements();

    let index = 0;
    const show = i => {
      slides().forEach((el, j) => el.classList.toggle("active", j === i));
    };
    const move = d => { index = (index + d + slides().length) % slides().length; show(index); };

    prev.onclick = () => move(-1);
    next.onclick = () => move(1);
    document.addEventListener("keydown", e => { if (e.key === "ArrowRight") move(1); if (e.key === "ArrowLeft") move(-1); });
    requestAnimationFrame(() => show(0));
  }
}
customElements.define("chatapp-slidedeck", ChatAppSlidedeck);
