/**
 * Stock items, a receipt and a stocktake. Synthetic — see the note at the top of `branch.ts`.
 *
 * Quantities are in the item's base unit and are never rounded for storage: the stock ledger of
 * plan E08 keeps four decimal places, which is why 0.25 m of lining is a real number here rather
 * than a count. `S-` is the stock namespace of plan D9, alongside `G-` for a garment job.
 */
export interface StockItem {
  readonly id: string
  readonly barcode: string
  readonly name: string
  readonly unitSymbol: string
  /** The spoken unit, because "m" read aloud is not "metres". */
  readonly unitLabel: string
  readonly onHand: number
  readonly reorderLevel: number
  readonly location: string
}

export const STOCK_ITEMS: readonly StockItem[] = [
  {
    id: 'ST-0041',
    barcode: 'S-9XR2VT6KHB3D',
    name: 'Cotton lining — natural',
    unitSymbol: 'm',
    unitLabel: 'metres',
    onHand: 42.5,
    reorderLevel: 20,
    location: 'Rack A2',
  },
  {
    id: 'ST-0107',
    barcode: 'S-4KDW8ZQ3XN7B',
    name: 'Hook card — 12 pairs',
    unitSymbol: 'card',
    unitLabel: 'cards',
    onHand: 8,
    reorderLevel: 15,
    location: 'Drawer B1',
  },
  {
    id: 'ST-0212',
    barcode: 'S-7BQM2XK9WT4Z',
    name: 'AD stone and bead kit',
    unitSymbol: 'kit',
    unitLabel: 'kits',
    onHand: 2,
    reorderLevel: 6,
    location: 'Cabinet C3',
  },
  {
    id: 'ST-0330',
    barcode: 'S-3XNW7KQB2MT5',
    name: 'Polyester thread — matching',
    unitSymbol: 'reel',
    unitLabel: 'reels',
    onHand: 64,
    reorderLevel: 24,
    location: 'Rack A1',
  },
]

export const SUPPLIERS = [
  { id: 'SUP-004', name: 'Kovai Textiles' },
  { id: 'SUP-011', name: 'Ganga Trims and Accessories' },
  { id: 'SUP-019', name: 'Sri Lakshmi Threads' },
] as const

/** One stocktake line, at the point where the recount has been entered and the variance is known. */
export interface StocktakeLine {
  readonly itemId: string
  readonly name: string
  readonly unitSymbol: string
  readonly expected: number
  readonly counted: number
  readonly recounted: number
}

export const STOCKTAKE = {
  id: 'STK-CBE01-2627-000018',
  location: 'Rack A2',
  /** A frozen location cannot be issued from while the count is open. Step 4 of `A11Y-RJ-05`. */
  frozen: true,
  lines: [
    {
      itemId: 'ST-0041',
      name: 'Cotton lining — natural',
      unitSymbol: 'm',
      expected: 42.5,
      counted: 41,
      recounted: 41,
    },
    {
      itemId: 'ST-0330',
      name: 'Polyester thread — matching',
      unitSymbol: 'reel',
      expected: 64,
      counted: 66,
      recounted: 66,
    },
  ] as readonly StocktakeLine[],
} as const

/** The variance with its sign, which step 6 of `A11Y-RJ-05` requires to be announced as a pair. */
export function variance(line: StocktakeLine): number {
  return line.recounted - line.expected
}

export const LOW_STOCK = STOCK_ITEMS.filter((item) => item.onHand < item.reorderLevel)
