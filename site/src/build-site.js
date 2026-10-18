// Builds the deployable site in <repo>/site from the mockup sources in this folder.
const fs = require('fs');
const path = require('path');
const d = __dirname + '/';
const OUT = path.resolve(__dirname, '../public') + '/';
const REPO = path.resolve(__dirname, '../..') + '/';
const read = f => fs.readFileSync(d + f, 'utf8').replace(/\r\n/g, '\n'); // CRLF would change the script hashes: browsers hash the text with LF

// 1. generate pages that come from templates
require('child_process').execSync('node build-integrations.js', { cwd: d, stdio: 'inherit' });
const { md } = require('./md.js');
const doc = f => fs.readFileSync(REPO + f, 'utf8');
const head = (h, lede) => `<p class="eyebrow">Docs</p>\n<h1>${h}</h1>\n<p class="lede">${lede}</p>\n`;
const WEBLIBS = `\n<h2 id="website">This website</h2>\n<p>The pages at emberpc.uk, not the Ember app, use these libraries. They are served from this site, not from a third party.</p>\n<ul>\n<li><b>GSAP</b> 3.15.0 and ScrollTrigger, by GreenSock, under the <a href="https://gsap.com/standard-license">GSAP Standard "no charge" licence</a>. Used for scroll and entrance animation.</li>\n<li><b>Lenis</b> 1.3.27, by Darkroom Engineering, MIT licence. Used for smooth scrolling.</li>\n<li><b>Integration icons</b> for Govee, Nanoleaf, WiZ, Yeelight and Meross come from <a href=\"https://github.com/Donnnno/Arcticons\">Arcticons</a> (CC BY-SA 4.0), which redraws app icons in one style. The OpenRGB icon is the one shipped with <a href=\"https://openrgb.org\">OpenRGB</a> (GPL-2.0). Brand names and logos belong to their owners; they are shown only to say what works with Ember.</li>\n<li><b>Geist</b> and <b>Geist Mono</b>, SIL Open Font License 1.1.</li>\n</ul>\n<p>A few small effects (spotlight cards, magnetic buttons, shiny text) are our own code in the style of <a href="https://reactbits.dev">React Bits</a>. No React Bits code is included.</p>\n`;
// a seven-blade fan, drawn once and reused wherever a page shows fans
const FANSYM = '<svg width="0" height="0" style="position:absolute" aria-hidden="true"><symbol id="fan" viewBox="0 0 100 100"><circle cx="50" cy="50" r="47" fill="none" stroke="currentColor" stroke-width="3" opacity=".55"/>' + [0,1,2,3,4,5,6].map(k => '<path d="M50 50C43 43 39 27 45 11C55 9 62 21 58 35C56 43 53 48 50 50Z" fill="currentColor" opacity=".92" transform="rotate(' + (k * 360 / 7).toFixed(2) + ' 50 50)"/>').join('') + '<circle cx="50" cy="50" r="11" fill="var(--bg2,#17100d)"/><circle cx="50" cy="50" r="5" fill="currentColor"/></symbol></svg>';
const PAGES = [
  ['privacy.html', 'Privacy and cookies · Ember PC', read('privacy.frag.html').trim() + '\n<h2 id="app">The Ember app</h2>\n' + md(doc('PRIVACY.md'), { dropH1: true, demote: 1 })],
  ['terms.html', 'Terms and legal notices · Ember PC', read('terms.frag.html').trim() + '\n<h2 id="app">The Ember app</h2>\n' + md(doc('TERMS.md'), { dropH1: true, demote: 1 })],
  ['changelog.html', 'Changelog · Ember PC', head('Changelog', 'Every Ember release, newest first.') + md(doc('CHANGELOG.md'), { dropH1: true })],
  ['roadmap.html', 'Roadmap · Ember PC', head('Roadmap', 'What has shipped and what is next.') + md(doc('ROADMAP.md'), { dropH1: true })],
  ['install.html', 'Install guide · Ember PC', head('Install guide', 'Requirements, installer options and updating.') + md(doc('docs/install.md'), { dropH1: true })],
  ['security.html', 'Security · Ember PC', head('Security', 'How Ember is locked down, and how to report a problem.') + md(doc('SECURITY.md'), { dropH1: true })],
  ['known-issues.html', 'Known issues · Ember PC', head('Known issues', 'Problems we know about and how to work around them.') + md(doc('docs/known-issues.md'), { dropH1: true })],
  ['licences.html', 'Open source licences · Ember PC', head('Open source licences', 'The third-party parts Ember is built on.') + '<h2 id="mit">Ember licence (MIT)</h2>\n<pre><code>' + doc('LICENSE').trim().replace(/&/g, '&amp;').replace(/</g, '&lt;') + '</code></pre>\n' + md(doc('THIRD-PARTY.md'), { dropH1: true }) + WEBLIBS],
];
for (const [file, title, body] of PAGES) fs.writeFileSync(d + file, read('legal.tpl.html').replace('{{TITLE}}', title).replace('{{BODY}}', () => body));

