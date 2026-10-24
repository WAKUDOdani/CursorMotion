import { useEffect, useRef, useState } from 'react';
import { BookmarkSimple, Check, DownloadSimple, Plus, Trash, ArrowClockwise } from '@phosphor-icons/react';

export function PresetLibrary({ presets = [], warning, busy, hasDraft, onSave, onApply, onUpdate, onDelete, onImport, onExport }) {
  const [name, setName] = useState('');
  const [error, setError] = useState('');
  const [action, setAction] = useState(null);
  const dialog = useRef(null);
  useEffect(() => {
    if (action && !dialog.current.open) dialog.current.showModal();
    if (!action && dialog.current.open) dialog.current.close();
  }, [action]);
  async function confirm() {
    const result = await (action.kind === 'delete' ? onDelete(action.preset) : action.kind === 'update' ? onUpdate(action.preset) : onApply(action.preset));
    if (result) setAction(null);
  }
  return <section id="user-presets" className="preset-library" aria-label="Мои пресеты">
    <div className="preset-library-heading"><span className="preset-library-icon"><BookmarkSimple size={21} /></span><div><h3>Мои пресеты</h3><p>Твой курсор, движение и оформление — в одно нажатие.</p></div></div>
    <form className="preset-create" onSubmit={async event => { event.preventDefault(); if (presets.some(preset => preset.name.toLocaleLowerCase() === name.trim().toLocaleLowerCase())) { setError('Название уже занято. Выбери другое или обнови сохранённый пресет кнопкой ↻.'); return; } if (name.trim() && await onSave(name.trim())) setName(''); }}>
      <label>Название пресета<input aria-label="Название пресета" placeholder="Например, мой повседневный" value={name} maxLength={60} disabled={busy} required onChange={event => { setName(event.target.value); setError(''); }} /></label>
      <button className="preset-save" type="submit" disabled={busy || !name.trim()}><Plus size={18} />{hasDraft ? 'Применить формы и сохранить' : 'Сохранить текущие настройки'}</button>
    </form>
    {error && <p className="native-hint" role="alert">{error}</p>}
    <p className="native-hint preset-scope">Сохраняются все формы, размеры, эффекты, интерфейс и область действия. Автозапуск не меняется.{hasDraft && ' Сохранение также применит твои изменения форм.'}</p>
    {warning && <p role="alert" className="native-hint">{warning}</p>}
    <div className="preset-list">
      {presets.length ? presets.map(preset => <article className={`preset-card ${preset.isCurrent ? 'is-current' : ''}`} key={preset.id} data-preset-id={preset.id}>
        <div className="preset-card-copy"><strong>{preset.name}</strong><small>{preset.summary.scale}× · размытие {preset.summary.shutter}° · сглаживание {preset.summary.smoothing} мс</small></div>
        <div className="preset-card-actions"><button className="preset-apply" disabled={busy || (preset.isCurrent && !hasDraft)} aria-label={`Применить пресет ${preset.name}`} onClick={() => hasDraft ? setAction({ kind: 'apply', preset }) : onApply(preset)}>{preset.isCurrent && !hasDraft ? <><Check size={16} />Текущий</> : 'Применить'}</button>
          <button className="preset-icon-action" title="Заменить сохранённые настройки текущими" aria-label={`Обновить пресет ${preset.name}`} disabled={busy} onClick={() => setAction({ kind: 'update', preset })}><ArrowClockwise size={18} /></button>
          <button className="preset-icon-action" title="Экспортировать этот пресет" aria-label={`Экспортировать пресет ${preset.name}`} disabled={busy} onClick={() => onExport(preset)}><DownloadSimple size={18} /></button>
          <button className="preset-icon-action preset-delete" title="Удалить пресет" aria-label={`Удалить пресет ${preset.name}`} disabled={busy} onClick={() => setAction({ kind: 'delete', preset })}><Trash size={18} /></button></div>
      </article>) : <div className="preset-empty">Пока пусто. Настрой курсор под себя и сохрани первый пресет.</div>}
    </div>
    <button className="native-text-button" disabled={busy} onClick={onImport}>Импортировать пресет из файла</button>
    <dialog ref={dialog} className="glass mix-dialog" onCancel={event => { if (busy) event.preventDefault(); else setAction(null); }}>
      <h2>{action?.kind === 'delete' ? 'Удалить пресет?' : action?.kind === 'update' ? 'Обновить пресет?' : 'Применить пресет?'}</h2>
      <p>{action?.kind === 'delete' ? `«${action.preset.name}» исчезнет из списка. Текущий курсор останется как есть.` : action?.kind === 'update' ? `Настройки «${action.preset.name}» заменятся текущими.${hasDraft ? ' Изменения форм также применятся.' : ''}` : 'Неприменённые изменения форм заменятся настройками из пресета.'}</p>
      <div className="native-profile-actions"><button disabled={busy} onClick={() => setAction(null)} autoFocus>Отмена</button><button disabled={busy} onClick={confirm}>{action?.kind === 'delete' ? 'Удалить' : action?.kind === 'update' ? 'Обновить' : 'Применить'}</button></div>
    </dialog>
  </section>;
}
