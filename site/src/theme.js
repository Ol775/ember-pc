(function(){
  // Site-wide look: theme, tint and accent chosen on the page are kept for every page of the site.
  var K="ember-site-theme",CK="ember-consent",mem=null,root=document.documentElement,mq=matchMedia("(prefers-color-scheme:dark)"),
      orig=root.getAttribute("data-theme"),def={mode:"system",tint:"warm",accent:null},touched=false;
  function consent(){try{return localStorage.getItem(CK)}catch(e){return null}}
  // choices persist on this device only after the visitor agrees; until then they last for the page view
  var MODES=["system","dark","oled","light"],TINTS=["warm","neutral","cool"];
  function clean(v){v=v&&typeof v==="object"?v:{};var o={};if(MODES.indexOf(v.mode)>-1)o.mode=v.mode;if(TINTS.indexOf(v.tint)>-1)o.tint=v.tint;o.accent=typeof v.accent==="string"&&/^#[0-9a-f]{6}$/i.test(v.accent)?v.accent:null;return o}
  function load(){var base=Object.assign({},def);try{if(consent()==="yes"){var v=JSON.parse(localStorage.getItem(K));if(v)return Object.assign(base,clean(v))}}catch(e){}return Object.assign(base,clean(mem))}
  function save(s){mem=s;try{if(consent()==="yes")localStorage.setItem(K,JSON.stringify(s))}catch(e){}}
  var H={warm:20,neutral:0,cool:215},S={warm:28,neutral:3,cool:26};
  var PAL=["--bg","--bg2","--card","--fg","--mute","--line","--glass"],ACC=["--ember","--ember-hi","--glow","--on-ember"];
  function lum(hex){var m=/^#?([0-9a-f]{6})$/i.exec(hex);if(!m)return 0;var n=parseInt(m[1],16);return(.2126*(n>>16)+.7152*(n>>8&255)+.0722*(n&255))/255}
  function apply(s){
    var isDef=s.mode===def.mode&&s.tint===def.tint&&!s.accent;
    if(isDef&&!touched)return;
    touched=true;
    if(root.hasAttribute("data-accent-only")){accent(s);return}
    var dark=s.mode==="dark"||s.mode==="oled"||(s.mode==="system"&&(orig?orig==="dark":mq.matches));
    if(s.mode==="system"){orig?root.setAttribute("data-theme",orig):root.removeAttribute("data-theme")}
    else root.setAttribute("data-theme",dark?"dark":"light");
    var st=root.style,h=H[s.tint]||20,sat=S[s.tint]||28,oled=s.mode==="oled";
    if(s.tint==="warm"&&!oled){PAL.forEach(function(v){st.removeProperty(v)})}
    else{
      var bg,bg2,card;
      if(dark){bg=oled?"#000":"hsl("+h+" "+sat+"% 6%)";bg2=oled?"#0d0d0d":"hsl("+h+" "+sat+"% 9%)";card=oled?"#121212":"hsl("+h+" "+sat+"% 11%)";
        st.setProperty("--fg","hsl("+h+" "+Math.min(55,sat*2)+"% 95%)");st.setProperty("--mute","hsl("+h+" "+Math.min(18,sat)+"% 66%)");st.setProperty("--line","hsl("+h+" 40% 85% / .12)")}
      else{bg="hsl("+h+" "+Math.round(sat*.9)+"% 95%)";bg2="hsl("+h+" "+Math.round(sat*.8)+"% 91%)";card="#fff";
        st.setProperty("--fg","hsl("+h+" "+(sat+10)+"% 10%)");st.setProperty("--mute","hsl("+h+" 10% 40%)");st.setProperty("--line","hsl("+h+" 40% 15% / .12)")}
      st.setProperty("--bg",bg);st.setProperty("--bg2",bg2);st.setProperty("--card",card);
      st.setProperty("--glass",bg.charAt(0)==="#"?"rgba(0,0,0,.72)":bg.replace(")"," / .72)"));
    }
    accent(s);
  }
  function accent(s){var st=root.style;
    if(s.accent){var a=s.accent;
      st.setProperty("--ember",a);st.setProperty("--ember-hi","color-mix(in srgb,"+a+" 62%,#fff)");
      st.setProperty("--glow","color-mix(in srgb,"+a+" 24%,transparent)");st.setProperty("--on-ember",lum(a)>.62?"#1a0c06":"#fff");
    }else ACC.forEach(function(v){st.removeProperty(v)});
  }
  var api=window.emberTheme={def:def,get:load,
    set:function(p){var s=Object.assign(load(),clean(Object.assign({},load(),p)));save(s);apply(s);return s},
    reset:function(){save(Object.assign({},def));apply(Object.assign({},def));return Object.assign({},def)}};
  window.emberConsent={get:consent,set:function(v){try{localStorage.setItem(CK,v)}catch(e){}
    if(v==="yes")save(load());else{try{localStorage.removeItem(K)}catch(e){}}
    document.dispatchEvent(new Event("ember-consent"))}};
  mq.addEventListener&&mq.addEventListener("change",function(){var s=load();if(s.mode==="system")apply(s)});
  apply(load());
})();
