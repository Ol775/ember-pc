(function(){
  var still=matchMedia("(prefers-reduced-motion:reduce)").matches;
  var $$=function(q,r){return [].slice.call((r||document).querySelectorAll(q))};
  // nav picks up depth once you scroll
  var nav=document.querySelector("nav");
  function sc(){nav&&nav.classList.toggle("scrolled",scrollY>8)}
  addEventListener("scroll",sc,{passive:true});sc();
  var lk=nav&&nav.querySelector(".links");
  if(lk){var mb=document.createElement("button");mb.type="button";mb.className="navbtn";mb.setAttribute("aria-label","Menu");mb.setAttribute("aria-expanded","false");mb.innerHTML="<span class=\"nb\"></span>";
    nav.querySelector(".bar").insertBefore(mb,lk.nextSibling);
    var tog=function(o){nav.classList.toggle("open",o);mb.setAttribute("aria-expanded",o)};
    mb.addEventListener("click",function(){tog(!nav.classList.contains("open"))});
    lk.addEventListener("click",function(e){if(e.target.closest("a"))tog(false)});
    document.addEventListener("keydown",function(e){if(e.key==="Escape")tog(false)});
    document.addEventListener("click",function(e){if(!nav.contains(e.target))tog(false)})}
  if(still)return;

  // entrance for the first screen, in reading order
  var first=$$("header .eyebrow,header h1,header .tagline,header .lede,header .cta,header .pills,header .stats,header .win,.intro>*");
  first.forEach(function(e,i){e.style.setProperty("--d",i);e.classList.add("fx-in")});

  // scroll reveal for anything below the fold; what is already on screen stays put
  var skip=".device,.win,.stage,.pin,.stop .card,.grid .card,.tile,.fx-in";
  var sel=".head,.about,.pr>div,.card,.step,.fact,.build,.node,.panel,.num,.final,details,.shipped,.np,.led,.facts,.flow,.ww-note,.note";
  var els=$$(sel).filter(function(e){return !e.closest(skip)&&!e.querySelector(".fx-in")});
  // a parent that is revealed already covers its children
  els=els.filter(function(e){return !els.some(function(p){return p!==e&&p.contains(e)&&["about","facts","flow","rm","led"].some(function(c){return p.classList.contains(c)})})});
  var vh=innerHeight;
  var below=els.filter(function(e){return e.getBoundingClientRect().top>vh*.92});
  if("IntersectionObserver" in window){
    below.forEach(function(e){e.classList.add("fx-hide","fx-rv")});
    var batch=0,t;
    var io=new IntersectionObserver(function(es){
      es.filter(function(x){return x.isIntersecting}).forEach(function(x,i){
        x.target.style.transitionDelay=Math.min(i,6)*70+"ms";x.target.classList.add("fx-show");io.unobserve(x.target)});
    },{rootMargin:"0px 0px -8% 0px",threshold:.08});
    below.forEach(function(e){io.observe(e)});
  }

  // numbers count up when they come into view
  var nums=$$(".fact b,.num b,.stats b").filter(function(e){return /^\d+/.test(e.textContent)});
  nums.forEach(function(e){
    var m=/^(\d+)(.*)$/.exec(e.textContent.trim()),to=+m[1],tail=m[2];if(!to)return;
    e.textContent=0+tail;
    var go=function(){var t0=performance.now();(function f(t){var k=Math.min(1,(t-t0)/1100),v=1-Math.pow(1-k,3);e.textContent=Math.round(to*v)+tail;if(k<1)requestAnimationFrame(f)})(t0)};
    if("IntersectionObserver" in window){var o=new IntersectionObserver(function(es){if(es[0].isIntersecting){o.disconnect();go()}},{threshold:.6});o.observe(e)}else e.textContent=to+tail;
  });

  // hero rings and chart fill in
  $$(".win .ring").forEach(function(r,i){var p=+getComputedStyle(r).getPropertyValue("--p")||0,t0=performance.now()+250+i*120;
    r.style.setProperty("--p",0);(function f(t){var k=Math.max(0,Math.min(1,(t-t0)/1200)),v=1-Math.pow(1-k,3);r.style.setProperty("--p",p*v);if(k<1)requestAnimationFrame(f)})(performance.now())});
  $$(".win .chart path[fill=none]").forEach(function(p,i){p.setAttribute("pathLength","1");p.classList.add("fx-draw");p.style.animationDelay=.5+i*.25+"s"});
  $$(".split i").forEach(function(b,i){b.classList.add("fx-grow");b.style.animationDelay=i*.15+"s"});
  // draw the fan curve when a card scrolls in
  var cl=document.getElementById("cline");
  if(cl&&"IntersectionObserver" in window){var oc=new IntersectionObserver(function(es){if(es[0].isIntersecting){oc.disconnect();cl.setAttribute("pathLength","1");cl.classList.add("fx-draw")}},{threshold:.4});oc.observe(cl)}
})();
