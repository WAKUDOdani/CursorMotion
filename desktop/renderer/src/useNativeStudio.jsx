import { useEffect, useState, useSyncExternalStore } from 'react';
import { NativeStudio } from './native-model.mjs';

export function useNativeStudio() {
  const [client] = useState(() => new NativeStudio(window.studio));
  const view = useSyncExternalStore(client.subscribe, client.getSnapshot);
  useEffect(() => {
    client.load().catch(() => {});
    const timer = setInterval(() => client.poll(), 900);
    return () => {
      clearInterval(timer);
      client.flush().catch(() => {});
    };
  }, [client]);
  return { ...view, client };
}
