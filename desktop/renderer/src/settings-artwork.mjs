import { nativeArtworkMetadata } from './native-artwork-meta.mjs';
export const materialIds = ['crystal', 'aurora', 'carbon', 'rose', 'classic'];

const baseCursorForms = [
  { id: 'arrow', label: 'Стрелка', center: 90, width: 140, height: 192 },
  { id: 'text', label: 'Текст', center: 230, width: 100, height: 170 },
  { id: 'waiting', label: 'Ожидание', center: 370, width: 145, height: 170 },
  { id: 'hand', label: 'Рука', center: 510, width: 140, height: 192 },
];

export const artworkMetadata = nativeArtworkMetadata;

export function createCursorMix(material) {
  return Object.fromEntries(cursorForms.map(form => [form.label, material]));
}

export function updateCursorMix(current, form, material) {
  if (!cursorForms.some(item => item.label === form) || !materialIds.includes(material)) {
    throw new RangeError('Unknown cursor form or material');
  }
  return { ...current, [form]: material };
}

export function sameCursorMix(first, second) {
  return cursorForms.every(form => first?.[form.label] === second?.[form.label]);
}

export function packIdForMix(mix) {
  const material = mix[cursorForms[0].label];
  return cursorForms.every(form => mix[form.label] === material) ? material : 'custom';
}

export function artworkPath(material, subject) {
  return `./assets/settings/${material}/${subject}.png`;
}

export const settingsArtworkFrame = { width: 600, height: 300, baseline: 270, aspectRatio: 2 };
export const collectionArtworkFrame = { width: 600, height: 270, baseline: 256, aspectRatio: 600 / 270 };
export const settingsCursorArtworkFrame = { ...settingsArtworkFrame };

function balancedForms(forms, frame) {
  // Space visible silhouettes, not transparent PNG rectangles. Use the width
  // that fits every material: cores stay equally wide, with fixed slot centers.
  const envelopes = forms.map(form => Math.min(...materialIds.map(material => {
    const b = artworkMetadata[material][form.id].bounds;
    return Math.min(form.width, form.height * (b.right - b.left) / (b.bottom - b.top));
  })));
  const gap = 28;
  const groupWidth = envelopes.reduce((sum, width) => sum + width, 0) + gap * (forms.length - 1);
  const scale = Math.min(1, (frame.width - 40) / groupWidth);
  let edge = (frame.width - groupWidth * scale) / 2;
  const result = forms.map((form, index) => {
    const center = edge + envelopes[index] * scale / 2;
    edge += (envelopes[index] + gap) * scale;
    return { ...form, center, width: envelopes[index] * scale, height: form.height * scale };
  });
  const heights = result.map(form => materialIds.map(material => {
    const b = artworkMetadata[material][form.id].bounds;
    return Math.min(form.height, form.width * (b.bottom - b.top) / (b.right - b.left));
  }));
  const height = (Math.max(...heights.flat()) + Math.max(...heights.map(values => Math.min(...values)))) / 2;
  frame.baseline = (frame.height + height) / 2;
  return result;
}

export const cursorForms = balancedForms(baseCursorForms, settingsCursorArtworkFrame);
export const collectionCursorForms = balancedForms(baseCursorForms.map(form => ({ ...form, width: form.width * 1.05, height: form.height * 1.15 })), collectionArtworkFrame);

export function artworkPlacement(material, subject, slot, frame = settingsCursorArtworkFrame, override) {
  const asset = override || artworkMetadata[material][subject];
  const bounds = asset.bounds;
  const factor = Math.min(slot.width / (bounds.right - bounds.left), slot.height / (bounds.bottom - bounds.top));
  return {
    width: `${asset.width * factor / frame.width * 100}%`,
    left: `${(slot.center - (bounds.left + bounds.right) / 2 * factor) / frame.width * 100}%`,
    top: `${(frame.baseline - bounds.bottom * factor) / frame.height * 100}%`,
  };
}

