// Touch-friendly drawing pad: pen, eraser, fill bucket, undo, colours and sizes.
// Works with mouse, finger and stylus (pointer events, pressure-aware).
"use strict";

const SKETCH_COLORS = ["#1b1b1b", "#ffffff", "#7f7f7f", "#e63946", "#f4a261", "#ffd166", "#06d6a0", "#2a9d8f", "#118ab2", "#3a0ca3", "#ff70a6", "#8d5524", "#c68642", "#ffdbac"];
const SKETCH_SIZES = [3, 7, 14, 28];

class Sketch {
  /**
   * @param {HTMLElement} host
   * @param {{width?:number,height?:number,guideImage?:string,foldGuide?:boolean,onChange?:Function}} opts
   */
  constructor(host, opts = {}) {
    this.w = opts.width || 800;
    this.h = opts.height || 600;
    this.color = SKETCH_COLORS[0];
    this.size = SKETCH_SIZES[1];
    this.tool = "pen";
    this.undoStack = [];
    this.dirty = false;
    this.onChange = opts.onChange || (() => {});

    host.innerHTML = "";
    const root = document.createElement("div");
    root.className = "sketch";
    root.innerHTML = `
      <div class="toolbar">
        <button type="button" class="tool sel" data-tool="pen" title="Pen" aria-label="Pen">✏️</button>
        <button type="button" class="tool" data-tool="eraser" title="Eraser" aria-label="Eraser">🧽</button>
        <button type="button" class="tool" data-tool="fill" title="Fill" aria-label="Fill bucket">🪣</button>
        <span class="sep"></span>
        ${SKETCH_SIZES.map((s, i) => `<button type="button" class="tool ${i === 1 ? "sel" : ""}" data-size="${s}" title="Size ${s}" aria-label="Brush size ${s}"><span class="size-dot" style="width:${Math.min(26, 4 + s * .8)}px;height:${Math.min(26, 4 + s * .8)}px"></span></button>`).join("")}
        <span class="sep"></span>
        <button type="button" class="tool" data-act="undo" title="Undo" aria-label="Undo">↩️</button>
        <button type="button" class="tool" data-act="clear" title="Clear" aria-label="Clear">🗑️</button>
      </div>
      <div class="toolbar swatches">
        ${SKETCH_COLORS.map((c, i) => `<button type="button" data-color="${c}" class="${i === 0 ? "sel" : ""}" style="background:${c}" aria-label="Colour ${c}"></button>`).join("")}
        <input type="color" value="#ff0000" title="Any colour" aria-label="Pick any colour">
      </div>
      <div class="over-fold" hidden><span>over the fold ↓ carry these lines on</span><canvas></canvas></div>
      <div class="canvas-wrap"><canvas class="main"></canvas></div>`;
    host.appendChild(root);
    this.root = root;

    this.canvas = root.querySelector("canvas.main");
    this.canvas.width = this.w;
    this.canvas.height = this.h;
    this.ctx = this.canvas.getContext("2d", { willReadFrequently: true });
    this.ctx.fillStyle = "#fff";
    this.ctx.fillRect(0, 0, this.w, this.h);
    this.ctx.lineCap = "round";
    this.ctx.lineJoin = "round";

    if (opts.foldGuide) {
      const g = document.createElement("div");
      g.className = "fold-guide";
      g.style.top = "88%";
      g.innerHTML = "<span>next player sees below this line</span>";
      root.querySelector(".canvas-wrap").appendChild(g);
    }
    if (opts.guideImage) this._showGuide(opts.guideImage);

    root.querySelector(".toolbar").addEventListener("click", e => {
      const b = e.target.closest("button");
      if (!b) return;
      if (b.dataset.tool) { this.tool = b.dataset.tool; this._sel("[data-tool]", b); }
      if (b.dataset.size) { this.size = +b.dataset.size; this._sel("[data-size]", b); if (this.tool === "fill") { this.tool = "pen"; this._sel("[data-tool]", root.querySelector('[data-tool="pen"]')); } }
      if (b.dataset.act === "undo") this.undo();
      if (b.dataset.act === "clear") this.clear();
    });
    root.querySelector(".swatches").addEventListener("click", e => {
      const b = e.target.closest("button[data-color]");
      if (!b) return;
      this.color = b.dataset.color;
      this._sel("[data-color]", b);
      if (this.tool === "eraser") { this.tool = "pen"; this._sel("[data-tool]", root.querySelector('[data-tool="pen"]')); }
    });
    root.querySelector('input[type=color]').addEventListener("input", e => {
      this.color = e.target.value;
      root.querySelectorAll("[data-color]").forEach(x => x.classList.remove("sel"));
    });

    this._bindPointer();
  }

  _sel(selector, btn) {
    this.root.querySelectorAll(selector).forEach(x => x.classList.toggle("sel", x === btn));
  }

  _showGuide(src) {
    const wrap = this.root.querySelector(".over-fold");
    const c = wrap.querySelector("canvas");
    const img = new Image();
    img.onload = () => {
      const strip = Math.round(img.height * 0.12);
      c.width = this.w;
      c.height = Math.round(this.w * strip / img.width);
      c.getContext("2d").drawImage(img, 0, img.height - strip, img.width, strip, 0, 0, c.width, c.height);
      wrap.hidden = false;
    };
    img.src = src;
  }

