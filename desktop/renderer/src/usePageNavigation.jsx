import { useCallback, useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { pageDirection } from './page-navigation.mjs';

export function usePageNavigation(reduced, preparePage) {
  const [page, setPage] = useState('collection');
  const current = useRef('collection');
  const requested = useRef('collection');
  const revision = useRef(0);
  const active = useRef(null);
  const fallback = useRef(null);
  const pending = useRef(Promise.resolve());
  const ready = useRef(Promise.resolve());
  const reduction = useRef(reduced);
  reduction.current = reduced;

  const navigate = useCallback((next, { animate = true } = {}) => {
    const direction = pageDirection(current.current, next);
    if (requested.current === next) return pending.current;
    requested.current = next;
    const version = ++revision.current;
    active.current?.skipTransition();
    fallback.current?.cancel();
    document.documentElement.dataset.pageDirection = direction;
    const update = () => {
      if (version !== revision.current || current.current === next) return;
      flushSync(() => setPage(next));
      current.current = next;
      document.querySelector('.studio')?.scrollTo({ top: 0, behavior: 'instant' });
      document.querySelector('.settings-scroll')?.scrollTo({ top: 0, behavior: 'instant' });
    };
    let releaseReady;
    ready.current = new Promise(resolve => { releaseReady = resolve; });
    const run = async () => {
      await preparePage(next);
      await new Promise(resolve => requestAnimationFrame(resolve));
      if (version !== revision.current) return;
      if (!animate || reduction.current || direction === 'none' || typeof document.startViewTransition !== 'function') {
        update();
        active.current = null;
        if (animate && !reduction.current && direction !== 'none') {
          fallback.current = document.querySelector('.main-content')?.animate([
            { opacity: 0, transform: `translateX(${direction === 'forward' ? 64 : -64}px)` },
            { opacity: 1, transform: 'translateX(0)' },
          ], { duration: 420, easing: 'cubic-bezier(.22,1,.36,1)' });
        }
        releaseReady();
        await fallback.current?.finished.catch(() => {});
        return;
      }
      const transition = document.startViewTransition(update);
      active.current = transition;
      const timeout = setTimeout(() => transition.skipTransition(), 1200);
      transition.ready.catch(() => {}).finally(releaseReady);
      await transition.finished.catch(() => {});
      clearTimeout(timeout);
      if (active.current === transition) active.current = null;
    };
    pending.current = run().finally(releaseReady);
    return pending.current;
  }, [preparePage]);

  const whenSettled = useCallback(() => pending.current, []);
  const whenReady = useCallback(() => ready.current, []);

  useEffect(() => {
    if (reduced) {
      active.current?.skipTransition();
      fallback.current?.cancel();
    }
  }, [reduced]);

  useEffect(() => () => {
    revision.current++;
    active.current?.skipTransition();
    fallback.current?.cancel();
  }, []);

  return { page, navigate, whenSettled, whenReady };
}
