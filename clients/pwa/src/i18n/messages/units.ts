/**
 * Unit words and symbols.
 *
 * A symbol — `in`, `cm`, `₹` — is language-neutral and is printed as it is. The **word** is what a
 * screen reader says and what a Tamil-reading member of staff reads, and it is glossary-owned
 * (docs/nfr/accessibility-localisation.md section 11.2 lists measurement labels as the
 * highest-consequence translation in the product). Every `FieldUnit` takes both, and both come from
 * here rather than from a literal in a component.
 */
export const unitsEn = {
  'units.inch.symbol': 'in',
  'units.inch.label': 'inches',
  'units.centimetre.symbol': 'cm',
  'units.centimetre.label': 'centimetres',
  'units.millimetre.symbol': 'mm',
  'units.millimetre.label': 'millimetres',
  'units.rupee.symbol': '₹',
  'units.rupee.label': 'rupees',
  'units.percent.symbol': '%',
  'units.percent.label': 'per cent',
  'units.metre.symbol': 'm',
  'units.metre.label': 'metres',
  'units.piece.symbol': 'pc',
  'units.piece.label': 'pieces',
} as const

export const unitsTa: Record<keyof typeof unitsEn, string> = {
  'units.inch.symbol': 'in',
  'units.inch.label': 'அங்குலம்',
  'units.centimetre.symbol': 'cm',
  'units.centimetre.label': 'சென்டிமீட்டர்',
  'units.millimetre.symbol': 'mm',
  'units.millimetre.label': 'மில்லிமீட்டர்',
  'units.rupee.symbol': '₹',
  'units.rupee.label': 'ரூபாய்',
  'units.percent.symbol': '%',
  'units.percent.label': 'சதவீதம்',
  'units.metre.symbol': 'm',
  'units.metre.label': 'மீட்டர்',
  'units.piece.symbol': 'pc',
  'units.piece.label': 'துண்டுகள்',
}
