// Records the KVBind sample-app overview demo.
// Usage: node record.mjs <outDir>   (UI on :5200, API on :5101; see README.md)
// Frames come from Chrome's screencast (crisp, 1.5x); build.sh turns them into an mp4.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const UI = process.env.UI ?? 'http://localhost:5200';
const API = process.env.API ?? 'http://localhost:5101/api/models';
const out = process.argv[2] ?? 'out';
const framesDir = path.join(out, 'frames');
fs.rmSync(framesDir, { recursive: true, force: true }); // frames of an earlier run would linger
fs.mkdirSync(framesDir, { recursive: true });

const appNo = `UW-${4000 + Math.floor(Math.random() * 5000)}`;

// ── page helpers injected into every document: a visible cursor, captions and title cards ──
const helpers = () => {
  const ready = () => {
    if (document.getElementById('__demo-css')) return;
    const css = document.createElement('style');
    css.id = '__demo-css';
    css.textContent = `
      #__cursor{position:fixed;z-index:2147483647;left:0;top:0;width:22px;height:22px;pointer-events:none;
        transform:translate(-3px,-2px);transition:transform .05s}
      #__cursor svg{filter:drop-shadow(0 2px 3px rgba(0,0,0,.35))}
      .__ripple{position:fixed;z-index:2147483646;width:34px;height:34px;margin:-17px 0 0 -17px;border-radius:50%;
        border:3px solid #7592ff;pointer-events:none;animation:__rip .5s ease-out forwards}
      @keyframes __rip{from{transform:scale(.3);opacity:1}to{transform:scale(1.4);opacity:0}}
      #__caption{position:fixed;z-index:2147483645;left:50%;bottom:78px;transform:translate(-50%,12px);opacity:0;
        transition:opacity .35s,transform .35s;pointer-events:none;max-width:860px;text-align:center;
        background:rgba(17,24,39,.94);color:#fff;border:1px solid rgba(117,146,255,.45);border-radius:14px;padding:12px 22px;
        font:600 17px/1.35 Outfit,system-ui,sans-serif;box-shadow:0 10px 30px rgba(0,0,0,.25)}
      #__caption.on{opacity:1;transform:translate(-50%,0)}
      #__caption.top{bottom:auto;top:84px}
      #__caption small{display:block;margin-top:3px;font-weight:400;font-size:14px;color:#c7d2fe}
      #__card{position:fixed;z-index:2147483646;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;
        background:radial-gradient(1200px 600px at 50% 40%,#2a3a9e 0%,#111827 70%);color:#fff;opacity:0;transition:opacity .5s;
        font-family:Outfit,system-ui,sans-serif;pointer-events:none}
      [data-operations]{display:none!important} /* the overview video leaves custom operations out */
      #__card.on{opacity:1}
      #__card svg{animation:__float 2.4s ease-in-out infinite}
      @keyframes __float{50%{transform:translateY(-7px)}}
      #__card h1{font-size:64px;font-weight:700;margin:22px 0 8px;letter-spacing:-.5px}
      #__card p{font-size:24px;color:#c7d2fe;margin:0;max-width:900px;text-align:center}
      #__card .u{margin-top:30px;font-size:20px;color:#7592ff;font-weight:600}`;
    document.head.appendChild(css);
    const c = document.createElement('div');
    c.id = '__cursor';
    c.innerHTML = `<svg width="22" height="22" viewBox="0 0 24 24"><path d="M3 2l7.5 19 2.6-7.9L21 10.5z" fill="#fff" stroke="#111827" stroke-width="1.6" stroke-linejoin="round"/></svg>`;
    const pos = window.__cursorPos ?? { x: -50, y: -50 };
    c.style.left = pos.x + 'px'; c.style.top = pos.y + 'px';
    document.body.appendChild(c);
  };
  document.addEventListener('mousemove', (e) => {
    window.__cursorPos = { x: e.clientX, y: e.clientY };
    const c = document.getElementById('__cursor');
    if (c) { c.style.left = e.clientX + 'px'; c.style.top = e.clientY + 'px'; }
  }, true);
  document.addEventListener('mousedown', (e) => {
    const r = document.createElement('div'); r.className = '__ripple';
    r.style.left = e.clientX + 'px'; r.style.top = e.clientY + 'px';
    document.body.appendChild(r); setTimeout(() => r.remove(), 600);
  }, true);
  window.__caption = (text, sub, top) => {
    ready();
    let el = document.getElementById('__caption');
    if (!el) { el = document.createElement('div'); el.id = '__caption'; document.body.appendChild(el); }
    if (!text) { el.classList.remove('on'); return; }
    el.innerHTML = text + (sub ? `<small>${sub}</small>` : '');
    el.classList.toggle('top', !!top);
    requestAnimationFrame(() => el.classList.add('on'));
  };
  window.__card = (title, sub, url) => {
    ready();
    let el = document.getElementById('__card');
    if (!el) { el = document.createElement('div'); el.id = '__card'; document.body.appendChild(el); }
    const cur = document.getElementById('__cursor');
    if (cur) cur.style.visibility = title ? 'hidden' : 'visible';
    if (!title) { el.classList.remove('on'); return; }
    el.innerHTML = `<svg width="96" height="96" viewBox="0 0 64 64"><g stroke="#7592ff" stroke-width="4" stroke-linecap="round"><line x1="32" y1="16" x2="16" y2="46"/><line x1="32" y1="16" x2="48" y2="46"/></g><circle cx="32" cy="16" r="9" fill="#465fff"/><circle cx="16" cy="46" r="7" fill="#7592ff"/><circle cx="48" cy="46" r="7" fill="#7592ff"/></svg>
      <h1>${title}</h1><p>${sub ?? ''}</p>${url ? `<div class="u">${url}</div>` : ''}`;
    requestAnimationFrame(() => el.classList.add('on'));
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready); else ready();
};

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM ?? '/usr/bin/chromium', args: ['--no-sandbox', '--hide-scrollbars'] });
const context = await browser.newContext({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1.5 });
await context.addInitScript(helpers);
const p = await context.newPage();
p.setDefaultTimeout(15000);

