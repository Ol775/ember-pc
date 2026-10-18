/* Site motion: Lenis smooth scrolling, GSAP and ScrollTrigger effects, and a few small effects in the spirit of React Bits (spotlight cards, magnetic buttons, shiny text). Everything is optional: with reduced motion, no libraries or an old browser, the pages simply stay still. */
(function () {
  var still = matchMedia("(prefers-reduced-motion:reduce)").matches;
  if (still) return;
  var fine = matchMedia("(hover:hover) and (pointer:fine)").matches;
  var $$ = function (q) { return [].slice.call(document.querySelectorAll(q)); };
  var g = window.gsap, ST = window.ScrollTrigger;
  if (g && ST) g.registerPlugin(ST);

  // smooth scrolling, driven by GSAP's ticker so ScrollTrigger stays in step
  var lenis = null;
  if (window.Lenis) {
    lenis = new Lenis({ lerp: 0.11, anchors: true });
    document.documentElement.classList.add("lenis-on");
    if (g) { g.ticker.add(function (t) { lenis.raf(t * 1000); }); g.ticker.lagSmoothing(0); }
    else (function f(t) { lenis.raf(t); requestAnimationFrame(f); })(performance.now());
    if (ST) lenis.on("scroll", ST.update);
    $$(".log,pre,.tbl").forEach(function (e) { e.setAttribute("data-lenis-prevent", ""); });
  }

  // reading progress along the top edge
  var bar = document.createElement("div"); bar.className = "prog"; bar.setAttribute("aria-hidden", "true"); document.body.appendChild(bar);
  function prog() { var h = document.documentElement.scrollHeight - innerHeight; bar.style.transform = "scaleX(" + (h > 0 ? Math.min(1, scrollY / h) : 0) + ")"; }
  addEventListener("scroll", prog, { passive: true }); addEventListener("resize", prog); prog();

  // embers: a slow glow and sparks drifting up behind every page
  var em = document.createElement("div"); em.className = "emb"; em.setAttribute("aria-hidden", "true");
  var gl = document.createElement("span"); gl.className = "gl"; em.appendChild(gl);
  var n = innerWidth < 700 ? 10 : 18;
  for (var i = 0; i < n; i++) { var p = document.createElement("i"), st = p.style;
    st.setProperty("--x", (Math.random() * 100).toFixed(1) + "%"); st.setProperty("--s", (0.22 + Math.random() * 0.42).toFixed(2) + "rem");
    st.setProperty("--t", (13 + Math.random() * 14).toFixed(1) + "s"); st.setProperty("--d", (-Math.random() * 24).toFixed(1) + "s");
    st.setProperty("--dx", ((Math.random() - 0.5) * 12).toFixed(1) + "rem"); em.appendChild(p); }
  document.documentElement.classList.add("embers-on"); document.body.insertBefore(em, document.body.firstChild);

  // spotlight cards: a soft glow follows the pointer
  if (fine) $$(".bento .card,.tile,.step,.fact,.num").forEach(function (c) {
    var s = document.createElement("i"); s.className = "spl"; s.setAttribute("aria-hidden", "true");
    if (getComputedStyle(c).position === "static") c.style.position = "relative";
    c.insertBefore(s, c.firstChild);
    c.addEventListener("pointermove", function (e) { var r = c.getBoundingClientRect(); s.style.setProperty("--mx", e.clientX - r.left + "px"); s.style.setProperty("--my", e.clientY - r.top + "px"); });
  });

  // shiny headline on the closing call to action
  $$(".final h2").forEach(function (h) { h.classList.add("shiny"); });

  if (!g) return;

  // magnetic primary buttons
  if (fine) $$(".btn.pri").forEach(function (b) {
    var qx = g.quickTo(b, "x", { duration: 0.5, ease: "power3" }), qy = g.quickTo(b, "y", { duration: 0.5, ease: "power3" });
    b.addEventListener("pointermove", function (e) { var r = b.getBoundingClientRect(); qx((e.clientX - r.left - r.width / 2) * 0.22); qy((e.clientY - r.top - r.height / 2) * 0.3); });
    b.addEventListener("pointerleave", function () { qx(0); qy(0); });
  });

  if (!ST) return;

  // section headings arrive word by word
  $$(".head h2").forEach(function (h) {
    if (h.querySelector("*")) return;
    var words = h.textContent.trim().split(/\s+/); h.textContent = "";
    var spans = words.map(function (w, i) { var s = document.createElement("span"); s.className = "wd"; s.textContent = w; h.appendChild(s); if (i < words.length - 1) h.appendChild(document.createTextNode(" ")); return s; });
    g.from(spans, { yPercent: 70, opacity: 0, duration: 0.9, ease: "power3.out", stagger: 0.07, scrollTrigger: { trigger: h, start: "top 88%", once: true } });
  });

  // the first screen drifts away as you scroll
  var ems = document.querySelector(".intro .pxs");
  if (ems) g.to(ems, { y: -90, ease: "none", scrollTrigger: { trigger: ".intro", start: "top top", end: "bottom top", scrub: true } });
  var cue = document.querySelector(".intro .scroll");
  if (cue) g.to(cue, { opacity: 0, ease: "none", scrollTrigger: { trigger: ".intro", start: "top top", end: "40% top", scrub: true } });
  var hero = document.querySelector(".hero .win");
  if (hero) g.to(hero, { y: -40, ease: "none", scrollTrigger: { trigger: ".hero", start: "top top", end: "bottom top", scrub: true } });
})();