  _pos(e) {
    const r = this.canvas.getBoundingClientRect();
    return { x: (e.clientX - r.left) * this.w / r.width, y: (e.clientY - r.top) * this.h / r.height, p: e.pressure || .5 };
  }

  _bindPointer() {
    const c = this.canvas;
    let drawing = false, last = null, mid = null;

    const begin = e => {
      if (e.button > 0) return;
      e.preventDefault();
      c.setPointerCapture(e.pointerId);
      this._pushUndo();
      const pt = this._pos(e);
      if (this.tool === "fill") { this._fill(Math.floor(pt.x), Math.floor(pt.y)); this._changed(); return; }
      drawing = true; last = pt; mid = pt;
      this._dot(pt);
    };
    const move = e => {
      if (!drawing) return;
      e.preventDefault();
      const events = e.getCoalescedEvents ? e.getCoalescedEvents() : [e];
      for (const ev of events.length ? events : [e]) {
        const pt = this._pos(ev);
        const newMid = { x: (last.x + pt.x) / 2, y: (last.y + pt.y) / 2 };
        const ctx = this.ctx;
        ctx.globalCompositeOperation = "source-over";
        ctx.strokeStyle = this.tool === "eraser" ? "#ffffff" : this.color;
        const pressure = ev.pointerType === "pen" ? (0.4 + pt.p * 1.2) : 1;
        ctx.lineWidth = (this.tool === "eraser" ? this.size * 2 : this.size) * pressure;
        ctx.beginPath();
        ctx.moveTo(mid.x, mid.y);
        ctx.quadraticCurveTo(last.x, last.y, newMid.x, newMid.y);
        ctx.stroke();
        last = pt; mid = newMid;
      }
    };
    const end = () => { if (drawing) { drawing = false; this._changed(); } };
    c.addEventListener("pointerdown", begin);
    c.addEventListener("pointermove", move);
    c.addEventListener("pointerup", end);
    c.addEventListener("pointercancel", end);
    c.addEventListener("lostpointercapture", end);
  }

  _dot(pt) {
    const ctx = this.ctx;
    ctx.fillStyle = this.tool === "eraser" ? "#ffffff" : this.color;
    ctx.beginPath();
    ctx.arc(pt.x, pt.y, (this.tool === "eraser" ? this.size * 2 : this.size) / 2, 0, Math.PI * 2);
    ctx.fill();
  }

  _changed() { this.dirty = true; this.onChange(); }

  _pushUndo() {
    this.undoStack.push(this.ctx.getImageData(0, 0, this.w, this.h));
    if (this.undoStack.length > 25) this.undoStack.shift();
  }

  undo() {
    const img = this.undoStack.pop();
    if (img) { this.ctx.putImageData(img, 0, 0); this.dirty = this.undoStack.length > 0; this.onChange(); }
  }

  clear() {
    this._pushUndo();
    this.ctx.fillStyle = "#fff";
    this.ctx.fillRect(0, 0, this.w, this.h);
    this.dirty = false;
    this.onChange();
  }

  /** Scanline flood fill with a little tolerance so anti-aliased edges don't leave gaps. */
  _fill(x, y) {
    const { w, h } = this;
    if (x < 0 || y < 0 || x >= w || y >= h) return;
    const img = this.ctx.getImageData(0, 0, w, h);
    const d = new Uint32Array(img.data.buffer);
    const target = d[y * w + x];
    const hex = this.color.replace("#", "");
    const r = parseInt(hex.slice(0, 2), 16), g = parseInt(hex.slice(2, 4), 16), b = parseInt(hex.slice(4, 6), 16);
    const fill = (255 << 24) | (b << 16) | (g << 8) | r; // little-endian RGBA
    if (target === fill) return;
    const tr = target & 255, tg = (target >> 8) & 255, tb = (target >> 16) & 255;
    const tol = 48;
    const match = i => {
      const c = d[i];
      return Math.abs((c & 255) - tr) <= tol && Math.abs(((c >> 8) & 255) - tg) <= tol && Math.abs(((c >> 16) & 255) - tb) <= tol && c !== fill;
    };
    const stack = [[x, y]];
    while (stack.length) {
      let [cx, cy] = stack.pop();
      let i = cy * w + cx;
      while (cx >= 0 && match(i)) { cx--; i--; }
      cx++; i++;
      let up = false, down = false;
      while (cx < w && match(i)) {
        d[i] = fill;
        if (cy > 0) { const m = match(i - w); if (m && !up) { stack.push([cx, cy - 1]); up = true; } else if (!m) up = false; }
        if (cy < h - 1) { const m = match(i + w); if (m && !down) { stack.push([cx, cy + 1]); down = true; } else if (!m) down = false; }
        cx++; i++;
      }
    }
    this.ctx.putImageData(img, 0, 0);
  }

  /** Compressed image for upload (WebP where supported, else PNG/JPEG), kept under the server's size limit. */
  toDataUrl() {
    let url = this.canvas.toDataURL("image/webp", 0.85);
    if (!url.startsWith("data:image/webp")) url = this.canvas.toDataURL("image/png");
    if (url.length > 850000) url = this.canvas.toDataURL("image/jpeg", 0.75);
    if (url.length > 850000) url = this.canvas.toDataURL("image/jpeg", 0.5);
    return url;
  }
}

window.Sketch = Sketch;
