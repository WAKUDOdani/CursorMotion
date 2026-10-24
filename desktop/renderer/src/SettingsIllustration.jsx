import { memo, useEffect } from 'react';
import { artworkMetadata, artworkPath, artworkPlacement, collectionArtworkFrame, collectionCursorForms, cursorForms, materialIds, settingsArtworkFrame, settingsCursorArtworkFrame } from './settings-artwork.mjs';

const ArtworkImage = memo(function ArtworkImage({ material, subject, label, center, width, height, frame, asset }) {
  const metadata = asset || artworkMetadata[material][subject];
  return <img className="settings-object" data-form={subject} data-material={material} data-renderer="native-sdf" data-artwork-meta={JSON.stringify({ width: metadata.width, height: metadata.height, bounds: metadata.bounds })} src={asset?.dataUri || artworkPath(material, subject)} alt={label} style={artworkPlacement(material, subject, { center, width, height }, frame, asset)} draggable="false" decoding="async" />;
});

export function SettingsIllustration({ page, material, mix, materials, framing = 'settings', frames = {} }) {
  const frame = framing === 'collection' ? collectionArtworkFrame : page === 'cursor' ? settingsCursorArtworkFrame : settingsArtworkFrame;
  const forms = framing === 'collection' ? collectionCursorForms : cursorForms;
  useEffect(() => {
    if (page !== 'cursor' || framing === 'collection') return;
    for (const identifier of materialIds) {
      for (const form of cursorForms) {
        const image = new Image();
        image.src = artworkPath(identifier, form.id);
      }
    }
  }, [page, framing]);

  return <span className={`settings-illustration ${framing === 'collection' ? 'collection-illustration' : ''}`}><span className="illustration-canvas" data-testid={`${framing}-illustration`}>
    {page === 'cursor' ? forms.map(form => <ArtworkImage key={form.id} material={mix[form.label]} subject={form.id} label={`${form.label} · ${materials.find(item => item.id === mix[form.label]).name}`} center={form.center} width={form.width} height={form.height} frame={frame} asset={frames[form.id]} />) : <ArtworkImage material={material} subject={page} label={`${page === 'motion' ? 'Движение' : 'Пространство'} · ${materials.find(item => item.id === material).name}`} center={300} width={page === 'motion' ? 560 : 470} height={235} frame={frame} asset={frames[page]} />}
  </span></span>;
}
