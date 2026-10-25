import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { optionHelp } from './option-help.mjs';
import { PresetLibrary } from './PresetLibrary.jsx';
import { ArrowRight, Check, Cursor, Diamond, Monitor, SlidersHorizontal, WaveSine, X, Minus, Square, TelegramLogo } from '@phosphor-icons/react';
import { SettingsIllustration } from './SettingsIllustration.jsx';
import { artworkPath, collectionArtworkFrame, createCursorMix, cursorForms, materialIds, packIdForMix } from './settings-artwork.mjs';
import { useNativeStudio } from './useNativeStudio.jsx';
import { formLabels, mixFromAppearances, packForAppearances, replaceFormMaterial, sameAppearances, skinIds, withSharedExactSize, visibleCursorSize, maximumCursorSize } from './native-model.mjs';
import { runStudioTests } from './studio-tests.mjs';
import { usePageNavigation } from './usePageNavigation.jsx';
import { useNativeArtwork, waitForNativeArtwork } from './useNativeArtwork.jsx';

const packs = [
  { id: 'crystal', name: 'Crystal', material: 'Жидкое стекло', detail: 'холодные блики', accent: '#1184f5' },
  { id: 'aurora', name: 'Aurora', material: 'Переливчатое стекло', detail: 'сирень и мята', accent: '#9e72e9' },
  { id: 'carbon', name: 'Carbon', material: 'Графит', detail: 'точная светлая кромка', accent: '#687387' },
  { id: 'rose', name: 'Rose', material: 'Тёплый металл', detail: 'мягкий блеск', accent: '#d58f76' },
  { id: 'classic', name: 'Classic', material: 'Исходные формы', detail: 'чёрный и белый', accent: '#79818f' },
  { id: 'custom', name: 'Свой набор', material: 'Твои формы, палитра и эффекты', detail: '', accent: '#8995a7' },
];

const navigation = [
  { id: 'collection', label: 'Коллекция', icon: Diamond },
  { id: 'motion', label: 'Движение', icon: WaveSine },
  { id: 'cursor', label: 'Курсор', icon: Cursor },
  { id: 'space', label: 'Пространство', icon: Monitor },
];

const collectionDescriptions = { classic: 'Чёрный и белый', custom: 'Своё оформление' };
const decodedArtwork = new Map();

function prepareArtwork(source) {
  if (!decodedArtwork.has(source)) {
    const image = new Image();
    image.src = source;
    decodedArtwork.set(source, image.decode().catch(() => { decodedArtwork.delete(source); }));
  }
  return decodedArtwork.get(source);
}

function useReducedMotion() {
  const [reduced, setReduced] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  useEffect(() => {
    const media = window.matchMedia('(prefers-reduced-motion: reduce)');
    const update = () => setReduced(media.matches);
    media.addEventListener('change', update);
    return () => media.removeEventListener('change', update);
  }, []);
  return reduced;
}

function TiltArtwork({ pack, className = '', intensity = 1, reduced = false, scale = 1, response = 550, contained = false, aspectRatio = 2, children }) {
  const artwork = useRef(null);
  const frame = useRef(null);
  useEffect(() => () => cancelAnimationFrame(frame.current), []);
  useEffect(() => {
    if ((reduced || intensity === 0) && artwork.current) {
      artwork.current.style.setProperty('--rotate-x', '0deg');
      artwork.current.style.setProperty('--rotate-y', '0deg');
      artwork.current.style.setProperty('--lift', '0px');
      artwork.current.parentElement.dataset.tilted = 'false';
    }
  }, [reduced, intensity]);
  function move(event) {
    if (reduced || intensity === 0 || event.pointerType === 'touch') return;
    const bounds = event.currentTarget.getBoundingClientRect();
    const horizontal = Math.max(-0.5, Math.min(0.5, (event.clientX - bounds.left) / bounds.width - 0.5));
    const vertical = Math.max(-0.5, Math.min(0.5, (event.clientY - bounds.top) / bounds.height - 0.5));
    cancelAnimationFrame(frame.current);
    frame.current = requestAnimationFrame(() => {
      if (!artwork.current) return;
      artwork.current.parentElement.dataset.tilted = 'true';
      artwork.current.style.setProperty('--rotate-x', `${-vertical * (contained ? 5 : 11) * intensity}deg`);
      artwork.current.style.setProperty('--rotate-y', `${horizontal * (contained ? 7 : 13) * intensity}deg`);
      artwork.current.style.setProperty('--lift', contained ? '-3px' : '-6px');
    });
  }
  function leave() {
    cancelAnimationFrame(frame.current);
    if (!artwork.current) return;
    artwork.current.style.setProperty('--rotate-x', '0deg');
    artwork.current.style.setProperty('--rotate-y', '0deg');
    artwork.current.style.setProperty('--lift', '0px');
    artwork.current.parentElement.dataset.tilted = 'false';
  }
  const style = { '--art-scale': scale, transitionDuration: `${response}ms` };
  return <span className={`artwork-space ${className}`} data-aspect-ratio={children ? aspectRatio : undefined} onPointerMove={move} onPointerLeave={leave}>{children ? <span ref={artwork} className="cursor-artwork composed-artwork" style={style}>{children}</span> : <img ref={artwork} className="cursor-artwork" style={style} src={artworkPath(pack.id === 'custom' ? 'classic' : pack.id, 'arrow')} alt={`${pack.name}: реальный указатель`} draggable="false" />}</span>;
}

