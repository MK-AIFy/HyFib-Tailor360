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

/**
 * Resolves a scanned `S-` payload to a stock item.
 *
 * Step 2 of `A11Y-RJ-05` is "select an item by scanning its `S-` barcode, and hear the resolved
 * item", and a step that only ever succeeds proves nothing — so an unknown payload is a distinct
 * outcome the screen has to be able to say out loud, and a `G-` payload scanned into a stock field
 * is a third, because that is the mistake somebody actually makes with a garment in the other hand.
 */
export type StockScanResolution =
  | { readonly outcome: 'accepted'; readonly item: StockItem }
  | { readonly outcome: 'wrong-namespace' }
  | { readonly outcome: 'unknown' }

export function resolveStockScan(payload: string): StockScanResolution {
  const trimmed = payload.trim().toUpperCase()

  if (trimmed.startsWith('G-')) {
    return { outcome: 'wrong-namespace' }
  }

  const item = STOCK_ITEMS.find(
    (candidate) => candidate.barcode === trimmed || candidate.id === trimmed,
  )
  return item === undefined ? { outcome: 'unknown' } : { outcome: 'accepted', item }
}

/**
 * The units a purchase can arrive in, and how much of the stocked unit each one holds.
 *
 * `baseUnitSymbol` is what ties a purchase unit to an item, and it is not decoration. Without it
 * every unit is offered for every item, and choosing hook cards while rolls of lining is still
 * selected produces "2 x Rolls of 25 m is 50 card" — a sentence that is wrong in exactly the way
 * step 1 of `A11Y-RJ-05` exists to catch, and one a clerk would post to the ledger without blinking.
 * `purchaseUnitsFor` is what a screen offers; nothing offers this whole list.
 */
export interface PurchaseUnit {
  readonly value: string
  readonly label: string
  /** The stocked unit this purchase unit converts into. */
  readonly baseUnitSymbol: string
  /** How many of the stocked unit one of these holds. */
  readonly baseUnitsEach: number
}

export const PURCHASE_UNITS: readonly PurchaseUnit[] = [
  { value: 'm', label: 'Metres', baseUnitSymbol: 'm', baseUnitsEach: 1 },
  { value: 'roll', label: 'Rolls of 25 m', baseUnitSymbol: 'm', baseUnitsEach: 25 },
  { value: 'card', label: 'Cards', baseUnitSymbol: 'card', baseUnitsEach: 1 },
  { value: 'box', label: 'Boxes of 24 cards', baseUnitSymbol: 'card', baseUnitsEach: 24 },
  { value: 'kit', label: 'Kits', baseUnitSymbol: 'kit', baseUnitsEach: 1 },
  { value: 'reel', label: 'Reels', baseUnitSymbol: 'reel', baseUnitsEach: 1 },
  { value: 'reel-box', label: 'Boxes of 12 reels', baseUnitSymbol: 'reel', baseUnitsEach: 12 },
]

/** The purchase units that convert into this item's stocked unit. Never empty for a stocked item. */
export function purchaseUnitsFor(item: StockItem | undefined): readonly PurchaseUnit[] {
  if (item === undefined) {
    return []
  }
  return PURCHASE_UNITS.filter((unit) => unit.baseUnitSymbol === item.unitSymbol)
}

/**
 * One movement on the stock ledger, newest first.
 *
 * The ledger is immutable and append-only (plan E08), so a correction is another row and never an
 * edit — which is why `quantity` carries its own sign and there is no "reversed" flag to look for.
 * Step 8 of `A11Y-RJ-05` is "read the ledger browser row by row", and a row only reads if it says
 * what moved, how much, in which direction and against what.
 */
export interface LedgerRow {
  readonly id: string
  readonly at: string
  readonly itemId: string
  readonly itemName: string
  readonly unitSymbol: string
  /** Signed: positive is a receipt into stock, negative is an issue out of it. */
  readonly quantity: number
  readonly movement: string
  /** The job, purchase or stocktake the movement was against. */
  readonly against: string
  readonly actor: string
}

export const LEDGER: readonly LedgerRow[] = [
  {
    id: 'LG-000418',
    at: '2026-05-07T10:42:00+05:30',
    itemId: 'ST-0041',
    itemName: 'Cotton lining — natural',
    unitSymbol: 'm',
    quantity: -0.25,
    movement: 'Issued to a job',
    against: 'J-CBE01-2627-000512-01, Stitching',
    actor: 'Vijaya S.',
  },
  {
    id: 'LG-000417',
    at: '2026-05-07T09:58:00+05:30',
    itemId: 'ST-0330',
    itemName: 'Polyester thread — matching',
    unitSymbol: 'reel',
    quantity: -2,
    movement: 'Issued to a job',
    against: 'J-CBE01-2627-000934-02, Finishing',
    actor: 'Vijaya S.',
  },
  {
    id: 'LG-000416',
    at: '2026-05-06T16:10:00+05:30',
    itemId: 'ST-0041',
    itemName: 'Cotton lining — natural',
    unitSymbol: 'm',
    quantity: 25,
    movement: 'Received from a supplier',
    against: 'Kovai Textiles, 1 roll of 25 m',
    actor: 'Vijaya S.',
  },
  {
    id: 'LG-000415',
    at: '2026-05-06T11:30:00+05:30',
    itemId: 'ST-0212',
    itemName: 'AD stone and bead kit',
    unitSymbol: 'kit',
    quantity: -1,
    movement: 'Issued to a job',
    against: 'J-CBE01-2627-000689-01, Finishing',
    actor: 'Vijaya S.',
  },
]
