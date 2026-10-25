import test from 'node:test';
import assert from 'node:assert/strict';
import { NativeStudio, mixFromAppearances, packForAppearances, replaceFormMaterial, sameAppearances, withSharedSize, withSharedExactSize, visibleCursorSize } from '../src/native-model.mjs';

const appearance = { Skin: 0, Scale: 1.6, MorphTime: 140, IdleHide: 0, FillColor: '#FF000000', OutlineColor: '#FFFFFFFF', ShadowColor: '#66000000', OutlineWidth: 1.4, ShadowBlur: 2.5, ShadowX: 0, ShadowY: 1.5 };
const packs = [{ Id: 2, Fill: '#FFBDA4FF', Outline: '#FFF3EEFF', Shadow: '#553B2270', Width: .8, Softness: 3.5 }];
const snapshot = () => ({ settings: { Smoothing: 140, Shutter: 270 }, appearances: Array.from({ length: 4 }, () => ({ ...appearance })), state: { enabled: false }, packs, displays: [] });

test('legacy fitted requests show visible size while exact experimental sizes remain literal', () => {
  const state = { compactRequired: true, nativeGeometryRatio: 1, nativeMaximumScale: 2.8 };
  assert.equal(visibleCursorSize({...appearance,Scale:6},state),2.8);
  assert.equal(visibleCursorSize({...appearance,Scale:6,ExactSize:true},state),4.5);
  assert.equal(visibleCursorSize({...appearance,Scale:3}, {...state,nativeGeometryRatio:2/3}),2);
  assert.equal(visibleCursorSize({...appearance,Scale:6}, {compactRequired:false}),4.5);
});

test('shared literal sizing preserves materials and timing, and survives JSON export', () => {
  const original=snapshot().appearances.map((look,i)=>({...look,Scale:1+i,MorphTime:100+i*25}));
  const resized=JSON.parse(JSON.stringify(withSharedExactSize(original,4.5)));
  for(let i=0;i<4;i++){
    assert.equal(resized[i].ExactSize,true);
    assert.equal(resized[i].Scale,4.5);
    const {ExactSize,...rest}=resized[i];
    assert.deepEqual({...rest,Scale:original[i].Scale},original[i]);
  }
});

test('shared size updates every complete form without changing materials or behavior', () => {
  const original = snapshot().appearances.map((look, i) => ({ ...look, Skin: i, Scale: 1 + i, MorphTime: 100 + i * 40 }));
  const before = structuredClone(original), resized = withSharedSize(original, 2.75);
  assert.deepEqual(original, before);
  assert.ok(resized.every(look => look.Scale === 2.75));
  for (let i = 0; i < 4; i++) assert.deepEqual({ ...resized[i], Scale: original[i].Scale }, original[i]);
  for (const value of [NaN, Infinity, 0, 4.51, 6, 7]) assert.throws(() => withSharedSize(original, value), RangeError);
});

test('repainted stock materials become a custom set while untouched legacy Classic stays Classic', () => {
  const classic = [{ Id: 0, Fill: '#FF000000', Outline: '#FFFFFFFF', Shadow: '#66000000', Width: 1.4, Softness: 2.5 }];
  const looks = snapshot().appearances;
  assert.equal(packForAppearances(looks, classic), 'classic');
  looks[0].Scale = 4; looks[3].MorphTime = 240;
  assert.equal(packForAppearances(looks, classic), 'classic');
  looks[1].FillColor = '#FFFF0000';
  assert.equal(packForAppearances(looks, classic), 'custom');
});

test('one material changes only its own complete appearance and preserves behavior', () => {
  const original = snapshot().appearances;
  original[1] = { ...original[1], Scale: 3, MorphTime: 300, IdleHide: 4 };
  const changed = replaceFormMaterial(original, 1, 'aurora', packs);
  assert.equal(changed[0], original[0]);
  assert.equal(changed[2], original[2]);
  assert.equal(changed[1].Skin, 2);
  assert.equal(changed[1].Scale, 3);
  assert.equal(changed[1].MorphTime, 300);
  assert.equal(changed[1].IdleHide, 4);
  assert.equal(changed[1].FillColor, packs[0].Fill);
  assert.equal(original[1].Skin, 0);
  assert.equal(packForAppearances(changed), 'custom');
  assert.deepEqual(mixFromAppearances(changed), { Стрелка: 'classic', Текст: 'aurora', Ожидание: 'classic', Рука: 'classic' });
});

