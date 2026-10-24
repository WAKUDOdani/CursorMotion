import { useEffect, useState } from 'react';

const pending = new Set();
const cache = new Map();
const visualKeys = ['Skin', 'FillColor', 'OutlineColor', 'ShadowColor', 'OutlineWidth', 'ShadowBlur', 'ShadowX', 'ShadowY'];

export function artworkKey(appearances) {
  return JSON.stringify(appearances.map(look => Object.fromEntries(visualKeys.map(key => [key, look[key]]))));
}

export async function waitForNativeArtwork() {
  while (pending.size) await Promise.all([...pending]);
}

export function useNativeArtwork(client, appearances) {
  const key = artworkKey(appearances);
  const [state, setState] = useState({ key: '', frames: {}, error: '' });
  useEffect(() => {
    let current = true, release;
    const waiting = new Promise(resolve => { release = resolve; });
    pending.add(waiting);
    const timer = setTimeout(async () => {
      try {
        if (!cache.has(key)) {
          const request = client.bridge.request('artwork', { appearances });
          cache.set(key, request);
          request.catch(() => cache.delete(key));
          if (cache.size > 16) cache.delete(cache.keys().next().value);
        }
        const result = await cache.get(key);
        await Promise.all(Object.values(result.frames).map(frame => {
          const image = new Image(); image.src = frame.dataUri; return image.decode();
        }));
        if (current) setState({ key, frames: result.frames, error: '' });
      } catch (error) {
        if (current) setState({ key, frames: {}, error: error.message || String(error) });
      } finally { pending.delete(waiting); release(); }
    }, 180);
    return () => { current = false; clearTimeout(timer); pending.delete(waiting); release(); };
  }, [client, key]);
  return { frames: state.key === key ? state.frames : {}, loading: state.key !== key, error: state.error };
}
