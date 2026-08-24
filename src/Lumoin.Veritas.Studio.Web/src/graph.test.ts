// The directed-edge arrowhead's unit rows: the arrow marks the TARGET end (the from/to order of the
// graph query's rows is the direction, so an arrow at the wrong end asserts a false statement), stands
// off the node so it is not buried in the disc, sweeps symmetric wings, and refuses to draw on an edge
// too short to point anywhere.

import { describe, expect, it } from 'vitest';
import { edgeArrowhead } from './graph';

describe('edgeArrowhead', () => {
  it('stands the tip off the target along the edge', () => {
    const arrow = edgeArrowhead(0, 0, 100, 0, 10, 8);
    expect(arrow).not.toBeNull();
    expect(arrow!.tipX).toBeCloseTo(90);
    expect(arrow!.tipY).toBeCloseTo(0);
  });

  it('marks the target end, not the source: swapping the endpoints moves the arrow', () => {
    const forward = edgeArrowhead(0, 0, 100, 0, 10, 8);
    const backward = edgeArrowhead(100, 0, 0, 0, 10, 8);
    expect(forward!.tipX).toBeCloseTo(90);
    expect(backward!.tipX).toBeCloseTo(10);
  });

  it('sweeps symmetric wings behind the tip', () => {
    const arrow = edgeArrowhead(0, 0, 100, 0, 10, 8);
    expect(arrow!.leftX).toBeCloseTo(82);
    expect(arrow!.rightX).toBeCloseTo(82);
    expect(arrow!.leftY).toBeCloseTo(-arrow!.rightY);
    expect(Math.abs(arrow!.leftY)).toBeGreaterThan(0);
  });

  it('follows a diagonal edge: the tip stays on the segment and the wings stay off it', () => {
    const arrow = edgeArrowhead(0, 0, 30, 40, 5, 5);
    expect(arrow!.tipX).toBeCloseTo(27);
    expect(arrow!.tipY).toBeCloseTo(36);
    const crossLeft = 30 * arrow!.leftY - 40 * arrow!.leftX;
    const crossRight = 30 * arrow!.rightY - 40 * arrow!.rightX;
    expect(crossLeft).toBeCloseTo(-crossRight);
    expect(Math.abs(crossLeft)).toBeGreaterThan(0);
  });

  it('draws no arrow on an edge too short to point anywhere', () => {
    expect(edgeArrowhead(0, 0, 12, 0, 10, 8)).toBeNull();
    expect(edgeArrowhead(5, 5, 5, 5, 10, 8)).toBeNull();
  });
});
