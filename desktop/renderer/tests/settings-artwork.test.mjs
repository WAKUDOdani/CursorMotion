import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { artworkMetadata, artworkPath, artworkPlacement, collectionArtworkFrame, collectionCursorForms, createCursorMix, cursorForms, materialIds, packIdForMix, sameCursorMix, updateCursorMix, settingsCursorArtworkFrame } from '../src/settings-artwork.mjs';

test('changing one form preserves both other materials and the applied snapshot', () => {
  for (const form of cursorForms) {
    const applied = createCursorMix('classic');
    const draft = updateCursorMix(applied, form.label, 'aurora');
    assert.notStrictEqual(draft, applied);
    assert.equal(draft[form.label], 'aurora');
    assert.deepEqual(applied, createCursorMix('classic'));
    for (const other of cursorForms.filter(item => item.id !== form.id)) {
      assert.equal(draft[other.label], applied[other.label]);
      assert.deepEqual(artworkPlacement(draft[other.label], other.id, other), artworkPlacement(applied[other.label], other.id, other));
    }
    assert.equal(sameCursorMix(draft, applied), false);
  }
});

test('sequential edits survive form switching and explicit application snapshots', () => {
  let draft = createCursorMix('classic');
  draft = updateCursorMix(draft, 'Текст', 'aurora');
  draft = updateCursorMix(draft, 'Ожидание', 'rose');
  const applied = { ...draft };
  draft = updateCursorMix(draft, 'Стрелка', 'carbon');
  assert.deepEqual(draft, { Стрелка: 'carbon', Текст: 'aurora', Ожидание: 'rose', Рука: 'classic' });
  assert.deepEqual(applied, { Стрелка: 'classic', Текст: 'aurora', Ожидание: 'rose', Рука: 'classic' });
  assert.equal(packIdForMix(draft), 'custom');
  assert.equal(sameCursorMix(draft, applied), false);
  assert.equal(sameCursorMix({ ...draft }, draft), true);
});

test('uniform packs map to their material and invalid updates are rejected', () => {
  for (const material of materialIds) assert.equal(packIdForMix(createCursorMix(material)), material);
  assert.throws(() => updateCursorMix(createCursorMix('classic'), 'Неизвестная форма', 'rose'), RangeError);
  assert.throws(() => updateCursorMix(createCursorMix('classic'), 'Стрелка', 'custom'), RangeError);
});

test('all 50 native-rendered assets are genuine RGBA PNGs with matching metadata', () => {
  for (const material of materialIds) {
    for (const subject of ['motion', 'space', ...cursorForms.map(form => form.id), 'resize-horizontal', 'resize-vertical', 'resize-nwse', 'resize-nesw']) {
      const image = readFileSync(new URL(`../public/${artworkPath(material, subject).slice(2)}`, import.meta.url));
      const metadata = artworkMetadata[material][subject];
      assert.equal(image.subarray(1, 4).toString(), 'PNG');
      assert.equal(image.readUInt32BE(16), metadata.width);
      assert.equal(image.readUInt32BE(20), metadata.height);
      assert.equal(image[25], 6);
    }
  }
});

test('all cursor materials align to fixed centers and a shared baseline', () => {
  for (const material of materialIds) {
    for (const form of cursorForms) {
      const asset = artworkMetadata[material][form.id];
      const placement = artworkPlacement(material, form.id, form);
      const renderedWidth = parseFloat(placement.width) / 100 * 600;
      const factor = renderedWidth / asset.width;
      const left = parseFloat(placement.left) / 100 * 600;
      const top = parseFloat(placement.top) / 100 * 300;
      const center = left + (asset.bounds.left + asset.bounds.right) / 2 * factor;
      const baseline = top + asset.bounds.bottom * factor;
      assert.ok(Math.abs(center - form.center) < 0.00001);
      assert.ok(Math.abs(baseline - settingsCursorArtworkFrame.baseline) < 0.00001);
      assert.ok((asset.bounds.right - asset.bounds.left) * factor <= form.width + 0.00001);
      assert.ok((asset.bounds.bottom - asset.bounds.top) * factor <= form.height + 0.00001);
    }
  }
});

test('visible forms have real gaps, shared baselines and a centered group in every material mix', () => {
  for (const [forms, frame] of [[cursorForms, settingsCursorArtworkFrame], [collectionCursorForms, collectionArtworkFrame]]) {
    for (let combination = 0; combination < 625; combination++) {
      let code = combination;
      const boxes = forms.map(form => {
        const material = materialIds[code % 5]; code = Math.floor(code / 5);
        const asset = artworkMetadata[material][form.id], p = artworkPlacement(material, form.id, form, frame);
        const k = parseFloat(p.width) / 100 * frame.width / asset.width;
        const x = parseFloat(p.left) / 100 * frame.width, y = parseFloat(p.top) / 100 * frame.height;
        return { left: x + asset.bounds.left * k, right: x + asset.bounds.right * k, top: y + asset.bounds.top * k, bottom: y + asset.bounds.bottom * k };
      });
      const gaps = boxes.slice(1).map((box, index) => box.left - boxes[index].right);
      assert.ok(Math.min(...gaps) >= 24, `Forms touch in mix ${combination}: ${gaps}`);
      assert.ok(Math.max(...gaps) - Math.min(...gaps) < .01, `Uneven spacing: ${gaps}`);
      assert.ok(Math.abs((boxes[0].left + boxes.at(-1).right) / 2 - frame.width / 2) < 1);
      assert.ok(Math.abs((Math.min(...boxes.map(b => b.top)) + boxes[0].bottom) / 2 - frame.height / 2) < 2.5);
    }
  }
});

test('collection framing removes empty canvas without cropping any cursor core', () => {
  for (const material of materialIds) {
    for (const form of collectionCursorForms) {
      const asset = artworkMetadata[material][form.id];
      const frame = collectionArtworkFrame;
      const placement = artworkPlacement(material, form.id, form, frame);
      const factor = parseFloat(placement.width) / 100 * frame.width / asset.width;
      const left = parseFloat(placement.left) / 100 * frame.width;
      const top = parseFloat(placement.top) / 100 * frame.height;
      assert.ok(left + asset.bounds.left * factor >= 0);
      assert.ok(top + asset.bounds.top * factor >= 0);
      assert.ok(left + asset.bounds.right * factor <= frame.width);
      assert.ok(top + asset.bounds.bottom * factor <= frame.height);
      assert.ok(Math.abs(top + asset.bounds.bottom * factor - frame.baseline) < 0.00001);
      assert.ok(asset.width / (asset.width * factor * 440 / frame.width) > 2);
    }
  }
});