// ── screencast capture ──
const frames = [];
const cdp = await context.newCDPSession(p);
cdp.on('Page.screencastFrame', async ({ data, metadata, sessionId }) => {
  const file = path.join(framesDir, `f${String(frames.length).padStart(6, '0')}.jpg`);
  fs.writeFileSync(file, Buffer.from(data, 'base64'));
  frames.push({ file: path.basename(file), t: metadata.timestamp });
  await cdp.send('Page.screencastFrameAck', { sessionId }).catch(() => {});
});
const startCast = () => cdp.send('Page.startScreencast', { format: 'jpeg', quality: 92, maxWidth: 1920, maxHeight: 1200, everyNthFrame: 1 });

// ── interaction helpers ──
const PACE = Number(process.env.PACE ?? 0.82);
const wait = (ms) => p.waitForTimeout(Math.round(ms * PACE));
const caption = async (text, sub, top = false) => { await p.evaluate(([t, s, tp]) => window.__caption(t, s, tp), [text, sub, top]); };
const card = async (title, sub, url) => { await p.evaluate(([t, s, u]) => window.__card(t, s, u), [title, sub, url]); };
let mouse = { x: 640, y: 400 };
const moveTo = async (loc, dx = 0.5, dy = 0.5) => {
  await loc.scrollIntoViewIfNeeded();
  const box = await loc.boundingBox();
  const x = box.x + box.width * dx, y = box.y + box.height * dy;
  const dist = Math.hypot(x - mouse.x, y - mouse.y);
  await p.mouse.move(x, y, { steps: Math.max(8, Math.min(40, Math.round(dist / 18))) });
  mouse = { x, y };
  await wait(120);
};
const click = async (loc, pause = 500) => { await moveTo(loc); await p.mouse.down(); await p.mouse.up(); await wait(pause); };
const type = async (loc, text, pause = 300) => {
  await click(loc, 150);
  await loc.press('Control+a');
  await loc.pressSequentially(text, { delay: 30 });
  await wait(pause);
};
// "Basics/X" matches exactly; a bare key ("Name") matches the field inside the current collection item.
const field = (fieldPath) => p.locator(fieldPath.includes('/')
  ? `[data-path="${fieldPath}"]` : `[data-path="${fieldPath}"], [data-path$="/${fieldPath}"]`).first();
