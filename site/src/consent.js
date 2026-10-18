(function(){
  // The site sets no cookies and runs no ads; visit counts use Cloudflare Web Analytics, which stores nothing on the device. The only optional storage is the visitor's own look settings.
  // Cloudflare Web Analytics counts visits on the live site, and is skipped when the browser sends Do Not Track or Global Privacy Control.
  if(navigator.doNotTrack!=="1"&&!navigator.globalPrivacyControl&&location.hostname==="emberpc.uk"){var cf=document.createElement("script");cf.defer=true;cf.src="https://static.cloudflareinsights.com/beacon.min.js";cf.setAttribute("data-cf-beacon",'{"token":"371f2343903a48ae8068264cb793c71a"}');document.head.appendChild(cf)}
  var C=window.emberConsent;if(!C)return;
  var b=document.createElement("div");b.className="cb";b.setAttribute("role","dialog");b.setAttribute("aria-labelledby","cbt");b.setAttribute("aria-live","polite");
  b.innerHTML='<h2 id="cbt">Your settings, your device</h2>'+
    '<p>This site sets no cookies and has no ads. It counts visits with Cloudflare Web Analytics, which stores nothing on your device and is skipped if your browser sends Do Not Track. If you personalise the look, we can remember your choices on your device so they are still there next time. That is optional.</p>'+
    '<p><a href="/privacy">Privacy and cookies</a></p>'+
    '<div class="row"><button type="button" data-v="no">No thanks</button><button type="button" data-v="yes">Remember my choices</button></div>';
  document.body.appendChild(b);
  function show(on){b.classList.toggle("on",on);if(on)b.querySelector("button").focus({preventScroll:true})}
  b.addEventListener("click",function(e){var v=e.target.getAttribute&&e.target.getAttribute("data-v");if(!v)return;C.set(v);show(false)});
  document.addEventListener("keydown",function(e){if(e.key==="Escape"&&b.classList.contains("on"))show(false)});
  document.addEventListener("click",function(e){var a=e.target.closest&&e.target.closest("[data-cookie-settings]");if(a){e.preventDefault();show(true)}});
  if(!C.get())setTimeout(function(){show(true)},700);
})();
