// Wavelength site effects and small helpers (shared by every page).
// Same motion language as the landing page: reveal on scroll, hopping letters, tilting cards, square cursor.
(() => {
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const canHover = matchMedia('(hover: hover) and (pointer: fine)').matches;
  // token written by @Html.AntiForgeryToken() in the layout; sent as a header on AJAX POSTs
  const csrf = document.querySelector('#csrf input[name="__RequestVerificationToken"]')?.value || '';
  // jQuery AJAX (Ajax.BeginForm, $.post) sends it too
  if (window.jQuery) jQuery.ajaxSetup({ headers: { 'X-CSRF-TOKEN': csrf } });

  // ---------- fetch helper used by the player and Vue islands ----------
  const api = async (url, { method = 'GET', body } = {}) => {
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf, Accept: 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
      credentials: 'same-origin',
    });
    if (res.status === 401) {
      location.href = '/login?returnUrl=' + encodeURIComponent(location.pathname + location.search);
      throw new Error('unauthorized');
    }
    const data = res.headers.get('content-type')?.includes('json') ? await res.json() : null;
    if (!res.ok) throw Object.assign(new Error(data?.error || res.statusText), { status: res.status, data });
    return data;
  };

  const toast = (text, kind = 'ok') => {
    const wrap = document.createElement('div');
    wrap.className = 'toast fixed left-1/2 top-24 z-[60] w-[min(92vw,32rem)] -translate-x-1/2';
    wrap.setAttribute('role', 'status');
    const box = document.createElement('div');
    box.className = 'brut flex items-center gap-3 px-5 py-4 font-mono text-sm font-bold ' + (kind === 'error' ? 'bg-flame text-paper' : 'bg-mint');
    box.innerHTML = '<span class="text-xl"></span><span></span>';
    box.children[0].textContent = kind === 'error' ? '!' : '✓';
    box.children[1].textContent = text;
    wrap.appendChild(box);
    document.body.appendChild(wrap);
    setTimeout(() => wrap.remove(), 5200);
  };

  const fmt = (s) => { s = Math.max(0, Math.floor(s || 0)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };

  window.WL = Object.assign(window.WL || {}, { api, toast, fmt, csrf, reducedMotion, canHover });

  // ---------- reveal on scroll ----------
  const revealAll = (root = document) => {
    root.querySelectorAll('[data-reveal]').forEach((el) => {
      if (el.classList.contains('reveal')) return;
      el.classList.add('reveal');
      const d = el.getAttribute('data-reveal');
      if (d) el.style.transitionDelay = d + 'ms';
      const io = new IntersectionObserver(([e]) => {
        if (e.isIntersecting) { el.classList.add('in'); io.disconnect(); }
      }, { threshold: 0.12 });
      io.observe(el);
    });
  };

  // ---------- letters hop and turn orange under the pointer ----------
  const hop = (el) => {
    if (reducedMotion || !canHover || el.dataset.hopped) return;
    el.dataset.hopped = '1';
    el.setAttribute('aria-label', el.textContent.trim().replace(/\s+/g, ' '));
    const walk = (node) => {
      [...node.childNodes].forEach((n) => {
        if (n.nodeType === 3) {
          const frag = document.createDocumentFragment();
          n.textContent.split(/(\s+)/).forEach((tok) => {
            if (!tok) return;
            if (/^\s+$/.test(tok)) { frag.appendChild(document.createTextNode(tok)); return; }
            const w = document.createElement('span');
            w.className = 'hop-w';
            w.setAttribute('aria-hidden', 'true');
            [...tok].forEach((ch) => {
              const c = document.createElement('span');
              c.className = 'hop-c';
              c.textContent = ch;
              w.appendChild(c);
            });
            frag.appendChild(w);
          });
          n.replaceWith(frag);
        } else if (n.nodeType === 1 && !n.classList.contains('hop-w')) {
          walk(n);
        }
      });
    };
    walk(el);
  };

  // ---------- cards lean toward the pointer ----------
  const tilt = (el) => {
    if (reducedMotion || !canHover || el.dataset.tilted) return;
    el.dataset.tilted = '1';
    const max = Number(el.getAttribute('data-tilt')) || 5;
    el.addEventListener('pointermove', (e) => {
      const r = el.getBoundingClientRect();
      const x = (e.clientX - r.left) / r.width - 0.5;
      const y = (e.clientY - r.top) / r.height - 0.5;
      el.style.transitionDelay = '0ms';
      el.style.transition = 'transform .1s ease-out, box-shadow .2s, opacity .8s';
      el.style.transform = `perspective(900px) rotateX(${-y * max}deg) rotateY(${x * max}deg) translateY(-4px)`;
    });
    el.addEventListener('pointerleave', () => {
      el.style.transition = 'transform .4s ease, box-shadow .2s, opacity .8s';
      el.style.transform = '';
    });
  };

  // ---------- count up numbers ----------
  const countUp = (el) => {
    const to = Number(el.getAttribute('data-count')) || 0;
    const suffix = el.getAttribute('data-suffix') || '';
    if (reducedMotion) { el.textContent = to.toLocaleString('en') + suffix; return; }
    el.textContent = '0' + suffix;
    const io = new IntersectionObserver(([e]) => {
      if (!e.isIntersecting) return;
      io.disconnect();
      const start = performance.now(), dur = 1400;
      const tick = (t) => {
        const p = Math.min((t - start) / dur, 1);
        el.textContent = Math.round(to * (1 - Math.pow(1 - p, 3))).toLocaleString('en') + suffix;
        if (p < 1) requestAnimationFrame(tick);
      };
      requestAnimationFrame(tick);
    }, { threshold: 0.4 });
    io.observe(el);
  };

  WL.enhance = (root = document) => {
    revealAll(root);
    root.querySelectorAll('[data-hop]').forEach(hop);
    root.querySelectorAll('[data-tilt]').forEach(tilt);
    root.querySelectorAll('[data-count]').forEach(countUp);
  };

  // ---------- toggles, dropdowns, confirmations ----------
  document.addEventListener('click', (e) => {
    const t = e.target.closest('[data-toggle]');
    if (t) {
      const target = document.querySelector(t.getAttribute('data-toggle'));
      if (target) {
        const hidden = target.classList.toggle('hidden');
        t.setAttribute('aria-expanded', String(!hidden));
      }
    }
    const dd = e.target.closest('[data-dropdown-toggle]');
    document.querySelectorAll('[data-dropdown-menu]').forEach((m) => {
      if (!dd || !dd.parentElement.contains(m)) m.classList.add('hidden');
    });
    if (dd) {
      const menu = dd.parentElement.querySelector('[data-dropdown-menu]');
      const hidden = menu.classList.toggle('hidden');
      dd.setAttribute('aria-expanded', String(!hidden));
    }
  });

  // forms and buttons with data-confirm ask first
  document.addEventListener('submit', (e) => {
    const msg = e.target.getAttribute('data-confirm') || e.submitter?.getAttribute('data-confirm');
    if (msg && !confirm(msg)) e.preventDefault();
  });

  // ---------- like buttons (any [data-like]) ----------
  document.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-like]');
    if (!b) return;
    e.preventDefault();
    if (document.body.dataset.signedIn !== 'true') {
      location.href = '/login?returnUrl=' + encodeURIComponent(location.pathname);
      return;
    }
    const id = b.getAttribute('data-like');
    try {
      const r = await api(`/api/tracks/${id}/like`, { method: 'POST' });
      document.querySelectorAll(`[data-like="${id}"]`).forEach((x) => {
        x.setAttribute('aria-pressed', String(r.liked));
        x.classList.toggle('bg-flame', r.liked);
        x.classList.toggle('text-paper', r.liked);
        x.classList.toggle('bg-paper', !r.liked);
        const count = x.querySelector('[data-like-count]');
        if (count) count.textContent = r.likes;
      });
      toast(r.liked ? 'Saved to your liked songs.' : 'Removed from liked songs.');
    } catch (err) {
      if (err.message !== 'unauthorized') toast('Could not update like.', 'error');
    }
  });

  // ---------- copy buttons ----------
  document.addEventListener('click', async (e) => {
    const b = e.target.closest('[data-copy]');
    if (!b) return;
    try { await navigator.clipboard.writeText(b.getAttribute('data-copy')); toast('Link copied.'); }
    catch { prompt('Copy this link:', b.getAttribute('data-copy')); }
  });

  // ---------- square cursor that trails the pointer ----------
  if (canHover && !reducedMotion) {
    const c = document.createElement('div');
    c.className = 'fx-cursor';
    document.body.appendChild(c);
    let tx = 0, ty = 0, x = 0, y = 0, shown = false;
    addEventListener('pointermove', (e) => {
      tx = e.clientX; ty = e.clientY;
      if (!shown) { shown = true; x = tx; y = ty; c.style.opacity = 1; }
    });
    document.documentElement.addEventListener('mouseleave', () => { c.style.opacity = 0; shown = false; });
    addEventListener('pointerover', (e) => {
      c.classList.toggle('big', !!e.target.closest('a, button, input, textarea, select, label, [role=button]'));
    });
    addEventListener('pointerdown', () => c.classList.add('down'));
    addEventListener('pointerup', () => c.classList.remove('down'));
    const loop = () => {
      x += (tx - x) * 0.22; y += (ty - y) * 0.22;
      c.style.transform = `translate(${x}px, ${y}px) translate(-50%, -50%)`;
      requestAnimationFrame(loop);
    };
    loop();
  }

  // ---------- Vue islands: mount any [data-vue] element with a registered component ----------
  WL.islands = WL.islands || {};
  WL.island = (name, options) => { WL.islands[name] = options; };

  document.addEventListener('DOMContentLoaded', () => {
    WL.enhance();
    document.querySelectorAll('[data-vue]').forEach((el) => {
      const opts = WL.islands[el.getAttribute('data-vue')];
      if (!opts) return;
      const props = el.getAttribute('data-props');
      const app = Vue.createApp(opts, props ? JSON.parse(props) : {});
      app.mount(el);
      el.removeAttribute('v-cloak');
      WL.enhance(el);
    });
  });
})();