function RangeControl({ label, value, min, max, step = 1, unit = '', onChange, description = optionHelp[label], disabled = false, disabledReason = '', marker = null }) {
  const hintId = useId();
  const progress = ((value - min) / (max - min)) * 100;
  const help = disabled && disabledReason ? `${description || ''} ${disabledReason}`.trim() : description;
  return <label className="range-control" data-disabled={disabled || undefined}><span className="range-label"><span>{label}</span><output>{Number(value.toFixed(3))}{unit}</output></span><span className="range-track"><input type="range" aria-label={label} aria-describedby={help ? hintId : undefined} disabled={disabled} min={min} max={max} step={step} value={value} onChange={event => { if (!disabled) onChange(Number(event.target.value)); }} style={{ '--range-progress': `${progress}%` }} />{marker != null && <i className="range-known-marker" style={{left:`calc(${((marker-min)/(max-min))*100}% + ${11.5-23*(marker-min)/(max-min)}px)`}} aria-hidden="true" />}</span>{help && <span id={hintId} className="range-description">{help}</span>}</label>;
}

export function App() {
  const native = useNativeStudio();
  if (!native.model) return <div className="studio"><div className="pearl-backdrop" /><WindowChrome /><div className="native-loading" role="status"><h1>CursorMotion</h1><p>{native.error || 'Подключаем движок…'}</p>{native.error && <button className="secondary-button" onClick={() => native.client.load().catch(() => {})}>Повторить подключение</button>}</div></div>;
  return <Studio native={native} />;
}

function WindowChrome() {
  return <div className="native-titlebar"><span>CursorMotion · Liquid Studio</span><div>{[['minimize', 'Свернуть', Minus], ['maximize', 'Развернуть', Square], ['close', 'Закрыть', X]].map(([action, label, Icon]) => <button key={action} aria-label={label} onClick={() => window.studio?.window(action)}><Icon size={15} /></button>)}</div></div>;
}

function ColorControl({ label, value, onChange }) {
  const hintId = useId();
  const alpha = Math.round(parseInt(value.slice(1, 3), 16) / 255 * 100);
  return <div className="native-color"><label>{label}<input type="color" aria-label={label} aria-describedby={hintId} value={`#${value.slice(3)}`} onChange={event => onChange(`${value.slice(0, 3)}${event.target.value.slice(1)}`.toUpperCase())} /></label><label>Непрозрачность<input type="number" aria-label={`${label}, непрозрачность`} aria-describedby={hintId} min={0} max={100} step={1} value={alpha} onChange={event => onChange(`#${Math.round(Math.max(0, Math.min(100, Number(event.target.value))) * 255 / 100).toString(16).padStart(2, '0')}${value.slice(3)}`.toUpperCase())} /><span>%</span></label><p id={hintId} className="option-help">{optionHelp[label]}</p></div>;
}