const input = (fieldPath) => field(fieldPath).locator('input, textarea').first();
const cardBtn = (title) => p.locator('button[type=button]', { has: p.locator('h3', { hasText: title }) }).first();
const btn = (text) => p.getByRole('button', { name: text, exact: true }).first();
const saveNow = async () => { await click(btn('Save now'), 300); await p.waitForFunction(() => !document.body.innerText.includes('unsaved')); await wait(500); };
const scrollTo = async (y) => { await p.evaluate((y) => window.scrollTo({ top: y, behavior: 'smooth' }), y); await wait(900); };

async function api(method, url, body) {
  const res = await fetch(`${API}${url}`, { method, headers: { 'content-type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  if (!res.ok) throw new Error(`${method} ${url} → ${res.status} ${await res.text()}`);
  return res.json();
}

try {
  // Log in before recording starts.
  await p.goto(`${UI}/login`);
  await p.fill('input[name=username]', 'alice');
  await p.click('button[type=submit]');
  await p.waitForURL('**/models');
  await p.waitForLoadState('networkidle');
  await p.evaluate(() => document.fonts.ready);
  await card('KVBind', 'A Git-inspired runtime for versioned business object graphs in .NET');
  await wait(400);
  await startCast();

  // ── 1. Title, then the generic UI ──
  await wait(3000);
  await card(null);
  await wait(500);
  await caption('One generic API and UI for every model', 'Forms, labels, options and rules all come from each model\'s KVBind definition');
  await wait(2700);
  await click(p.locator('a, button', { hasText: 'Underwriting Applications' }).first(), 1200);
  await caption(null);

  // ── 2. Create an aggregate ──
  await click(btn('New'), 600);
  await caption('Create an underwriting application');
  await type(input('Basics/ApplicationNumber'), appNo);
  await type(input('Basics/ApplicantName'), 'Northwind Logistics', 500);
  await click(btn('Create & edit'));
  await p.waitForURL('**/drafts/**');
  await p.waitForLoadState('networkidle');
  await wait(600);
  await caption('Each user edits a private draft', 'An overlay over the last commit. Defaults were applied when the aggregate was born');
  await wait(2700);

  // ── 3. Typed fields and validation ──
  await scrollTo(560);
  await caption('Typed C# properties, edited as path/value patches');
  await type(input('Basics/YearsInBusiness'), '250');
  await type(input('Basics/AnnualRevenue'), '4800000');
  await saveNow();
  await scrollTo(0);
  await click(btn('Validate'), 1200);
  await caption('Profile-based validation', 'Value rules declared once in the DSL: Required, Range, Pattern…');
  await wait(3000);
  await scrollTo(560);
  await type(input('Basics/YearsInBusiness'), '12');
  await saveNow();
  await scrollTo(0);
  await click(btn('Validate'), 1300);
  await click(p.locator('button', { hasText: '✕' }).first(), 300);
  await caption(null);

  // ── 4. Collections and change reactions ──
  await click(cardBtn('Locations'), 800);
  await caption('Polymorphic collections', 'Buildings and vehicles, with count and aggregate-sum rules');
  await click(btn('+ Building'), 1000);
  await type(input('Name'), 'Main warehouse');
  await type(input('Value'), '2500000');
  await saveNow();
  await click(p.getByRole('button', { name: 'Locations' }).first(), 800);
  await click(btn('+ Vehicle'), 1000);
  await type(input('Name'), 'Delivery truck');
  await type(input('Value'), '85000', 400);
  await saveNow();
  await click(p.getByRole('button', { name: 'Overview' }).first(), 900);
  await scrollTo(250);
  await caption('Change reactions keep derived values in sync', 'Total insured value and location count are maintained by OnChange');
  await moveTo(field('TotalInsuredValue'));
  await wait(2700);

  // ── 5. The overlay diff ──
  await scrollTo(0);
  await click(p.locator('button', { hasText: 'Overlay changes' }).first(), 600);
  await caption('The draft is a live diff against the last commit', 'Every edit is a patch; the overlay stores only what changed', true);
  await wait(3400);
  await click(p.locator('button', { hasText: 'Overlay changes' }).first(), 400);

  // ── 6. Commit ──
  await click(btn('Review & commit'), 1200);
  await caption('Review the change set, then commit', 'Commits are replayable and form a chain, like Git', true);
  await wait(2300);
  await click(btn('Confirm & commit'), 1500);
  await p.waitForURL((u) => !u.pathname.includes('/drafts/'));
  await p.waitForLoadState('networkidle');
  await wait(800);
  await click(btn('History'), 1000);
  const firstCommit = p.locator('button', { hasText: 'change(s)' }).first();
  await click(firstCommit, 800);
  await caption('Every commit keeps its change set', 'Who changed what, and when', true);
  await wait(2700);
  await caption(null);

  // ── 7. Concurrent drafts and rebase ──
  const id = p.url().split('/').filter(Boolean).pop();
  await click(btn('Edit'), 0);
  await p.waitForURL('**/drafts/**');
  await p.waitForLoadState('networkidle');
  await wait(600);
  await caption('Alice renames the applicant in her draft…');
  await type(input('Basics/ApplicantName'), 'Northwind Logistics Ltd', 400);
  await saveNow();

  // Bob commits a conflicting change through the API meanwhile.
  await caption('…while Bob commits a different name from his own draft');
  const bobDraft = await api('POST', `/underwriting/${id}/drafts`, { user: 'bob' });
  const bobId = bobDraft.draftId;
  await api('POST', `/underwriting/${id}/drafts/${bobId}/patch`, [
    { operationCode: 'SET', path: '/Basics/ApplicantName', value: 'Northwind Logistics GmbH' },
    { operationCode: 'SET', path: '/Basics/IsRenewal', value: true },
  ]);
  await api('POST', `/underwriting/${id}/drafts/${bobId}/commit`, { user: 'bob' });
  await wait(2400);

  await scrollTo(560);
  await type(input('Basics/YearsInBusiness'), '14', 300);
  await saveNow();
  await scrollTo(0);
  await caption('Alice\'s draft is now stale', 'Its base is no longer the latest commit');
  await wait(2300);
  await click(btn('Rebase now'), 1500);
  await caption('Rebase with conflict review', 'Bob\'s other edits merge automatically; the clash needs a decision', true);
  await wait(2700);
  await click(btn('Keep mine'), 1200);
  await click(btn('Finish merge'), 1500);
  await p.waitForURL('**/drafts/**');
  await p.waitForLoadState('networkidle');
  await wait(800);
  await scrollTo(560);
  await caption('Merged onto Bob\'s commit', 'Alice\'s name is kept and Bob\'s renewal flag is pulled in; the draft is ready to commit');
  await moveTo(field('Basics/IsRenewal'));
  await wait(3000);
  await caption(null);

  // ── 8. End card ──
  await card('KVBind', 'Drafts · commits · rebase · patches · validation · reactions · migrations', 'kvbind.x86.cc');
  await wait(4000);
} catch (e) {
  await p.screenshot({ path: path.join(out, 'failure.png') });
  console.error(e);
  process.exitCode = 1;
} finally {
  await cdp.send('Page.stopScreencast').catch(() => {});
  fs.writeFileSync(path.join(out, 'frames.json'), JSON.stringify({ frames, end: Date.now() / 1000 }));
  await browser.close();
  console.log(`${frames.length} frames, ${appNo}`);
}
