// Fixed parts of a header cell, in px, each matching a class in GridHeaderCell.tsx (a test checks the
// classes are still there). Default and fitted widths add these to the measured label so it never truncates.
export const HEADER_PADDING_X = 20 // px-2.5 on both sides
export const HEADER_GAP = 4 // gap-1 between the cell's flex children
export const HEADER_GRIP = 14 // w-3.5
export const HEADER_BADGE = 32 // w-8
export const HEADER_SORT = 4 + 12 // gap-1 inside the label button + size-3 mark
export const HEADER_MENU = 18 // size-[18px]
export const HEADER_FILTER = 18 // size-[18px] filter button
export const HEADER_PIN = 12 // size-3
// Canvas text widths are fractional and layout rounds; a few px stop a label ellipsing by a hair.
export const HEADER_SLACK = 4

const CHILD_GAPS = 4 // grip | badge | label | filter | menu

export const HEADER_EXTRA =
  HEADER_PADDING_X + HEADER_GRIP + HEADER_BADGE + HEADER_SORT + HEADER_FILTER + HEADER_MENU + CHILD_GAPS * HEADER_GAP + HEADER_SLACK

/** Added when the column is pinned: the pin glyph and the gap before it. */
export const PINNED_HEADER_EXTRA = HEADER_PIN + HEADER_GAP

/** The dot that marks a formatted column: size-1.5 plus the gap before it. */
export const FORMAT_MARK_WIDTH = 6 + HEADER_GAP

export function headerExtra({ pinned, formatted }: { pinned: boolean; formatted: boolean }): number {
  return HEADER_EXTRA + (pinned ? PINNED_HEADER_EXTRA : 0) + (formatted ? FORMAT_MARK_WIDTH : 0)
}
