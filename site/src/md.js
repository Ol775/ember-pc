// Small Markdown to HTML converter for the project's own docs: headings, lists, fenced code, paragraphs,
// inline code, bold, italic and links, with raw HTML lines passed through. Not a general Markdown parser.
const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

// where relative links in the docs land on the site; anything else relative is shown as plain text
const SITE = { 'CHANGELOG.md': '/changelog', 'ROADMAP.md': '/roadmap', 'PRIVACY.md': '/privacy#app', 'TERMS.md': '/terms#app',
  'SECURITY.md': '/security', 'install.md': '/install', 'known-issues.md': '/known-issues', 'THIRD-PARTY.md': '/licences', 'LICENSE': '/licences#mit' };

function inline(t) {
  const codes = [];
  t = esc(t).replace(/`([^`]+)`/g, (_, c) => '\u0000' + (codes.push('<code>' + c + '</code>') - 1) + '\u0000');
  t = t.replace(/&lt;(https?:[^&\s]+)&gt;/g, '<a href="$1">$1</a>');
  t = t.replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (_, text, url) => {
    url = url.replace(/"/g, '%22');
    if (url.endsWith('/blob/main/LICENSE')) url = '/licences#mit';
    if (/^https?:/.test(url)) return `<a href="${url}">${text}</a>`;
    if (url.startsWith('#')) return `<a href="${url}">${text}</a>`;
    const m = SITE[url.replace(/^(\.\.\/|\.\/|docs\/)+/, '').split('#')[0]];
    return m ? `<a href="${m}">${text}</a>` : text;
  });
  t = t.replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>').replace(/(^|[\s(])\*([^*\s][^*]*)\*/g, '$1<i>$2</i>');
  return t.replace(/\u0000(\d+)\u0000/g, (_, i) => codes[i]);
}

const slug = t => t.toLowerCase().replace(/<[^>]+>/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

// opts.dropH1: leave out the first "# Title"; opts.demote: shift headings down by this many levels
function md(src, opts = {}) {
  const out = [], lines = src.replace(/\r/g, '').split('\n');
  let para = [], list = null, code = null, seenH1 = false;
  const seen = {}; // headings that repeat (Fixed, Added, Changed per release) get a numbered id so every id is unique
  const flushP = () => { if (para.length) out.push('<p>' + inline(para.join(' ')) + '</p>'); para = []; };
  const flushL = () => { if (list) out.push(`</${list}>`); list = null; };
  for (const line of lines) {
    if (code) { if (/^```/.test(line)) { out.push('<pre><code>' + esc(code.join('\n')) + '</code></pre>'); code = null; } else code.push(line); continue; }
    if (/^```/.test(line)) { flushP(); flushL(); code = []; continue; }
    if (/← All docs/.test(line)) continue;
    let m;
    if ((m = /^(#{1,3}) (.*)/.exec(line))) {
      flushP(); flushL();
      if (m[1].length === 1 && opts.dropH1 && !seenH1) { seenH1 = true; continue; }
      const lvl = Math.min(m[1].length + (opts.demote || 0), 4), base = slug(m[2]); seen[base] = (seen[base] || 0) + 1; const id = seen[base] > 1 ? base + "-" + seen[base] : base;
      out.push(`<h${lvl} id="${id}">${inline(m[2])}</h${lvl}>`); continue;
    }
    if ((m = /^[-*] (.*)/.exec(line))) { flushP(); if (list !== 'ul') { flushL(); out.push('<ul>'); list = 'ul'; } out.push('<li>' + inline(m[1]) + '</li>'); continue; }
    if ((m = /^\d+\. (.*)/.exec(line))) { flushP(); if (list !== 'ol') { flushL(); out.push('<ol>'); list = 'ol'; } out.push('<li>' + inline(m[1]) + '</li>'); continue; }
    if (/^</.test(line)) { flushP(); flushL(); out.push(line); continue; }
    if (!line.trim()) { flushP(); flushL(); continue; }
    flushL(); para.push(line.trim());
  }
  flushP(); flushL();
  return out.join('\n');
}

module.exports = { md, inline };
