import { artworkMetadata } from './settings-artwork.mjs';

export async function runStudioTests(client) {
  const assert = (value, message) => { if (!value) throw new Error(message); };
  const settle = async () => {
    await client.flush();
    await window.__pageTransitionFinished?.();
    await new Promise(resolve => setTimeout(resolve, 120));
    const artworkStart = performance.now();
    await window.__waitNativeArtwork?.();
    console.info('Native sample wait ms: ' + Math.round(performance.now() - artworkStart));
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  };
  const click = async selector => {
    console.info('UI click: '+selector);
    const element = document.querySelector(selector);
    assert(element, `Missing control: ${selector}`);
    element.click();
    await settle();
  };
  const change = async (selector, value) => {
    const element = document.querySelector(selector);
    assert(element, `Missing input: ${selector}`);
    const prototype = element.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, String(value));
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
  };
  const state = () => client.getSnapshot().model;
  const shapeImages = () => Object.fromEntries([...document.querySelectorAll('.settings-art img[data-form]')].map(image => [image.dataset.form, { src: image.src, material: image.dataset.material, renderer: image.dataset.renderer, style: image.getAttribute('style'), bounds: image.getBoundingClientRect().toJSON() }]));
  let assertions = 0;
  const check = (value, message) => { assert(value, message); assertions++; };
  await settle();
  const defaultSettings = structuredClone(state().settings);
  check(document.querySelectorAll('[data-pack]').length === 6, 'Six collection entries');
  await click('[data-pack="crystal"]');
  check(state().appearances.every(appearance => appearance.Skin === 0), 'Selection must not apply a pack');
  check(document.querySelector('[data-pack="classic"] .pack-check') && !document.querySelector('[data-pack="crystal"] .pack-check'), 'Check belongs only to applied pack');
  await click('.action-dock .primary-button');
  check(state().appearances.length === 4 && state().appearances.every(appearance => appearance.Skin === 1), 'Collection applies all four native forms');
  await click('nav [data-page="cursor"]');
  const before = shapeImages();
  await click('.settings-panel .segments button:nth-child(2)');
  await change('select[aria-label="Набор для формы"]', 'aurora');
  const after = shapeImages();
  check(before.arrow.src === after.arrow.src && before.arrow.style === after.arrow.style && before.waiting.src === after.waiting.src && before.waiting.style === after.waiting.style, 'Only edited form may change');
  check(after.text.src.startsWith('data:image/png;base64,') && after.text.material === 'aurora' && after.text.renderer === 'native-sdf', 'Text illustration is an independent render of its actual appearance');
  check(JSON.stringify(before.arrow.bounds) === JSON.stringify(after.arrow.bounds) && JSON.stringify(before.waiting.bounds) === JSON.stringify(after.waiting.bounds), 'Other form positions and sizes are unchanged');
  check(state().appearances[1].Skin === 1, 'Draft must not auto-apply');
  await change('input[aria-label="Размер"]', 2.35);
    check(Number(document.querySelector('input[aria-label="Размер"]').max) === 4.5, 'Cursor size control has a hard 4.5x maximum');
  await click('.cursor-apply');
  check(state().appearances[0].Skin === 1 && state().appearances[1].Skin === 2 && state().appearances[2].Skin === 1, 'Mixed native materials applied atomically');
  check(state().appearances[1].Scale === 2.35 && state().appearances[0].Scale === 1.6, 'Independent native sizes');
  const textBeforePalette = shapeImages().text.src;
  await click('.settings-panel .native-details summary');
  await change('input[aria-label="Заливка"]', '#778899');
  await change('input[aria-label="Заливка, непрозрачность"]', 50);
  await change('input[aria-label="Толщина обводки"]', 2.2);
  await change('input[aria-label="Мягкость тени"]', 4.5);
  await change('input[aria-label="Смещение тени X"]', -3);
  await change('input[aria-label="Скрывать в покое"]', 2);
  await click('.cursor-apply');
  check(state().appearances[1].FillColor === '#80778899' && state().appearances[1].OutlineWidth === 2.2 && state().appearances[1].ShadowBlur === 4.5 && state().appearances[1].ShadowX === -3 && state().appearances[1].IdleHide === 2, 'Appearance controls and ARGB opacity reach native profile');
  check(state().appearances[0].FillColor !== '#80778899' && state().appearances[2].ShadowX === 0, 'Appearance edits stay within selected form');

  check(shapeImages().text.src !== textBeforePalette && shapeImages().text.renderer === 'native-sdf', 'Still image updates from actual custom colors, outline and shadow');
  const beforeHand = structuredClone(state().appearances);
  await click('.settings-panel .segments button:nth-child(4)');
  await change('select[aria-label="Набор для формы"]', 'rose');
  await change('input[aria-label="Размер"]', 2.1);
  await change('input[aria-label="Переход между формами"]', 190);
  check(state().appearances[3].Skin === 1, 'Hand draft does not auto-apply');
  check(shapeImages().hand.src.startsWith('data:image/png;base64,') && shapeImages().hand.material === 'rose', 'Hand artwork follows its actual native material');
  await click('.cursor-apply');
  check(state().appearances[3].Skin === 4 && state().appearances[3].Scale === 2.1 && state().appearances[3].MorphTime === 190, 'Hand material, size and morph duration reach native profile');
  check(JSON.stringify(state().appearances.slice(0, 3)) === JSON.stringify(beforeHand.slice(0, 3)), 'Hand edits preserve arrow/text/wait appearances');
  const beforeSharedSize = structuredClone(state().appearances);
  await click('[aria-label="Одинаковый размер всех форм"]');
  await change('input[aria-label="Размер"]', 3.1);
  check(JSON.stringify(state().appearances) === JSON.stringify(beforeSharedSize), 'Linked size is a draft until explicitly applied');
  for (let i = 1; i <= 4; i++) {
    await click(`.settings-panel .segments button:nth-child(${i})`);
    check(Number(document.querySelector('input[aria-label="Размер"]').value) === 3.1, `Shared size draft reaches form ${i}`);
  }
  await click('.cursor-apply');
  check(state().appearances.every(look => look.Scale === 3.1), 'Shared size applies to all four complete native appearances');
    check(state().appearances.every((look, i) => JSON.stringify({ ...look, Scale: beforeSharedSize[i].Scale, ExactSize: beforeSharedSize[i].ExactSize }) === JSON.stringify(beforeSharedSize[i])), 'Shared size preserves every material, palette, morph and idle setting');
    check(state().appearances.every(look=>look.ExactSize), 'Size slider uses literal sizes without the legacy hidden cap');
  await click('[aria-label="Одинаковый размер всех форм"]');
  await change('input[aria-label="Размер"]', 1.95);
  await click('.cursor-apply');
  check(state().appearances[3].Scale === 1.95 && state().appearances.slice(0, 3).every(look => look.Scale === 3.1), 'Turning off shared size restores independent edits');
  await click('nav [data-page="motion"]');
  await change('input[aria-label="Сглаживание"]', 210);
  check(state().settings.Smoothing === 210, 'Motion slider reaches backend');
  await change('input[aria-label="Плотность размытия"]', 3.25);
  check(state().settings.BlurDensity === 3.25, 'Exposure density slider reaches native renderer settings');
  check(!document.querySelector('input[aria-label="Затухание пружины"]') && !document.querySelector('input[aria-label="Параллакс объектов"]'), 'Motion panel omits obsolete spring label and interface-only parallax');
  await click('.settings-panel .native-details summary');
  const dampingBefore=state().settings.Damping;
  await change('input[aria-label="Сглаживание"]', 0);
  check(document.querySelector('input[aria-label="Затухание наклона"]').disabled, 'Damping is unavailable without smoothing');
  await change('input[aria-label="Затухание наклона"]', .4);
  check(state().settings.Damping === dampingBefore, 'Disabled damping retains saved value');
  await change('input[aria-label="Сглаживание"]', 210);
  check(!document.querySelector('input[aria-label="Затухание наклона"]').disabled, 'Smoothing re-enables relevant tilt damping');
  await change('input[aria-label="Наклон при движении"]', 0);
  check(document.querySelector('input[aria-label="Затухание наклона"]').disabled, 'Zero tilt disables tilt-only damping');
  await change('input[aria-label="Угол затвора"]', 0);
  check(['Плотность размытия','Затухание шлейфа','Качество шлейфа','База экспозиции','Сглаживание'].every(label=>document.querySelector('input[aria-label="'+label+'"]').disabled), 'Zero shutter and tilt disable dependent effects');
  const densityBefore=state().settings.BlurDensity;
  await change('input[aria-label="Плотность размытия"]', 1);
  check(state().settings.BlurDensity === densityBefore, 'Disabled blur control preserves profile');
  await change('input[aria-label="Наклон при движении"]', 4);
  check(!document.querySelector('input[aria-label="Сглаживание"]').disabled, 'Smoothing still controls tilt without blur');
  await change('input[aria-label="Угол затвора"]', 270);
  check(['Плотность размытия','Затухание шлейфа','Качество шлейфа','База экспозиции'].every(label=>!document.querySelector('input[aria-label="'+label+'"]').disabled), 'Restoring shutter re-enables blur controls without resetting values');

  await click('.settings-panel .segments button:nth-child(3)');
  check(state().settings.Shutter === 360 && state().appearances[1].Scale === 3.1, 'Preset preserves appearances');
  await click('nav [data-page="space"]');
  await change('select[aria-label="Область действия"]', -1);
  await change('input[aria-label="Область: X"]', -1600);
  check(state().settings.Monitor === -1 && state().settings.CustomX === -1600, 'Custom region reaches backend');
  await change('input[aria-label="Плотность стекла"]', 45);
  check(state().settings.WindowTint === .45, 'Density persists natively');
  await click('[aria-label="Специальные формы"]');
  check(state().settings.PreserveSpecialCursors, 'Special cursor policy reaches backend');
  await click('[aria-label="Меньше движения"]');
  check(state().settings.ReduceMotion && document.querySelector('.reduced-motion'), 'Reduced motion persists');
  check(document.querySelector('input[aria-label="Параллакс объектов"]').disabled, 'Reduced interface motion disables decorative parallax');
  const parallaxBefore=state().settings.InterfaceParallax;
  await change('input[aria-label="Параллакс объектов"]', .5);
  check(state().settings.InterfaceParallax===parallaxBefore, 'Unavailable parallax does not overwrite stored preference');

  await window.__showPage('cursor');
  check(!document.getAnimations().some(animation => animation.animationName?.startsWith('page-')), 'Reduced motion skips page swipes');
  await window.__showPage('space');
  check(!document.querySelector('canvas') && !document.body.textContent.includes('Тест интерфейса'), 'No live cursor renderer or demo-only controls');
  await click('.settings-panel .native-details summary');
  await click('[aria-label="Совместимость с NVIDIA"]');
  check(state().settings.CaptureCompatibility, 'Compact capture setting reaches native backend');
  await click('[aria-label="Совместимость с NVIDIA"]');
  check(!state().settings.CaptureCompatibility, 'Normal publication can be restored');
  await change('select[aria-label="Режим формы"]', 2);
  await change('input[aria-label="Частота отрисовки"]', 144);
  check(state().settings.Style === 2 && state().settings.Fps === 144, 'Forced shape and render rate reach backend');
  const beforeResetCancel = JSON.stringify(state().settings);
  await click('[aria-label="Вернуть стандартные настройки"]');
  await click('dialog[open] .native-profile-actions button:first-child');
  check(JSON.stringify(state().settings) === beforeResetCancel, 'Canceling the visible global reset preserves the complete profile');
  await click('nav [data-page="cursor"]');
  await click('[aria-label="Вернуть стандартные настройки"]');
  await click('dialog[open] .native-profile-actions button:nth-child(2)');
  check(state().settings.WindowTint === .35 && !state().settings.ReduceMotion, 'Confirmed reset reaches native settings');
  check(JSON.stringify(state().settings) === JSON.stringify(defaultSettings), 'Global reset restores every default including sizes, colors, motion and display settings');
  check(!document.querySelector('[aria-label="Одинаковый размер всех форм"]').checked, 'Global reset clears linked-size UI mode');
  await click('nav [data-page="cursor"]');
  check(document.querySelector('.cursor-apply').disabled, 'Reset clears form draft');
  await click('nav [data-page="collection"]');
  await click('[data-pack="crystal"]');
  await settle();
  console.info('UI fonts and actual artwork');
  await document.fonts.ready;
  await Promise.all([...document.images].map(image => image.decode()));
  console.info('UI artwork decoded');
  check(document.fonts.check('14px Inter'), 'Local font loads');
  check([...document.images].every(image => image.complete && image.naturalWidth > 0), 'Local file artwork loads');
  check(typeof window.require === 'undefined' && typeof window.process === 'undefined', 'Renderer sandbox retained');
  const pageFrames = [];
  const panelFrames = [];
  for (const page of ['collection', 'motion', 'cursor', 'space']) {
    console.info('UI geometry: '+page);
    await window.__showPage(page);
    await settle();
    pageFrames.push({ page, nav: document.querySelector('nav').getBoundingClientRect().toJSON(), heading: document.querySelector('.page-heading').getBoundingClientRect().toJSON(), titleSize: getComputedStyle(document.querySelector('h1')).fontSize });
    if (page !== 'collection') {
      const panel = document.querySelector('.settings-panel').getBoundingClientRect();
      const scroll = document.querySelector('.settings-scroll').getBoundingClientRect();
      panelFrames.push({ page, ...panel.toJSON() });
      check(scroll.top >= panel.top + 11 && scroll.bottom <= panel.bottom - 11 && scroll.right <= panel.right - 11, `Scrollbar stays inside ${page} panel`);
      check(getComputedStyle(document.querySelector('.settings-art .cursor-artwork')).transform === 'none', 'Settings artwork has no resting compositing transform');
      if (page === 'cursor') verifyCoreSpacing(document.querySelector('.settings-art'), check, 'Cursor settings');
      await click('.settings-panel .native-details summary');
      const expanded = document.querySelector('.settings-panel').getBoundingClientRect();
      check(['x', 'y', 'width', 'height'].every(property => Math.abs(expanded[property] - panel[property]) < .1), `Expanded ${page} settings do not resize or move the glass shell`);
    }
  }
  check(pageFrames.every(frame => Math.abs(frame.nav.y - pageFrames[0].nav.y) < .1 && Math.abs(frame.nav.height - pageFrames[0].nav.height) < .1 && Math.abs(frame.heading.x - pageFrames[0].heading.x) < .1 && Math.abs(frame.heading.y - pageFrames[0].heading.y) < .1 && frame.titleSize === pageFrames[0].titleSize), 'All pages share identical navigation and heading anchors');
  check(panelFrames.every(frame => ['x', 'y', 'width', 'height'].every(property => Math.abs(frame[property] - panelFrames[0][property]) < .1)), 'All three glass settings shells have identical position and size');
  await window.__showPage('collection');
  await settle();
  const stage = document.querySelector('.specimen-stage .settings-illustration');
  const canvas = stage.querySelector('.illustration-canvas');
  const unscaledWidth = Math.min(stage.clientWidth, stage.clientHeight * 600 / 270);
  check(Math.abs(canvas.getBoundingClientRect().width / unscaledWidth - .8) < .01, 'Collection art is exactly 80 percent of the available unscaled frame');
  const forward = window.__showPage('cursor');
  console.info('UI forward swipe');
  await window.__pageTransitionReady?.();
  await new Promise(resolve => requestAnimationFrame(resolve));
  const forwardAnimations = document.getAnimations().map(animation => animation.animationName).filter(Boolean);
  console.info('Forward animations: '+JSON.stringify(forwardAnimations));
  check(document.documentElement.dataset.pageDirection === 'forward', 'Rightward tab navigation chooses forward swipe');
  if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) check(forwardAnimations.includes('page-enter-forward') && forwardAnimations.includes('page-exit-forward'), 'Forward swipe animates both old and new page snapshots');
  await forward;
  const backward = window.__showPage('motion');
  console.info('UI backward swipe');
  await window.__pageTransitionReady?.();
  await new Promise(resolve => requestAnimationFrame(resolve));
  const backwardAnimations = document.getAnimations().map(animation => animation.animationName).filter(Boolean);
  console.info('Backward animations: '+JSON.stringify(backwardAnimations));
  check(document.documentElement.dataset.pageDirection === 'backward', 'Leftward tab navigation chooses backward swipe');
  if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) check(backwardAnimations.includes('page-enter-backward') && backwardAnimations.includes('page-exit-backward'), 'Backward swipe reverses both page animations');
  await backward;
  const interrupted = window.__showPage('space');
  const latest = window.__showPage('collection');
  await Promise.all([interrupted, latest]);
  check(document.querySelector('.studio').dataset.page === 'collection', 'Rapid navigation ends on latest requested page');
  check(document.querySelector('[data-pack="crystal"]').getAttribute('aria-pressed') === 'true', 'Swiping preserves collection selection');
  const hoverContours = await verifySelectionContours(check);

  await window.__showPage('space'); await settle();
  const savedFullPreset = JSON.stringify(state().settings);
  await change('input[aria-label="Название пресета"]', 'Мой тестовый');
  await click('.preset-create button');
  check(state().presets.length === 1 && state().presets[0].name === 'Мой тестовый', 'Preset saved from visible UI');
  await change('input[aria-label="Плотность стекла"]', 61);
  await click('[aria-label="Применить пресет Мой тестовый"]');
  check(JSON.stringify(state().settings) === savedFullPreset, 'Visible preset restores full native profile');
  await click('[aria-label="Удалить пресет Мой тестовый"]');
  await click('dialog[open] .native-profile-actions button:first-child');
  check(state().presets.length === 1, 'Canceling deletion retains preset');
  await click('[aria-label="Удалить пресет Мой тестовый"]');
  await click('dialog[open] .native-profile-actions button:nth-child(2)');
  check(state().presets.length === 0 && JSON.stringify(state().settings) === savedFullPreset, 'Deleting preset preserves applied settings');
  await window.__showPage('cursor'); await settle();
  const appearanceDetails=document.querySelector('.settings-panel .native-details');
  if(!appearanceDetails.open)await click('.settings-panel .native-details summary');
  await change('input[aria-label="Цвет тени, непрозрачность"]', 0);
  check(['Мягкость тени','Смещение тени X','Смещение тени Y'].every(label=>document.querySelector('input[aria-label="'+label+'"]').disabled), 'Invisible shadow disables controls that cannot affect pixels');
  await change('input[aria-label="Цвет тени, непрозрачность"]', 50);
  check(['Мягкость тени','Смещение тени X','Смещение тени Y'].every(label=>!document.querySelector('input[aria-label="'+label+'"]').disabled), 'Restoring shadow opacity restores controls');
  for (const page of ['motion', 'cursor', 'space']) {
    await window.__showPage(page); await settle();
    check([...document.querySelectorAll('.range-control input')].every(input => document.getElementById(input.getAttribute('aria-describedby'))?.textContent.length > 12), 'Visible descriptions for every slider in '+page);
  }
  await window.__showPage('collection'); await settle();
  return { assertions, independentForms: true, atomicApply: true, nativeSettings: true, sandbox: true, pageFrames, panelFrames, hoverContours, directionalSwipe: true, containedScrollbars: true };
}