function Studio({ native }) {
  const { model, client, busy, error } = native;
  const settings = model.settings;
  const [selected, setSelected] = useState(() => packForAppearances(model.appearances, model.packs));
  const applied = packForAppearances(model.appearances, model.packs);
  const enabled = model.state.enabled && !model.state.error && !error;
  const [notice, setNotice] = useState('');
  const [motionPreset, setMotionPreset] = useState('');
  const smoothing = settings.Smoothing;
  const damping = settings.Damping;
  const shutter = settings.Shutter;
  const parallax = settings.InterfaceParallax;
  const [shape, setShape] = useState('Стрелка');
  const [draft, setDraft] = useState(() => structuredClone(model.appearances));
  const [sharedSize, setSharedSize] = useState(false);
  const draftArtwork = useNativeArtwork(client, draft);
  const appliedArtwork = useNativeArtwork(client, model.appearances);
  const shapeIndex = formLabels.indexOf(shape);
  const appearance = draft[shapeIndex];
  const density = Math.round(settings.WindowTint * 100);
  const special = settings.PreserveSpecialCursors;
  const [customOpen, setCustomOpen] = useState(false);
  const mix = mixFromAppearances(draft);
  const [mixConfirmed, setMixConfirmed] = useState(false);
  const appliedMix = mixFromAppearances(model.appearances);
  const lessMotion = settings.ReduceMotion;
  const [committing, setCommitting] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const systemReduced = useReducedMotion();
  const reduced = systemReduced || lessMotion;
  const blurOff = shutter === 0;
  const dampingOff = smoothing === 0 || settings.Tilt === 0;
  const legacyBlur = model.state.motionKernel?.startsWith('legacy-') ?? false;
  const shadowOff = parseInt(appearance.ShadowColor.slice(1, 3), 16) === 0;
  const preparePage = useCallback(next => {
    const sources = next === 'collection' ? materialIds.flatMap(material => cursorForms.map(form => artworkPath(material, form.id))) : next === 'cursor' ? cursorForms.map(form => artworkPath(mix[form.label], form.id)) : [artworkPath(appliedMix.Стрелка, next)];
    return Promise.all(sources.map(prepareArtwork));
  }, [mix.Стрелка, mix.Текст, mix.Ожидание, mix.Рука, appliedMix.Стрелка]);
  const { page, navigate: setPage, whenSettled, whenReady } = usePageNavigation(reduced, preparePage);
  const gallery = useRef(null);
  const lens = useRef(null);
  const lensAnimation = useRef(null);
  const lensBounds = useRef(null);
  const nav = useRef(null);
  const navLens = useRef(null);
  const actionButton = useRef(null);
  const noticeTimeout = useRef(null);
  const dialog = useRef(null);
  const selectedPack = packs.find(pack => pack.id === selected);
  const appliedPack = packs.find(pack => pack.id === applied);
  const alreadyApplied = selected === applied && (selected !== 'custom' || sameAppearances(draft, model.appearances));
  const cursorHasChanges = !sameAppearances(draft, model.appearances);
  const previewPack = page === 'cursor' ? packs.find(pack => pack.id === packForAppearances(draft, model.packs)) : appliedPack;

  function updateAppearance(property, value) {
    setDraft(current => {
      if (property !== 'Scale') return current.map((item,index)=>index===shapeIndex?{...item,[property]:value}:item);
      const actual=model.state.compactRequired ? current.map(item=>({...item,Scale:visibleCursorSize(item,model.state),ExactSize:true})) : current;
      return sharedSize ? withSharedExactSize(actual,value) : actual.map((item,index)=>index===shapeIndex?{...item,Scale:value,ExactSize:true}:item);
    });
    setMixConfirmed(false);
  }

  function globalChange(property, value) {
    client.updateGlobal(property, value);
    if (['Smoothing', 'Damping', 'Shutter', 'Tilt', 'ClickScale', 'TrailFalloff', 'BlurDensity', 'MaxSamples'].includes(property)) setMotionPreset('');
  }

  async function commit(operation, message, replaceDraft = false) {
    setCommitting(true);
    try {
      const result = await operation();
      if (replaceDraft && result?.appearances) {
        setDraft(structuredClone(result.appearances));
        if (!result.appearances.every(item => item.Scale === result.appearances[0].Scale)) setSharedSize(false);
        setSelected(packForAppearances(result.appearances, result.packs));
        setMixConfirmed(true);
      }
      if (result !== null && message) announce(message);
      return result;
    } catch (failure) {
      announce(failure.message);
      return null;
    } finally { setCommitting(false); }
  }

  useEffect(() => {
    window.__studioReady = true;
    window.__showPage = (next, options) => setPage(next === 'screen' ? 'space' : next, options);
    window.__runStudioTests = () => runStudioTests(client);
    window.__flushNativeSettings = () => client.flush();
    window.__pageTransitionFinished = whenSettled;
    window.__pageTransitionReady = whenReady;
    window.__waitNativeArtwork = waitForNativeArtwork;
    return () => { window.__studioReady = false; delete window.__showPage; delete window.__runStudioTests; delete window.__flushNativeSettings; delete window.__pageTransitionFinished; delete window.__pageTransitionReady; delete window.__waitNativeArtwork; };
  }, [client, setPage, whenSettled, whenReady]);

  useEffect(() => () => clearTimeout(noticeTimeout.current), []);

  function announce(message) {
    setNotice(message);
    clearTimeout(noticeTimeout.current);
    noticeTimeout.current = setTimeout(() => setNotice(''), 2800);
  }

  useLayoutEffect(() => {
    if (page !== 'collection' || !gallery.current || !lens.current) return;
    if (reduced) lensAnimation.current?.cancel();
    const target = gallery.current.querySelector(`[data-pack="${selected}"] .specimen-stage`);
    const artwork = target?.querySelector('.artwork-space');
    const image = artwork?.querySelector('img');
    if (!target || !artwork || !image) return;
    function place() {
      if ((!artwork.dataset.aspectRatio && (!image.naturalWidth || !image.naturalHeight)) || !lens.current) return;
      const galleryBounds = gallery.current.getBoundingClientRect();
      const targetBounds = target.getBoundingClientRect();
      const artworkStyle = getComputedStyle(artwork);
      const horizontalInset = parseFloat(artworkStyle.paddingLeft) + parseFloat(artworkStyle.paddingRight);
      const verticalInset = parseFloat(artworkStyle.paddingTop) + parseFloat(artworkStyle.paddingBottom);
      const aspectRatio = Number(artwork.dataset.aspectRatio) || image.naturalWidth / image.naturalHeight;
      const canvas = artwork.querySelector('.illustration-canvas');
      const imageWidth = canvas ? canvas.offsetWidth : image.clientWidth;
      const imageHeight = canvas ? canvas.offsetHeight : image.clientHeight;
      const width = Math.min(targetBounds.width, Math.min(imageWidth, imageHeight * aspectRatio) + horizontalInset);
      const height = Math.min(targetBounds.height, Math.min(imageHeight, imageWidth / aspectRatio) + verticalInset);
      const bounds = [targetBounds.left - galleryBounds.left + (targetBounds.width - width) / 2, targetBounds.top - galleryBounds.top + (targetBounds.height - height) / 2, width, height];
      if (lens.current.style.transform && lensBounds.current?.every((value, index) => Math.abs(value - bounds[index]) < .25)) return;
      lensBounds.current = bounds;
      const next = `translate3d(${bounds[0]}px, ${bounds[1]}px, 0px)`;
      const nextWidth = `${width}px`;
      const nextHeight = `${height}px`;
      const previous = getComputedStyle(lens.current).transform;
      lensAnimation.current?.cancel();
      lens.current.style.width = nextWidth;
      lens.current.style.height = nextHeight;
      lens.current.style.transform = next;
      if (!reduced && previous !== 'none') {
        lensAnimation.current = lens.current.animate([{ transform: previous }, { transform: next }], { duration: 620, easing: 'cubic-bezier(.2,.85,.2,1)' });
      }
    }
    image.addEventListener('load', place);
    place();
    const observer = new ResizeObserver(place);
    observer.observe(gallery.current);
    observer.observe(target);
    observer.observe(artwork);
    const canvas = artwork.querySelector('.illustration-canvas');
    if (canvas) observer.observe(canvas);
    return () => {
      observer.disconnect();
      image.removeEventListener('load', place);
    };
  }, [selected, page, reduced]);

  useLayoutEffect(() => {
    function place() {
      const active = nav.current?.querySelector(`[data-page="${page}"]`);
      if (!active || !navLens.current) return;
      navLens.current.style.width = `${active.offsetWidth}px`;
      navLens.current.style.height = `${active.offsetHeight}px`;
      navLens.current.style.transform = `translate3d(${active.offsetLeft}px, ${active.offsetTop}px, 0)`;
    }
    place();
    const observer = new ResizeObserver(place);
    if (nav.current) {
      observer.observe(nav.current);
      nav.current.querySelectorAll('button').forEach(button => observer.observe(button));
    }
    return () => observer.disconnect();
  }, [page]);

  useEffect(() => {
    if (!dialog.current) return;
    if (customOpen && !dialog.current.open) dialog.current.showModal();
    else if (!customOpen && dialog.current.open) dialog.current.close();
  }, [customOpen]);

  function choose(pack) {
    setSelected(pack.id);
    if (pack.id === 'custom') setCustomOpen(true);
  }

  async function applyPack() {
    if (selected === 'custom' && !mixConfirmed) { setCustomOpen(true); return; }
    if (alreadyApplied) return;
    const result = await commit(() => selected === 'custom' ? client.command('forms', { appearances: draft }) : client.command('pack', { id: skinIds[selected] }), `Набор ${selectedPack.name} применён`, true);
    if (!result) return;
    if (!reduced && actionButton.current) {
      actionButton.current.animate([{ transform: 'scale(.965)' }, { transform: 'scale(1.015)' }, { transform: 'scale(1)' }], { duration: 440, easing: 'cubic-bezier(.2,.8,.2,1)' });
    }
  }

  function changeFormMaterial(form, material) {
    setDraft(current => replaceFormMaterial(current, formLabels.indexOf(form), material, model.packs));
    setMixConfirmed(false);
  }

  async function applyCursorForms() {
    if (!cursorHasChanges) return;
    await commit(() => client.command('forms', { appearances: draft }), 'Формы курсора применены', true);
  }

  function galleryKeyDown(event, index) {
    const columns = getComputedStyle(gallery.current).gridTemplateColumns.split(' ').length;
    let next = index;
    if (event.key === 'ArrowRight') next = Math.min(index + 1, packs.length - 1);
    else if (event.key === 'ArrowLeft') next = Math.max(index - 1, 0);
    else if (event.key === 'ArrowDown') next = Math.min(index + columns, packs.length - 1);
    else if (event.key === 'ArrowUp') next = Math.max(index - columns, 0);
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = packs.length - 1;
    else return;
    event.preventDefault();
    setSelected(packs[next].id);
    gallery.current.querySelector(`[data-pack="${packs[next].id}"]`).focus();
  }

  async function choosePreset(name) {
    const result = await commit(() => client.command('preset', { name }), 'Характер движения применён');
    if (result) setMotionPreset(name);
  }

  async function saveUserPreset(name, id) {
    return commit(async () => {
      if (cursorHasChanges) await client.command('forms', { appearances: draft });
      return client.command('presets-save', { name, ...(id ? { id } : {}) });
    }, id ? 'Пресет обновлён' : 'Пресет сохранён', true);
  }

  async function applyUserPreset(preset) {
    const result = await commit(() => client.command('presets-apply', { id: preset.id }), `Пресет «${preset.name}» применён`, true);
    if (result) { setMotionPreset(''); setSharedSize(false); }
    return result;
  }

  return <div className={`studio ${reduced ? 'reduced-motion' : ''}`} data-page={page} style={{ '--glass-density': density / 100 }}>
    <div className="pearl-backdrop" aria-hidden="true" />
    <WindowChrome />
    <a className="skip-link" href="#main-content">К содержимому</a>
    <header className="topbar">
      <a className="brand" href="#collection" onClick={event => { event.preventDefault(); setPage('collection'); }} aria-label="CursorMotion, коллекция"><img src="./assets/brand.png" alt="" /><span><strong>CursorMotion</strong><small>by WAKUDO</small></span></a>
      <nav ref={nav} className="glass navigation" aria-label="Разделы студии"><span ref={navLens} className="nav-lens" aria-hidden="true" />{navigation.map(item => { const Icon = item.icon; return <button key={item.id} data-page={item.id} aria-current={page === item.id ? 'page' : undefined} className={page === item.id ? 'nav-active' : ''} onClick={() => setPage(item.id)}><Icon size={24} weight="regular" /><span>{item.label}</span></button>; })}</nav>
      <label className="effect-control" title={model.state.error || error || `${model.state.adapter} · ${Math.round(model.state.fps)} FPS`}><span>{model.state.error || error ? 'Ошибка движка' : enabled ? 'Эффект включён' : 'Эффект на паузе'}</span><button className="effect-switch" type="button" role="switch" aria-label="Эффект курсора" aria-checked={enabled} disabled={busy || committing} onClick={() => commit(() => client.command('enabled', { value: !enabled }), '')}><span /></button></label>
    </header>
    {(error || model.state.error || model.warning) && <div className="native-error" role="alert">{error || model.state.error || model.warning}</div>}
    <main id="main-content" className={`main-content page-${page}`}>
      <div className="page-heading"><h1>{navigation.find(item => item.id === page).label}</h1><p>{page === 'collection' ? <>Сейчас применяется <strong data-testid="applied-name">{appliedPack.name}</strong></> : page === 'motion' ? 'Настрой характер движения — от точного до невесомого.' : page === 'cursor' ? 'Четыре формы. Каждая со своим характером.' : 'Твоё пространство. Твой способ видеть.'}</p></div>
      {page === 'collection' ? <>
        <div ref={gallery} className="gallery" role="group" aria-label="Наборы курсоров">
          <span ref={lens} className="selection-lens" aria-hidden="true" />
          {packs.map((pack, index) => {
            const status = pack.id === applied && (pack.id !== 'custom' || sameAppearances(draft, model.appearances)) ? 'Применяется' : null;
            return <button key={pack.id} className={`specimen ${pack.id === selected ? 'is-selected' : ''}`} data-pack={pack.id} disabled={committing} aria-label={`${pack.name}, ${pack.material}${status ? ', применяется' : ''}`} aria-pressed={pack.id === selected} onClick={() => choose(pack)} onKeyDown={event => galleryKeyDown(event, index)} style={{ '--pack-accent': pack.accent }}>
              <span className="specimen-stage"><TiltArtwork pack={pack} intensity={parallax} reduced={reduced} aspectRatio={collectionArtworkFrame.aspectRatio} contained><SettingsIllustration page="cursor" mix={pack.id === 'custom' ? mix : createCursorMix(pack.id)} materials={packs} framing="collection" frames={pack.id === 'custom' ? draftArtwork.frames : pack.id === applied ? appliedArtwork.frames : {}} /></TiltArtwork></span>
              <span className="specimen-copy">
                <span className="specimen-title"><span>{pack.name}</span>{status && <span className="pack-check" role="img" aria-label={status} title={status}><Check size={16} weight="bold" /></span>}</span>
                <span className="specimen-description">{collectionDescriptions[pack.id] || pack.material}</span>
              </span>
            </button>;
          })}
        </div>
        <section className="glass action-dock" aria-label="Применение набора"><span className="dock-art"><SettingsIllustration page="cursor" mix={selected === 'custom' ? mix : createCursorMix(selected)} materials={packs} framing="collection" frames={selected === 'custom' ? draftArtwork.frames : selected === applied ? appliedArtwork.frames : {}} /></span><span className="dock-divider" /><div className="dock-copy" key={`copy-${selected}`}><span className="dock-title">{selectedPack.name}</span><span className="dock-description">{collectionDescriptions[selectedPack.id] || selectedPack.material}</span></div><button ref={actionButton} className={`primary-button ${alreadyApplied ? 'is-applied' : ''}`} disabled={alreadyApplied || committing} onClick={applyPack}><span>{committing ? 'Применяем…' : alreadyApplied ? 'Набор применён' : 'Применить набор'}</span>{alreadyApplied ? <Check size={25} weight="bold" /> : <ArrowRight size={27} />}</button></section>
      </> : <section className="settings-scene" aria-label={`Настройки: ${navigation.find(item => item.id === page).label}`}>
        <div className="settings-art"><TiltArtwork pack={previewPack} intensity={parallax} reduced={reduced} response={550}><SettingsIllustration page={page} material={appliedMix.Стрелка} mix={mix} materials={packs} frames={page === 'cursor' ? draftArtwork.frames : appliedArtwork.frames} /></TiltArtwork><span>{previewPack.name}</span><small>{(page === 'cursor' ? draftArtwork : appliedArtwork).loading ? 'Обновляем образцы…' : (page === 'cursor' ? draftArtwork : appliedArtwork).error ? 'Не удалось обновить образец' : page === 'cursor' && cursorHasChanges ? 'Образцы из движка · не применено' : 'Увеличенные образцы из движка'}</small></div>
        <div className="glass settings-panel" inert={committing || undefined}><div className="settings-scroll" key={page}>
          {page === 'motion' ? <>
            <h2>Ощущение движения</h2><p className="panel-intro">Мягкость в деталях, точность под рукой.</p>
            <div className="segments" aria-label="Характер движения">{['Точный', 'Сбалансированный', 'Кино'].map(name => <button key={name} aria-pressed={motionPreset === name} onClick={() => choosePreset(name)}>{name}</button>)}</div>
            <RangeControl label="Сглаживание" value={smoothing} min={0} max={500} disabled={blurOff && settings.Tilt === 0} disabledReason="Сейчас размытие и наклон выключены." onChange={value => globalChange('Smoothing', value)} />
            <RangeControl label="Угол затвора" value={shutter} min={0} max={720} unit="°" onChange={value => globalChange('Shutter', value)} />
            <RangeControl label="Плотность размытия" value={settings.BlurDensity ?? 2} min={0} max={4} step={.05} disabled={blurOff || legacyBlur} disabledReason={legacyBlur ? 'Недоступно: сейчас работает резервный рендер размытия.' : 'Чтобы настроить, увеличь угол затвора выше 0°.'} onChange={value => globalChange('BlurDensity', value)} />
            <details className="native-details"><summary>Шлейф и реакции</summary>
              <RangeControl label="Затухание шлейфа" value={settings.TrailFalloff} min={0} max={1} step={.01} disabled={blurOff} disabledReason="Размытие выключено: угол затвора равен 0°." onChange={value => globalChange('TrailFalloff', value)} />
              <RangeControl label="Качество шлейфа" value={settings.MaxSamples} min={4} max={128} disabled={blurOff} disabledReason="Размытие выключено: угол затвора равен 0°." onChange={value => globalChange('MaxSamples', value)} />
              <RangeControl label="Наклон при движении" value={settings.Tilt} min={0} max={20} step={.1} unit="°" onChange={value => globalChange('Tilt', value)} />
              <RangeControl label="Затухание наклона" value={damping} min={0.3} max={1.5} step={.01} disabled={dampingOff} disabledReason="Для настройки нужны сглаживание и наклон выше 0." onChange={value => globalChange('Damping', value)} />
              <RangeControl label="Размер при нажатии" value={settings.ClickScale} min={.5} max={1} step={.01} unit="×" onChange={value => globalChange('ClickScale', value)} />
              <RangeControl label="База экспозиции" value={settings.ShutterFps} min={24} max={240} unit=" FPS" disabled={blurOff} disabledReason="Размытие выключено: угол затвора равен 0°." onChange={value => globalChange('ShutterFps', value)} />
              <button className="secondary-button" onClick={() => choosePreset('Мгновенный')}>Без наклона и сглаживания <ArrowRight size={20} /></button>
            </details>
            <p className="native-hint">Движение применяется сразу и сохраняется автоматически.</p>
          </> : page === 'cursor' ? <>
            <h2>Форма и характер</h2><p className="panel-intro">Небольшие изменения, совсем другое ощущение.</p>
            <div className="segments" aria-label="Форма курсора">{cursorForms.map(form => <button key={form.id} aria-pressed={shape === form.label} onClick={() => setShape(form.label)}>{form.label}</button>)}</div>
            {shape === 'Стрелка' && <div className="resize-appearance-note"><span className="resize-native-samples" aria-hidden="true">{['resize-horizontal','resize-vertical','resize-nwse','resize-nesw'].map(subject => <img key={subject} data-renderer="native-sdf" src={draftArtwork.frames[subject]?.dataUri || artworkPath(mix.Стрелка, subject)} alt="" />)}</span><span>Стрелки изменения размера используют это оформление.</span></div>}
            <label className="select-control">Набор<select aria-label="Набор для формы" value={mix[shape]} onChange={event => changeFormMaterial(shape, event.target.value)}>{packs.filter(pack => pack.id !== 'custom').map(pack => <option key={pack.id} value={pack.id}>{pack.name}</option>)}</select><small className="option-help">Материал только для выбранной формы. Остальные формы не меняются.</small></label>
            <RangeControl label="Размер" value={visibleCursorSize(appearance,model.state)} min={.5} max={maximumCursorSize} step={.05} unit="×" onChange={value => updateAppearance('Scale', value)} />
            <p className="native-hint">Максимальный размер — {maximumCursorSize}× для каждой формы и общего размера.</p>
            <label className="size-link"><input type="checkbox" aria-label="Одинаковый размер всех форм" checked={sharedSize} onChange={event => { const linked = event.target.checked; setSharedSize(linked); if (linked) { setDraft(current => withSharedExactSize(current.map(item=>({...item,Scale:visibleCursorSize(item,model.state),ExactSize:true})), visibleCursorSize(current[shapeIndex],model.state))); setMixConfirmed(false); } }} /><span>Одинаковый размер всех форм<small className="option-help">Слайдер размера меняет стрелку, текст, ожидание и руку вместе.</small></span></label>
            <RangeControl label="Переход между формами" value={appearance.MorphTime} min={0} max={500} unit=" мс" onChange={value => updateAppearance('MorphTime', value)} />
            <button className={`primary-button cursor-apply ${cursorHasChanges ? '' : 'is-applied'}`} disabled={!cursorHasChanges} onClick={applyCursorForms}><span>{cursorHasChanges ? 'Применить формы' : 'Формы применены'}</span>{cursorHasChanges ? <ArrowRight size={22} /> : <Check size={22} weight="bold" />}</button>
            {cursorHasChanges && <button className="native-text-button" onClick={() => { setDraft(structuredClone(model.appearances)); setMixConfirmed(false); setSharedSize(false); }}>Отменить изменения форм</button>}
            <details className="native-details"><summary>Палитра, тень и поведение</summary>
              <ColorControl label="Заливка" value={appearance.FillColor} onChange={value => updateAppearance('FillColor', value)} />
              <ColorControl label="Обводка" value={appearance.OutlineColor} onChange={value => updateAppearance('OutlineColor', value)} />
              <RangeControl label="Толщина обводки" value={appearance.OutlineWidth} min={0} max={4} step={.1} onChange={value => updateAppearance('OutlineWidth', value)} />
              <ColorControl label="Цвет тени" value={appearance.ShadowColor} onChange={value => updateAppearance('ShadowColor', value)} />
              <RangeControl label="Мягкость тени" value={appearance.ShadowBlur} min={0} max={10} step={.1} disabled={shadowOff} disabledReason="Сначала увеличь непрозрачность цвета тени выше 0%." onChange={value => updateAppearance('ShadowBlur', value)} />
              <RangeControl label="Смещение тени X" value={appearance.ShadowX} min={-10} max={10} step={.1} disabled={shadowOff} disabledReason="Сначала увеличь непрозрачность цвета тени выше 0%." onChange={value => updateAppearance('ShadowX', value)} />
              <RangeControl label="Смещение тени Y" value={appearance.ShadowY} min={-10} max={10} step={.1} disabled={shadowOff} disabledReason="Сначала увеличь непрозрачность цвета тени выше 0%." onChange={value => updateAppearance('ShadowY', value)} />
              <RangeControl label="Скрывать в покое" value={appearance.IdleHide} min={0} max={30} step={.1} unit=" с"  onChange={value => updateAppearance('IdleHide', value)} />
              <p className="native-hint">Увеличенный образец отражает палитру, обводку и тень. Размер применяется к рабочему курсору; стрелки изменения размера следуют форме «Стрелка».</p>
            </details>
            <button className="secondary-button" onClick={() => { setSelected('custom'); setCustomOpen(true); }}>Собрать свой набор <ArrowRight size={20} /></button>
          </> : <>
            <h2>Стекло и пространство</h2><p className="panel-intro">Воздух, свет и только нужные границы.</p>
            <RangeControl label="Плотность стекла" value={density} min={15} max={85} unit="%" onChange={value => globalChange('WindowTint', value / 100)}  />
            <RangeControl label="Параллакс объектов" value={parallax} min={0} max={2} step={.1} unit="×" disabled={reduced} disabledReason="Сейчас анимации уменьшены настройкой приложения или Windows." onChange={value => globalChange('InterfaceParallax', value)} />
            <label className="select-control">Область действия<select aria-label="Область действия" value={settings.Monitor} onChange={event => globalChange('Monitor', Number(event.target.value))}><option value={-3}>Все экраны</option><option value={-2}>Основной экран</option>{model.displays.map(display => <option key={display.id} value={display.id}>{display.label}</option>)}<option value={-1}>Своя область</option>{settings.Monitor >= 0 && !model.displays.some(display => display.id === settings.Monitor) && <option value={settings.Monitor}>Монитор {settings.Monitor + 1} недоступен</option>}</select><small className="option-help">Где работают эффекты: на всех экранах, на одном или внутри своей области.</small></label>
            {settings.Monitor === -1 && <div className="native-region">{[['CustomX', 'X', -32768], ['CustomY', 'Y', -32768], ['CustomWidth', 'Ширина', 1], ['CustomHeight', 'Высота', 1]].map(([property, label, minimum]) => <label key={property}>{label}<input type="number" aria-label={`Область: ${label}`} min={minimum} max={32768} step={1} value={settings[property]} title={({CustomX:"Левая граница области в пикселях. На левом мониторе может быть отрицательной.",CustomY:"Верхняя граница области в пикселях.",CustomWidth:"Ширина своей области в пикселях.",CustomHeight:"Высота своей области в пикселях."})[property]} onChange={event => { if (event.target.value !== '') globalChange(property, Math.round(Math.max(minimum, Math.min(32768, Number(event.target.value))))); }} /></label>)}</div>}
            <div className="setting-switch-row"><span>Специальные формы<small>Оставлять системными редкие формы: например, запрет действия или точный прицел.</small></span><button className="effect-switch" role="switch" aria-checked={special} aria-label="Специальные формы" onClick={() => globalChange('PreserveSpecialCursors', !special)}><span /></button></div>
            <div className="setting-switch-row"><span>Меньше движения<small>Уменьшить анимации этого окна. Эффекты самого курсора не меняются.</small></span><button className="effect-switch" role="switch" aria-checked={lessMotion} aria-label="Меньше движения" onClick={() => globalChange('ReduceMotion', !lessMotion)}><span /></button></div>
            <div className="motion-preference"><SlidersHorizontal size={19} /><span>{systemReduced ? 'Уменьшение движения включено в системе' : lessMotion ? 'Анимации интерфейса уменьшены' : 'Анимации учитывают системные настройки'}</span></div>
            <PresetLibrary presets={model.presets} warning={model.presetWarning} busy={committing || busy} hasDraft={cursorHasChanges} onSave={saveUserPreset} onApply={applyUserPreset} onUpdate={preset => saveUserPreset(preset.name, preset.id)} onDelete={preset => commit(() => client.command('presets-delete', { id: preset.id }), 'Пресет удалён')} onImport={() => commit(() => client.file('import-preset'), 'Пресет добавлен в библиотеку')} onExport={preset => commit(() => client.file('export-preset', { id: preset.id }), 'Пресет экспортирован')} />
            <details className="native-details"><summary>Движок и профиль</summary>
              <StartupControl />
              <div className="setting-switch-row"><span>Ограничение для записи<small>{model.state.compactRequired ? 'На NVIDIA автоматически подбирается компактный кадр. Максимальный размер курсора — 4.5×.' : 'Убирает пустые поля кадра для записи и при необходимости сокращает слишком длинный шлейф.'}</small></span><button className="effect-switch" role="switch" aria-checked={!!settings.CaptureCompatibility || !!model.state.compactRequired} disabled={!!model.state.compactRequired} aria-label="Совместимость с NVIDIA" onClick={() => globalChange('CaptureCompatibility', !settings.CaptureCompatibility)}><span /></button></div>
              <label className="select-control">Режим формы<select aria-label="Режим формы" value={settings.Style} onChange={event => globalChange('Style', Number(event.target.value))}><option value={-1}>Автоматически</option><option value={0}>Всегда стрелка</option><option value={1}>Всегда текст</option><option value={2}>Всегда ожидание</option><option value={3}>Всегда рука</option></select><small className="option-help">«Автоматически» меняет форму по месту наведения. Другие варианты закрепляют одну форму.</small></label>
              <RangeControl label="Частота отрисовки" value={settings.Fps} min={30} max={240} unit=" FPS" onChange={value => globalChange('Fps', value)} />
              <p className="native-hint">{model.state.adapter}<br />{!model.state.enabled ? 'Эффект на паузе' : model.state.fps < 1 ? 'Покой · без повторной отрисовки' : `${Math.round(model.state.fps)} кадров/с · ${Number(model.state.renderMs).toFixed(1)} мс на кадр`}</p>
              <div className="native-profile-actions"><button disabled={committing} onClick={() => commit(() => client.file('import'), 'Профиль импортирован', true)}>Импорт</button><button disabled={committing} onClick={() => commit(() => client.file('export'), cursorHasChanges ? 'Сохранён применённый профиль. Черновик форм не экспортируется.' : 'Профиль экспортирован')}>Экспорт</button><button disabled={committing} onClick={() => setResetOpen(true)}>Сбросить</button></div>
              <p className="native-hint">Параметры сохраняются автоматически. Экспорт сохраняет только применённые формы.</p>
            </details>
          </>}
          <p className="native-hint native-shortcuts">Пауза: Ctrl + Alt + F8<br />Восстановить курсор и выйти: Ctrl + Alt + F12</p>
        </div></div>
      </section>}
    </main>
    <footer className="native-footer"><button onClick={() => window.studio.telegram()} aria-label="Telegram WAKUDO"><TelegramLogo size={17} /> WAKUDO</button><span>Пауза · Ctrl + Alt + F8</span><button onClick={async () => { await setPage('space'); document.getElementById('user-presets')?.scrollIntoView({ block: 'start', behavior: reduced ? 'instant' : 'smooth' }); }}>Пресеты и настройки</button><button aria-label="Вернуть стандартные настройки" onClick={() => setResetOpen(true)}>Стандартные настройки</button></footer>
    <div className={`toast glass ${notice ? 'is-visible' : ''}`} role="status" aria-live="polite"><Check size={19} /><span>{notice}</span></div>
    <dialog ref={dialog} className="glass mix-dialog" onCancel={() => setCustomOpen(false)} onClick={event => { if (event.target === dialog.current) setCustomOpen(false); }}><div className="dialog-heading"><div><h2>Твой микс</h2><p>Выбери материал для каждой формы.</p></div><button className="icon-button" aria-label="Закрыть свой набор" onClick={() => setCustomOpen(false)}><X size={23} /></button></div>{Object.keys(mix).map(name => <label key={name} className="select-control">{name}<select aria-label={`Материал: ${name}`} value={mix[name]} onChange={event => changeFormMaterial(name, event.target.value)}>{packs.filter(pack => pack.id !== 'custom').map(pack => <option key={pack.id} value={pack.id}>{pack.name}</option>)}</select></label>)}<button className="primary-button" onClick={() => { setMixConfirmed(true); setCustomOpen(false); announce('Свой набор готов к применению'); }}>Готово <Check size={22} /></button></dialog>
    <ResetDialog open={resetOpen} busy={committing} onCancel={() => setResetOpen(false)} onConfirm={async () => { const result = await commit(() => client.command('reset'), 'Стандартные настройки восстановлены', true); if (result) { setResetOpen(false); setMotionPreset(''); setSharedSize(false); } }} />
  </div>;
}