// 2. shared theme, motion and consent code
const theme = read('theme.js'), css = read('fx.css'), fx = read('fx.js'), consent = read('consent.js');
const legalCss = '.legal-links{display:flex;flex-wrap:wrap;gap:.5rem 1.5rem;margin-top:1rem;font-size:.8125rem}.legal-links a{color:var(--mute);text-underline-offset:.18em}.legal-links a:hover{color:var(--fg)}\n' +
  '.prose ul,.prose ol{margin:.8rem 0 0;padding-left:1.25rem;color:var(--mute)}.prose li{margin-top:.45rem;text-wrap:pretty}.prose li b{color:var(--fg)}.prose h3{font-size:1.0625rem;margin-top:1.75rem;letter-spacing:-.01em}.prose pre{margin-top:1rem;padding:1rem;overflow-x:auto;background:var(--bg2);border:1px solid var(--line);border-radius:1rem;font-size:.8125rem}.prose pre code{background:none;padding:0}.prose details{margin-top:1rem;border:1px solid var(--line);border-radius:1rem;padding:.75rem 1rem}.prose summary{cursor:pointer;color:var(--fg)}.prose h2[id],.prose h3[id]{scroll-margin-top:5rem}\n';

const DESC = {
  'index.html': 'Ember is a free, open source Windows app for PC temperatures, power draw, fan curves and RGB lighting. Local only, no telemetry.',
  'demo.html': 'Scroll through Ember\'s dashboard, fans, lights, games and power pages.',
  'integrations.html': 'The lights, smart plugs and game libraries Ember works with, and how each one connects.',
  'privacy.html': 'Privacy and cookies for emberpc.uk. No cookies, no ads, and cookieless visit counts you can switch off with Do Not Track.',
  'terms.html': 'Terms of use and legal notices for emberpc.uk and the Ember app.',
  'changelog.html': 'Every Ember release, newest first, with what was added, changed and fixed.',
  'roadmap.html': 'What Ember has shipped, what is being tested in beta, and what is planned next for the free Windows PC fan, lights and power app.',
  'install.html': 'Requirements, installer options and how to update Ember on Windows.',
  'security.html': 'How Ember is locked down and how to report a security problem.',
  'known-issues.html': 'Problems Ember knows about and how to work around them.',
  'licences.html': 'The third-party software Ember is built on and its licences.',
};
const TITLE = { 'index.html': 'Ember: PC fan, RGB lighting and power control for Windows', 'demo.html': 'Ember demo: see the dashboard, fans, lights and power pages', 'integrations.html': 'Ember integrations: RGB lights, smart plugs and game libraries' };
const LD = JSON.stringify({ '@context': 'https://schema.org', '@type': 'SoftwareApplication', name: 'Ember', url: 'https://emberpc.uk/', description: DESC['index.html'], applicationCategory: 'UtilitiesApplication', operatingSystem: 'Windows 10, Windows 11', offers: { '@type': 'Offer', price: '0', priceCurrency: 'GBP' }, isAccessibleForFree: true, codeRepository: 'https://github.com/Ol775/ember-pc', sameAs: ['https://github.com/Ol775/ember-pc'], image: 'https://emberpc.uk/img/og.png' });
// FAQPage data comes from the visible <details> blocks so the two cannot drift apart
const faqLd = html => JSON.stringify({ '@context': 'https://schema.org', '@type': 'FAQPage', mainEntity: [...html.matchAll(/<details><summary>([^<]*)<\/summary><p>([\s\S]*?)<\/p><\/details>/g)].map(m => ({ '@type': 'Question', name: m[1], acceptedAnswer: { '@type': 'Answer', text: m[2].replace(/<[^>]+>/g, '').replace(/&amp;/g, '&') } })) });
const SRC = { 'index.html': 'ember-site-mockup.html', 'demo.html': 'demo.html', 'integrations.html': 'integrations.html', 'privacy.html': 'privacy.html', 'terms.html': 'terms.html', 'changelog.html': 'changelog.html', 'roadmap.html': 'roadmap.html', 'install.html': 'install.html', 'security.html': 'security.html', 'known-issues.html': 'known-issues.html', 'licences.html': 'licences.html' };
const CANON = { 'index.html': '/', 'demo.html': '/demo', 'integrations.html': '/integrations', 'privacy.html': '/privacy', 'terms.html': '/terms', 'changelog.html': '/changelog', 'roadmap.html': '/roadmap', 'install.html': '/install', 'security.html': '/security', 'known-issues.html': '/known-issues', 'licences.html': '/licences' };