async function verifySelectionContours(check) {
  const immediate = document.createElement('style');
  immediate.textContent = '.page-collection .cursor-artwork{transition:none!important}';
  document.head.append(immediate);
  const frames = [];
  const nextFrame = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  try {
    for (const material of ['crystal', 'aurora', 'carbon', 'rose', 'classic']) {
      console.info('UI hover contour: '+material);
      document.querySelector(`[data-pack="${material}"]`).click();
      await nextFrame();
      const lens = document.querySelector('.selection-lens');
      for (const animation of lens.getAnimations()) animation.finish();
      const frame = lens.getBoundingClientRect();
      const radius = parseFloat(getComputedStyle(lens).borderTopLeftRadius);
      const inset = 8;
      const inside = point => {
        const left = frame.left + inset;
        const top = frame.top + inset;
        const right = frame.right - inset;
        const bottom = frame.bottom - inset;
        const innerRadius = Math.max(0, Math.min(radius - inset, (right - left) / 2, (bottom - top) / 2));
        if (point.x < left || point.x > right || point.y < top || point.y > bottom) return false;
        const horizontal = Math.max(left + innerRadius - point.x, 0, point.x - right + innerRadius);
        const vertical = Math.max(top + innerRadius - point.y, 0, point.y - bottom + innerRadius);
        return horizontal ** 2 + vertical ** 2 <= innerRadius ** 2 + .1;
      };
      const artwork = document.querySelector(`[data-pack="${material}"] .artwork-space`);
      verifyCoreSpacing(artwork, check, `${material} collection`);
      const markers = [];
      for (const image of artwork.querySelectorAll('img[data-form]')) {
        const metadata = JSON.parse(image.dataset.artworkMeta);
        for (const horizontal of [metadata.bounds.left, metadata.bounds.right]) {
          for (const vertical of [metadata.bounds.top, metadata.bounds.bottom]) {
            const marker = document.createElement('span');
            marker.style.cssText = `position:absolute;width:0;height:0;pointer-events:none;left:${image.offsetLeft + horizontal / metadata.width * image.offsetWidth}px;top:${image.offsetTop + vertical / metadata.height * image.offsetHeight}px`;
            image.parentElement.append(marker);
            markers.push(marker);
          }
        }
      }
      try {
        for (const [horizontal, vertical] of [[.02, .02], [.98, .02], [.98, .98], [.02, .98]]) {
          console.info(`UI hover angle: ${horizontal},${vertical}`);
          const bounds = artwork.getBoundingClientRect();
          artwork.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse', clientX: bounds.left + bounds.width * horizontal, clientY: bounds.top + bounds.height * vertical }));
          await nextFrame();
          check(markers.every(marker => inside(marker.getBoundingClientRect())), `${material} cursor cores retain eight pixels of rounded-frame clearance at hover ${horizontal},${vertical}`);
        }
      } finally {
        for (const marker of markers) marker.remove();
        artwork.dispatchEvent(new PointerEvent('pointerout', { bubbles: true, pointerType: 'mouse', relatedTarget: null }));
      }
      frames.push({ material, radius, width: frame.width, height: frame.height });
    }
  } finally {
    immediate.remove();
    document.querySelector('[data-pack="crystal"]').click();
  }
  verifyCoreSpacing(document.querySelector('.dock-art'), check, 'Application dock');
  return frames;
}

