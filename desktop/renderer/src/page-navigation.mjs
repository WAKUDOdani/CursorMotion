export const pageOrder = ['collection', 'motion', 'cursor', 'space'];

export function pageDirection(current, next) {
  const currentIndex = pageOrder.indexOf(current);
  const nextIndex = pageOrder.indexOf(next);
  if (currentIndex < 0 || nextIndex < 0) throw new RangeError('Unknown studio page');
  return nextIndex === currentIndex ? 'none' : nextIndex > currentIndex ? 'forward' : 'backward';
}
