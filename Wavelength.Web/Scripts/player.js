// Global audio player (Vue 3 island mounted on #player-root).
// Implements the "Play a track" sequence diagram:
//   click Play -> GET /api/tracks/{id} (published only) -> start playback
//   after 30 s of listening or when the track ends -> POST /api/plays { trackId, seconds }
// The queue and position survive page loads through sessionStorage.
(() => {
  const { createApp, ref, computed, watch, onMounted, nextTick } = Vue;
  const KEY = 'wl.player';
  const REPORT_AFTER = 30;

  const load = () => { try { return JSON.parse(sessionStorage.getItem(KEY)) || null; } catch { return null; } };
  const save = (s) => { try { sessionStorage.setItem(KEY, JSON.stringify(s)); } catch { /* private mode */ } };

  const app = createApp({
    setup() {
      const audio = new Audio();
      audio.preload = 'metadata';
      const queue = ref([]);
      const index = ref(0);
      const track = ref(null);
      const playing = ref(false);
      const time = ref(0);
      const duration = ref(0);
      const volume = ref(Number(localStorage.getItem('wl.volume') ?? 0.9));
      const loading = ref(false);
      const error = ref('');
      const expanded = ref(false);
      let listened = 0, lastTick = 0, reported = false;

      audio.volume = volume.value;
      watch(volume, (v) => { audio.volume = v; try { localStorage.setItem('wl.volume', v); } catch {} });

      const persist = () => save({ queue: queue.value, index: index.value, time: audio.currentTime || 0, playing: playing.value });

      const report = async () => {
        if (reported || !track.value) return;
        reported = true;
        try { await WL.api('/api/plays', { method: 'POST', body: { trackId: track.value.id, seconds: Math.round(listened) } }); }
        catch { /* a failed report should never stop the music */ }
      };

      const syncButtons = () => {
        document.querySelectorAll('[data-play]').forEach((b) => {
          const on = track.value && Number(b.getAttribute('data-play')) === track.value.id;
          const icon = b.querySelector('[data-play-icon]') || b;
          if (icon.hasAttribute('data-play-icon') || !b.children.length) icon.textContent = on && playing.value ? '❚❚' : '▶';
          b.closest('[data-track-row]')?.classList.toggle('bg-volt', !!on);
        });
      };

      const open = async (i, { autoplay = true, startAt = 0 } = {}) => {
        if (i < 0 || i >= queue.value.length) return;
        index.value = i;
        loading.value = true;
        error.value = '';
        listened = 0; reported = false;
        try {
          const t = await WL.api(`/api/tracks/${queue.value[i]}`);
          track.value = t;
          audio.src = t.audioUrl;
          audio.currentTime = startAt;
          duration.value = t.durationSec;
          document.body.classList.add('has-player');
          if (autoplay) await audio.play().catch(() => { playing.value = false; });
        } catch (e) {
          error.value = e.status === 404 ? 'Track unavailable.' : 'Could not load the track.';
          WL.toast(error.value, 'error');
          if (queue.value.length > 1 && i < queue.value.length - 1) return open(i + 1, { autoplay });
        } finally {
          loading.value = false;
          persist();
          syncButtons();
        }
      };

      const playQueue = (ids, start = 0) => {
        ids = ids.map(Number);
        if (track.value && ids[start] === track.value.id) return toggle();
        queue.value = ids;
        open(start);
      };
      const toggle = () => { if (!track.value) return; audio.paused ? audio.play().catch(() => {}) : audio.pause(); };
      const next = () => { if (index.value < queue.value.length - 1) open(index.value + 1); else { audio.pause(); audio.currentTime = 0; } };
      const prev = () => { if (audio.currentTime > 3 || index.value === 0) audio.currentTime = 0; else open(index.value - 1); };
      const seek = (e) => { audio.currentTime = Number(e.target.value); };
      const close = () => {
        audio.pause(); audio.removeAttribute('src');
        track.value = null; queue.value = []; playing.value = false;
        document.body.classList.remove('has-player');
        sessionStorage.removeItem(KEY);
        syncButtons();
      };

      audio.addEventListener('play', () => { playing.value = true; lastTick = audio.currentTime; syncButtons(); persist(); });
      audio.addEventListener('pause', () => { playing.value = false; syncButtons(); persist(); });
      audio.addEventListener('loadedmetadata', () => { if (isFinite(audio.duration)) duration.value = audio.duration; });
      audio.addEventListener('timeupdate', () => {
        const delta = audio.currentTime - lastTick;
        if (delta > 0 && delta < 2) listened += delta; // count real listening, not seeking
        lastTick = audio.currentTime;
        time.value = audio.currentTime;
        if (!reported && listened >= REPORT_AFTER) report();
      });
      audio.addEventListener('ended', () => { report(); next(); });
      addEventListener('pagehide', persist);

      // any [data-play] button on the page queues the tracks of its list
      document.addEventListener('click', (e) => {
        const b = e.target.closest('[data-play]');
        if (!b) return;
        e.preventDefault();
        const list = b.closest('[data-tracklist]');
        const all = list ? [...new Set([...list.querySelectorAll('[data-play]')].map((x) => x.getAttribute('data-play')))] : [b.getAttribute('data-play')];
        playQueue(all, Math.max(0, all.indexOf(b.getAttribute('data-play'))));
      });
      // "play all" buttons: data-play-all="1,2,3"
      document.addEventListener('click', (e) => {
        const b = e.target.closest('[data-play-all]');
        if (!b) return;
        e.preventDefault();
        const ids = b.getAttribute('data-play-all').split(',').filter(Boolean);
        if (ids.length) { queue.value = []; track.value = null; playQueue(ids, 0); }
      });
      document.addEventListener('keydown', (e) => {
        if (e.code !== 'Space' || !track.value || e.target.closest('input, textarea, select, button, [contenteditable]')) return;
        e.preventDefault(); toggle();
      });

      onMounted(() => {
        const s = load();
        if (s?.queue?.length) {
          queue.value = s.queue;
          open(s.index || 0, { autoplay: !!s.playing, startAt: s.time || 0 });
        }
        WL.player = { playQueue, toggle, next, prev, close };
      });

      const progress = computed(() => (duration.value ? (time.value / duration.value) * 100 : 0));
      return { track, playing, time, duration, volume, loading, error, expanded, queue, index, toggle, next, prev, seek, close, progress, fmt: WL.fmt };
    },
    template: `
    <div v-if="track" class="fixed inset-x-0 bottom-0 z-40 border-t-2 border-ink bg-ink p-3 text-paper md:px-6">
      <div class="mx-auto flex max-w-7xl items-center gap-3 md:gap-5">
        <a :href="'/tracks/' + track.id" class="relative h-14 w-14 shrink-0 overflow-hidden border-2 border-paper" :style="{ background: track.color }">
          <img v-if="track.coverUrl" :src="track.coverUrl" alt="" class="h-full w-full object-cover">
          <span v-else class="display flex h-full w-full items-center justify-center text-2xl" :style="{ color: track.onColor }">{{ track.title.charAt(0) }}</span>
        </a>
        <div class="min-w-0 flex-1 sm:w-56 sm:flex-none">
          <a :href="'/tracks/' + track.id" class="display block truncate text-xl hover:text-volt md:text-2xl">{{ track.title }}</a>
          <a :href="'/artists/' + track.artistId" class="block truncate font-mono text-xs text-paper/60 hover:text-volt">{{ track.artist }}</a>
        </div>
        <div class="flex items-center gap-2 md:gap-3">
          <button v-on:click="prev" class="hidden font-mono text-xl hover:text-volt sm:block" aria-label="Previous">⏮</button>
          <button v-on:click="toggle" class="flex h-12 w-12 items-center justify-center border-2 border-paper bg-volt text-ink transition hover:scale-105" :aria-label="playing ? 'Pause' : 'Play'">
            <span v-if="loading" class="animate-pulse">…</span><span v-else>{{ playing ? '❚❚' : '▶' }}</span>
          </button>
          <button v-on:click="next" class="font-mono text-xl hover:text-volt" aria-label="Next" :disabled="index >= queue.length - 1" :class="index >= queue.length - 1 ? 'opacity-30' : ''">⏭</button>
        </div>
        <div class="hidden flex-1 items-center gap-3 font-mono text-xs md:flex">
          <span class="w-10 text-right">{{ fmt(time) }}</span>
          <div class="relative h-3 flex-1">
            <div class="pointer-events-none absolute inset-0 border-2 border-paper"><div class="h-full bg-volt" :style="{ width: progress + '%' }"></div></div>
            <input type="range" min="0" :max="duration || 0" step="0.5" :value="time" v-on:input="seek" class="absolute inset-0 h-full w-full cursor-pointer opacity-0" aria-label="Seek">
          </div>
          <span class="w-10">{{ fmt(duration) }}</span>
        </div>
        <label class="hidden items-center gap-2 font-mono text-xs lg:flex">VOL
          <input type="range" min="0" max="1" step="0.05" v-model.number="volume" class="w-20 accent-[#FFE500]" aria-label="Volume">
        </label>
        <button :data-like="track.id" class="hidden h-9 w-9 items-center justify-center border-2 border-paper font-mono text-lg hover:text-flame sm:flex" aria-label="Like">♥</button>
        <button v-on:click="close" class="font-mono text-xl text-paper/60 hover:text-flame" aria-label="Close player">✕</button>
      </div>
      <div class="mt-2 h-1 bg-paper/20 md:hidden"><div class="h-full bg-volt" :style="{ width: progress + '%' }"></div></div>
    </div>`,
  });

  document.addEventListener('DOMContentLoaded', () => app.mount('#player-root'));
})();