function verifyCoreSpacing(root, check, label) {
  const canvas = root.querySelector('.illustration-canvas').getBoundingClientRect();
  const boxes = [...root.querySelectorAll('img[data-form]')].map(image => {
    const meta = JSON.parse(image.dataset.artworkMeta), box = image.getBoundingClientRect();
    return { left: box.left + meta.bounds.left / meta.width * box.width, right: box.left + meta.bounds.right / meta.width * box.width,
      top: box.top + meta.bounds.top / meta.height * box.height, bottom: box.top + meta.bounds.bottom / meta.height * box.height };
  });
  const gaps = boxes.slice(1).map((box, index) => box.left - boxes[index].right);
  check(boxes.length === 4 && Math.min(...gaps) >= canvas.width / 600 * 24 - .1 && Math.max(...gaps) - Math.min(...gaps) < .5, `${label}: four separated silhouettes with even visible gaps`);
  check(Math.abs((boxes[0].left + boxes.at(-1).right) / 2 - (canvas.left + canvas.width / 2)) < .5, `${label}: silhouette group is horizontally centered`);
  check(Math.max(...boxes.map(box => box.bottom)) - Math.min(...boxes.map(box => box.bottom)) < .5 && Math.abs((Math.min(...boxes.map(box => box.top)) + boxes[0].bottom) / 2 - (canvas.top + canvas.height / 2)) < 2, `${label}: common baseline and optically centered height`);
}