test('palette and size changes count as unapplied even without material changes', () => {
  const original = snapshot().appearances;
  assert.ok(sameAppearances(original, structuredClone(original)));
  const changed = structuredClone(original);
  changed[2].Scale = 2;
  assert.equal(sameAppearances(changed, original), false);
});

test('rapid global edits are coalesced and serialized before an atomic apply', async () => {
  let model = snapshot();
  const calls = [];
  const bridge = { request: async (command, data) => {
    calls.push([command, data]);
    if (command === 'set') model.settings[data.property] = data.value;
    if (command === 'forms') model.appearances = data.appearances;
    return structuredClone(model);
  } };
  const client = new NativeStudio(bridge);
  await client.load();
  client.updateGlobal('Smoothing', 200);
  client.updateGlobal('Smoothing', 220);
  client.updateGlobal('Shutter', 360);
  assert.equal(client.getSnapshot().model.settings.Smoothing, 220);
  await client.command('forms', { appearances: model.appearances });
  assert.deepEqual(calls.map(([command]) => command), ['get', 'set', 'set', 'forms']);
  assert.equal(calls[1][1].value, 220);
  assert.equal(client.getSnapshot().model.settings.Smoothing, 220);
  assert.equal(client.getSnapshot().busy, false);
});

test('an older backend response does not roll back a newer slider edit', async () => {
  let release;
  const client = new NativeStudio({ request: (command, data) => command === 'get' ? Promise.resolve(snapshot()) : new Promise(resolve => { release = () => resolve({ ...snapshot(), settings: { Smoothing: data.value } }); }) });
  await client.load();
  client.updateGlobal('Smoothing', 200);
  const first = client.flush();
  await new Promise(resolve => setTimeout(resolve, 0));
  client.updateGlobal('Smoothing', 250);
  release();
  await first;
  assert.equal(client.getSnapshot().model.settings.Smoothing, 250);
  const second = client.flush();
  await new Promise(resolve => setTimeout(resolve, 0));
  release();
  await second;
  assert.equal(client.getSnapshot().model.settings.Smoothing, 250);
});

test('failed writes roll back optimistics, surface error and permit recovery', async () => {
  let fail = true;
  const client = new NativeStudio({ request: async (command, data) => {
    if (command === 'get') return snapshot();
    if (fail) throw new Error('Движок остановлен');
    return { ...snapshot(), settings: { Smoothing: data.value } };
  } });
  await client.load();
  client.updateGlobal('Smoothing', 250);
  await assert.rejects(client.flush(), /Движок остановлен/);
  assert.equal(client.getSnapshot().model.settings.Smoothing, 140);
  assert.match(client.getSnapshot().error, /Движок остановлен/);
  fail = false;
  client.updateGlobal('Smoothing', 180);
  await client.flush();
  assert.equal(client.getSnapshot().model.settings.Smoothing, 180);
  assert.equal(client.getSnapshot().error, '');
});

test('canceled file dialog does not change the model', async () => {
  const client = new NativeStudio({ request: async () => snapshot(), file: async () => null });
  await client.load();
  const original = client.getSnapshot().model;
  assert.equal(await client.file('import'), null);
  assert.deepEqual(client.getSnapshot().model, original);
});

test('status polling reflects hotkeys and fails closed on disconnect', async () => {
  let fail = false;
  const client = new NativeStudio({ request: async command => {
    if (command === 'get') return snapshot();
    if (fail) throw new Error('Offline');
    return { enabled: true };
  } });
  await client.load();
  await client.poll();
  assert.equal(client.getSnapshot().model.state.enabled, true);
  fail = true;
  await client.poll();
  assert.equal(client.getSnapshot().model.state.enabled, false);
  assert.equal(client.getSnapshot().error, 'Offline');
});
