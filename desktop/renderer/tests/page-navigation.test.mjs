import test from 'node:test';
import assert from 'node:assert/strict';
import { pageDirection, pageOrder } from '../src/page-navigation.mjs';

test('all tab pairs use their actual spatial ordering', () => {
  for (const [currentIndex, current] of pageOrder.entries()) {
    for (const [nextIndex, next] of pageOrder.entries()) {
      assert.equal(pageDirection(current, next), nextIndex === currentIndex ? 'none' : nextIndex > currentIndex ? 'forward' : 'backward');
    }
  }
});

test('unknown pages cannot enter the transition state', () => {
  assert.throws(() => pageDirection('collection', 'missing'), RangeError);
  assert.throws(() => pageDirection('missing', 'motion'), RangeError);
});