const HASHES = {};
// Inline style="" attributes are moved into one hashed stylesheet, so the CSP can refuse every inline style attribute (style-src-attr 'none').
// Each distinct value becomes a class; the selector is repeated so it keeps beating the single-class rules the attribute used to beat.
const STYLE_CLASS = new Map(), PAGES_OUT = {};
function externalize(html) {
  return html.split(/(<script[\s\S]*?<\/script>|<style[\s\S]*?<\/style>)/).map((part, i) => i % 2 ? part : part.replace(/<([a-zA-Z][\w:-]*)((?:\s+[^\s>"'=]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>"']+))?)*)\s*(\/?)>/g, (m, tag, attrs, slash) => {
    const sm = /\sstyle="([^"]*)"/.exec(attrs);
    if (!sm) return m;
    let cls = STYLE_CLASS.get(sm[1]);
    if (!cls) { cls = 's' + (STYLE_CLASS.size + 1); STYLE_CLASS.set(sm[1], cls); }
    attrs = attrs.replace(sm[0], '');
    attrs = /\sclass="/.test(attrs) ? attrs.replace(/(\sclass=")([^"]*)"/, (a, p, c) => p + c + ' ' + cls + '"') : attrs + ' class="' + cls + '"';
    return '<' + tag + attrs + slash + '>';
  })).join('');
}
fs.mkdirSync(OUT + 'fonts', { recursive: true });
fs.mkdirSync(OUT + 'img', { recursive: true });
fs.mkdirSync(OUT + 'js', { recursive: true });
for (const f of fs.readdirSync(d + 'vendor')) fs.copyFileSync(d + 'vendor/' + f, OUT + 'js/' + f);
fs.copyFileSync(d + 'icons/openrgb.png', OUT + 'img/openrgb.png');
const fansName = 'fans.' + require('crypto').createHash('sha256').update(read('fans.js')).digest('hex').slice(0, 10) + '.js';
for (const f of fs.readdirSync(OUT + 'js')) if (/^fans\..*\.js$/.test(f)) fs.unlinkSync(OUT + 'js/' + f);
fs.writeFileSync(OUT + 'js/' + fansName, read('fans.js'));
const motionName = 'motion.' + require('crypto').createHash('sha256').update(read('motion.js')).digest('hex').slice(0, 10) + '.js';
for (const f of fs.readdirSync(OUT + 'js')) if (/^motion\..*\.js$/.test(f)) fs.unlinkSync(OUT + 'js/' + f);
fs.writeFileSync(OUT + 'js/' + motionName, read('motion.js'));
const libs = fs.readdirSync(d + 'vendor').sort((a, b) => (/^gsap/.test(a) ? 0 : /^Scroll/.test(a) ? 1 : 2) - (/^gsap/.test(b) ? 0 : /^Scroll/.test(b) ? 1 : 2)).map(f => '<script src="/js/' + f + '" defer></script>\n').join('') + '<script src="/js/' + fansName + '" defer></script>\n<script src="/js/' + motionName + '" defer></script>\n';

