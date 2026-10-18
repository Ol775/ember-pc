/* Spinning fans. Pages register an <svg class="fan"> with a function that returns its speed in degrees a second; each fan eases up and down to that speed, and only fans on screen are drawn. Pages that load first can push onto window.__fans. */
(function () {
  var still = matchMedia("(prefers-reduced-motion:reduce)").matches, items = [], running = false, last = 0;
  var io = "IntersectionObserver" in window ? new IntersectionObserver(function (es) { es.forEach(function (e) { e.target.__fi.on = e.isIntersecting; }); start(); }) : null;
  function add(el, get) {
    var it = { el: el, get: get, a: Math.random() * 360, v: 0, on: !io };
    el.__fi = it; items.push(it);
    if (io) io.observe(el);
    if (still) el.style.transform = "rotate(" + it.a.toFixed(0) + "deg)"; else start();
  }
  function frame(t) {
    var dt = Math.min(0.05, (t - last) / 1000 || 0.016); last = t;
    var any = false;
    items.forEach(function (it) {
      if (!it.on) return; any = true;
      var tgt = Math.max(0, it.get()) || 0;
      it.v += (tgt - it.v) * Math.min(1, dt * 2.2);
      if (it.v < 0.5 && tgt === 0) it.v = 0;
      it.a = (it.a + it.v * dt) % 360;
      it.el.style.transform = "rotate(" + it.a.toFixed(1) + "deg)";
    });
    if (any && !document.hidden) requestAnimationFrame(frame); else running = false;
  }
  function start() { if (still || running) return; running = true; last = performance.now(); requestAnimationFrame(frame); }
  document.addEventListener("visibilitychange", start);
  var q = window.__fans || [];
  window.__fans = { push: function (x) { add(x[0], x[1]); } };
  q.forEach(function (x) { add(x[0], x[1]); });
})();
