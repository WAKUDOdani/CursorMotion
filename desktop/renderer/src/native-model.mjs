export const skinIds = { classic: 0, crystal: 1, aurora: 2, carbon: 3, rose: 4 };
export const formLabels = ['Стрелка', 'Текст', 'Ожидание', 'Рука'];

export function mixFromAppearances(appearances) {
  return Object.fromEntries(formLabels.map((label, index) => [label, Object.keys(skinIds).find(material => skinIds[material] === appearances[index].Skin) || 'classic']));
}

export function replaceFormMaterial(appearances, index, material, packs) {
  const pack = packs.find(item => item.Id === skinIds[material]);
  if (!pack || !appearances[index]) throw new Error('Неизвестный материал или форма');
  return appearances.map((appearance, position) => position !== index ? appearance : {
    ...appearance,
    Skin: pack.Id,
    FillColor: pack.Fill,
    OutlineColor: pack.Outline,
    ShadowColor: pack.Shadow,
    OutlineWidth: pack.Width,
    ShadowBlur: pack.Softness,
    ShadowX: 0,
    ShadowY: pack.Id === 3 ? 1 : 1.6,
  });
}

export function sameAppearances(first, second) {
  return first.length === second.length && first.every((appearance, index) => Object.keys(appearance).every(key => appearance[key] === second[index][key]));
}

export function withSharedSize(appearances, scale) {
  if (!Number.isFinite(scale) || scale < .5 || scale > 6) throw new RangeError('Недопустимый размер');
  return appearances.map(appearance => ({ ...appearance, Scale: scale }));
}

export function packForAppearances(appearances, packs) {
  const mix = mixFromAppearances(appearances);
  if (!Object.values(mix).every(material => material === mix.Стрелка)) return 'custom';
  if (!packs) return mix.Стрелка;
  const pack = packs.find(item => item.Id === appearances[0].Skin);
  if (!pack) return 'custom';
  const colors = { FillColor: pack.Fill, OutlineColor: pack.Outline, ShadowColor: pack.Shadow };
  const numbers = { OutlineWidth: pack.Width, ShadowBlur: pack.Softness, ShadowX: 0, ShadowY: pack.Id === 3 ? 1 : 1.6 };
  const matches = appearances.every(look => Object.entries(colors).every(([key, value]) => look[key].toUpperCase() === value.toUpperCase())
    && Object.entries(numbers).every(([key, value]) => Math.abs(look[key] - value) <= (key === 'ShadowY' && pack.Id === 0 ? .10001 : .00001)));
  return matches ? mix.Стрелка : 'custom';
}

export class NativeStudio {
  constructor(bridge) {
    this.bridge = bridge;
    this.snapshot = null;
    this.view = { model: null, busy: false, error: '' };
    this.listeners = new Set();
    this.optimistic = new Map();
    this.pending = new Map();
    this.queue = Promise.resolve();
    this.revision = 0;
    this.jobs = 0;
    this.error = '';
  }

  subscribe = listener => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  getSnapshot = () => this.view;

  emit() {
    const settings = this.snapshot ? { ...this.snapshot.settings } : null;
    for (const [property, entry] of this.optimistic) settings[property] = entry.value;
    this.view = { model: this.snapshot ? { ...this.snapshot, settings } : null, busy: this.jobs > 0, error: this.error };
    for (const listener of this.listeners) listener();
  }

  enqueue(operation) {
    this.jobs++;
    this.emit();
    const action = this.queue.then(operation).then(result => {
      this.error = '';
      if (result?.settings) this.snapshot = result;
      return result;
    }).catch(error => {
      this.error = error.message || String(error);
      throw error;
    }).finally(() => {
      this.jobs--;
      this.emit();
    });
    this.queue = action.catch(() => {});
    return action;
  }

  async load() {
    if (!this.bridge) {
      this.error = 'Открой CursorMotion через приложение, а не как веб-страницу.';
      this.emit();
      return;
    }
    return this.enqueue(() => this.bridge.request('get'));
  }

  updateGlobal(property, value) {
    const entry = { value, revision: ++this.revision };
    this.optimistic.set(property, entry);
    this.pending.set(property, entry);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush().catch(() => {}), 80);
    this.emit();
  }

  flush() {
    clearTimeout(this.timer);
    const actions = [];
    for (const [property, entry] of this.pending) {
      this.pending.delete(property);
      actions.push(this.enqueue(async () => {
        try {
          return await this.bridge.request('set', { property, value: entry.value });
        } finally {
          if (this.optimistic.get(property)?.revision === entry.revision) this.optimistic.delete(property);
        }
      }));
    }
    return Promise.all(actions).then(() => this.queue);
  }

  async command(command, data = {}) {
    await this.flush();
    return this.enqueue(async () => {
      const result = await this.bridge.request(command, data);
      if (command === 'enabled' && this.snapshot) this.snapshot = { ...this.snapshot, state: result };
      return result;
    });
  }

  async file(action) {
    await this.flush();
    return this.enqueue(() => this.bridge.file(action));
  }

  async poll() {
    if (!this.snapshot || this.jobs || !this.bridge) return;
    try {
      const state = await this.bridge.request('status');
      if (!this.jobs) {
        this.snapshot = { ...this.snapshot, state };
        this.error = '';
        this.emit();
      }
    } catch (error) {
      this.error = error.message || String(error);
      this.snapshot = { ...this.snapshot, state: { ...this.snapshot.state, enabled: false } };
      this.emit();
    }
  }
}