for (const [out, src] of Object.entries(SRC)) {
  let s = read(src);
  // strip anything an earlier run injected
  s = s.replace(/<script id="ember-theme">[\s\S]*?<\/script>\s*/, '').replace(/<style id="ember-fx-css">[\s\S]*?<\/style>\s*/, '')
       .replace(/<script id="ember-fx">[\s\S]*?<\/script>\s*/, '').replace(/<script id="ember-consent">[\s\S]*?<\/script>\s*/, '');
  // fonts come from this site, not Google, so no visitor address is sent to a third party
  s = s.replace(/<link rel="preconnect"[^>]*>\s*/g, '').replace(/<link rel="stylesheet" href="https:\/\/fonts\.googleapis\.com[^>]*>\s*/g, '');
  s = s.replace(/<meta name="claude:viewport-fit-sides"[^>]*>\s*/g, '');
  // links between pages
  s = s.replace(/href="demo\.html"/g, 'href="/demo"').replace(/href="integrations\.html"/g, 'href="/integrations"')
       .replace(/href="\.\/#/g, 'href="/#').replace(/href="\.\/"/g, 'href="/"');
  const title = TITLE[out] || (/<title>([^<]*)<\/title>/.exec(s) || [, 'Ember'])[1];
  const meta = `<meta charset="utf-8">\n<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">\n<title>${title}</title>\n` +
    `<meta name="description" content="${DESC[out]}">\n<link rel="canonical" href="https://emberpc.uk${CANON[out]}">\n<link rel="icon" href="/favicon.ico" sizes="48x48">\n<link rel="icon" href="/favicon.svg" type="image/svg+xml">\n<link rel="apple-touch-icon" href="/apple-touch-icon.png">\n` +
    `<meta name="theme-color" content="#120d0b">\n<meta property="og:type" content="website"><meta property="og:site_name" content="Ember">\n` +
    `<meta property="og:title" content="${title}"><meta property="og:description" content="${DESC[out]}"><meta property="og:url" content="https://emberpc.uk${CANON[out]}">\n` +
    `<meta property="og:image" content="https://emberpc.uk/img/og.png"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630"><meta property="og:image:alt" content="Ember dashboard showing CPU and GPU temperatures, fan mode and fan speeds"><meta property="og:locale" content="en_GB">\n` +
    `<meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="${title}"><meta name="twitter:description" content="${DESC[out]}"><meta name="twitter:image" content="https://emberpc.uk/img/og.png">\n` +
    (out === 'index.html' ? `<script type="application/ld+json">${LD}</script>\n<script type="application/ld+json">${faqLd(s)}</script>\n` : '') +
    '' +
    `<link rel="preload" href="/fonts/geist-latin-400-normal.woff2" as="font" type="font/woff2" crossorigin>\n<link rel="stylesheet" href="/fonts/fonts.css">\n`;
  const inject = `<script id="ember-theme">\n${theme}</script>\n<style id="ember-fx-css">\n${css}${legalCss}</style>\n`;
  const tail = `<script id="ember-fx">\n${fx}</script>\n<script id="ember-consent">\n${consent}</script>\n${libs}`;
  const links = '<div class="legal-links"><a href="/changelog">Changelog</a><a href="/roadmap">Roadmap</a><a href="/install">Install guide</a><a href="/known-issues">Known issues</a><a href="https://github.com/Ol775/ember-pc/issues/new/choose">Report a bug</a><a href="/security">Security</a><a href="/licences">Open source licences</a><a href="/privacy">Privacy and cookies</a><a href="/terms">Terms and legal notices</a><a href="#" data-cookie-settings>Cookie settings</a></div>';

  if (!/<!doctype html>/i.test(s)) {
    // the main page is a fragment: split its head parts from its body at the end of its <style>
    s = s.replace(/<title>[^<]*<\/title>\s*/, '');
    const i = s.indexOf('</style>') + 8;
    s = `<!doctype html>\n<html lang="en">\n<head>\n${meta}${inject}${s.slice(0, i)}\n</head>\n<body>\n${s.slice(i)}\n${tail}</body>\n</html>\n`;
  } else {
    s = s.replace(/<meta charset="utf-8">\s*/, '').replace(/<meta name="viewport"[^>]*>\s*/, '').replace(/<title>[^<]*<\/title>\s*/, '');
    s = s.replace('<head>', '<head>\n' + meta + inject).replace('</body>', () => tail + '</body>');
  }
  // legal links in every footer
  s = s.replace(/<div class="legal-links">[\s\S]*?<\/div>/, '').replace('</footer>', () => links + '</footer>');
  if (!s.includes('legal-links"')) throw new Error('no footer in ' + out);
  s = s.replace(/<body[^>]*>/, m => m + '\n' + FANSYM + '\n<a class="skip" href="#main">Skip to content</a>');
  if (/<header[ >]/.test(s)) s = s.replace(/<header(?![^>]*\bid=)/, '<header id="main" tabindex="-1"'); else s = s.replace(/<main(?![^>]*\bid=)/, '<main id="main" tabindex="-1"');
  if (!s.includes('id="main"')) s = s.replace('href="#main"', 'href="#top"'); // the home page's first block already has its own id
  s = externalize(s);
  PAGES_OUT[out] = s;
  // content security policy: every inline script and style block is allowed by its hash, nothing else inline
  const hash = t => "'sha256-" + require('crypto').createHash('sha256').update(t, 'utf8').digest('base64') + "'";
  const sc = [...s.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/g)].map(m => hash(m[1]));
  const sty = [...s.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map(m => hash(m[1]));
  HASHES[out] = { sc, sty };
  console.log('wrote', out, s.length);
}

// the generated stylesheet, named by its content so it can be cached for a year
fs.mkdirSync(OUT + 'css', { recursive: true });
const inlineCss = '/* Inline styles moved out of the pages so the CSP can refuse style attributes. Generated by build-site.js. */\n' + [...STYLE_CLASS].map(([v, c]) => 'html .' + c + '.' + c + '.' + c + '{' + v + '}').join('\n') + '\n';
const inlineName = 'inline.' + require('crypto').createHash('sha256').update(inlineCss).digest('hex').slice(0, 10) + '.css';
for (const f of fs.readdirSync(OUT + 'css')) fs.unlinkSync(OUT + 'css/' + f);
fs.writeFileSync(OUT + 'css/' + inlineName, inlineCss);
for (const [out, html] of Object.entries(PAGES_OUT)) fs.writeFileSync(OUT + out, html.replace('<link rel="stylesheet" href="/fonts/fonts.css">', m => m + '\n<link rel="stylesheet" href="/css/' + inlineName + '">'));
console.log('inline styles ->', STYLE_CLASS.size, 'classes in', inlineName);

// 3. fonts, favicon, headers, robots, sitemap
const fonts = [['geist', 400], ['geist', 500], ['geist', 600], ['geist', 700], ['geist-mono', 400], ['geist-mono', 500]];
let fcss = '/* Geist and Geist Mono, SIL Open Font License 1.1 (see OFL.txt). Served from this site. */\n';
for (const [fam, w] of fonts) {
  const f = `${fam}-latin-${w}-normal.woff2`;
  fs.copyFileSync(d + 'fonts-dl/' + f, OUT + 'fonts/' + f);
  fcss += `@font-face{font-family:"${fam === 'geist' ? 'Geist' : 'Geist Mono'}";font-style:normal;font-weight:${w};font-display:swap;src:url(/fonts/${f}) format("woff2")}\n`;
}
fs.writeFileSync(OUT + 'fonts/fonts.css', fcss);
fs.copyFileSync(REPO + 'docs/images/logo.svg', OUT + 'favicon.svg');
fs.copyFileSync(d + 'favicon.ico', OUT + 'favicon.ico'); // 16, 32 and 48 px, rendered from the logo, for browsers and crawlers that ask for /favicon.ico
fs.copyFileSync(d + 'apple-touch-icon.png', OUT + 'apple-touch-icon.png');
// no CSP on the catch-all rule: matching rules are merged, and two policies would both apply
const csp = h => ["default-src 'self'", "script-src 'self' https://static.cloudflareinsights.com " + h.sc.join(' '), "style-src 'self'", "style-src-elem 'self' " + h.sty.join(' '), "style-src-attr 'none'",
  "img-src 'self' data:", "font-src 'self'", "connect-src 'self' https://cloudflareinsights.com", "object-src 'none'", "base-uri 'self'", "form-action 'none'", "frame-ancestors 'none'", 'upgrade-insecure-requests'].join('; ');
let hdr = [
  '/*',
  '  X-Content-Type-Options: nosniff',
  '  Referrer-Policy: strict-origin-when-cross-origin',
  '  Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()',
  '  Strict-Transport-Security: max-age=31536000; includeSubDomains; preload',
  '  Cross-Origin-Opener-Policy: same-origin',
  '  Cross-Origin-Resource-Policy: same-origin',
  '  X-Frame-Options: DENY',
  '',
  ''].join('\n');
for (const [out, h] of Object.entries(HASHES)) {
  const paths = out === 'index.html' ? ['/', '/index.html'] : ['/' + out.replace('.html', ''), '/' + out];
  for (const p of paths) hdr += p + '\n  Content-Security-Policy: ' + csp(h) + '\n\n';
}
hdr += '/fonts/*\n  Cache-Control: public, max-age=31536000, immutable\n\n/css/*\n  Cache-Control: public, max-age=31536000, immutable\n\n/js/*\n  Cache-Control: public, max-age=31536000, immutable\n';
fs.writeFileSync(OUT + '_headers', hdr);
fs.mkdirSync(OUT + '.well-known', { recursive: true });
fs.writeFileSync(OUT + '.well-known/security.txt', ['Contact: https://github.com/Ol775/ember-pc/security/advisories/new', 'Expires: ' + new Date(Date.now() + 330 * 864e5).toISOString().replace(/\.\d+Z$/, '.000Z'), 'Preferred-Languages: en', 'Canonical: https://emberpc.uk/.well-known/security.txt', 'Policy: https://emberpc.uk/security', ''].join('\n'));
fs.writeFileSync(OUT + 'robots.txt', 'User-agent: *\nAllow: /\nSitemap: https://emberpc.uk/sitemap.xml\n');
fs.writeFileSync(OUT + 'sitemap.xml', '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
  Object.values(CANON).map(u => `  <url><loc>https://emberpc.uk${u}</loc><lastmod>${new Date().toISOString().slice(0, 10)}</lastmod></url>`).join('\n') + '\n</urlset>\n');
console.log('done ->', OUT);