function StartupControl() {
  const [startup, setStartup] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { window.studio?.startup?.().then(setStartup).catch(failure => setError(failure.message)); }, []);
  if (!startup?.available) return error ? <p role="alert" className="native-hint">{error}</p> : null;
  async function toggle() {
    setBusy(true);
    try { setStartup(await window.studio.startup(!startup.enabled)); setError(''); }
    catch (failure) { setError(failure.message); }
    finally { setBusy(false); }
  }
  return <><div className="setting-switch-row"><span>Запускать с Windows<small>Сразу применять сохранённый курсор, без окна настроек.</small></span><button className="effect-switch" role="switch" aria-checked={startup.enabled} aria-label="Запускать с Windows" disabled={busy} onClick={toggle}><span /></button></div><p className="native-hint">Закрой окно — курсор продолжит работать. Настройки выгрузятся из памяти; открыть их снова можно из трея.</p>{error && <p className="native-hint" role="alert">{error}</p>}</>;
}

function ResetDialog({ open, busy, onCancel, onConfirm }) {
  const element = useRef(null);
  useEffect(() => {
    if (open && !element.current.open) element.current.showModal();
    if (!open && element.current.open) element.current.close();
  }, [open]);
  return <dialog ref={element} className="glass mix-dialog" onCancel={event => { if (busy) event.preventDefault(); else onCancel(); }}><h2>Вернуть стандартные настройки?</h2><p>Размеры, материалы, движение, интерфейс и черновик форм вернутся к исходным. При необходимости сначала экспортируй профиль.</p><div className="native-profile-actions"><button disabled={busy} onClick={onCancel} autoFocus>Отмена</button><button disabled={busy} onClick={onConfirm}>Да, сбросить</button></div></dialog>;
}
