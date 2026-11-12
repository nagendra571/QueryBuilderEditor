export const SECTION_H = 1
export const MAX_SECTION_TITLE = 80
export const MAX_SECTION_DESCRIPTION = 160

/** The box the server insists on for a section: full width, one row, at column 0. */
export function sectionBox(y: number) {
  return { x: 0, y, width: 12, height: SECTION_H }
}

export interface SectionValidation {
  valid: boolean
  titleError?: string
  descriptionError?: string
}

/** Same limits the server enforces, so the dialog can refuse before a save round-trip. */
export function validateSection(title: string, description: string): SectionValidation {
  const t = title.trim()
  const d = description.trim()
  const titleError = !t ? 'A title is required.' : t.length > MAX_SECTION_TITLE ? `At most ${MAX_SECTION_TITLE} characters.` : undefined
  const descriptionError = /[\r\n]/.test(description)
    ? 'One line only.'
    : d.length > MAX_SECTION_DESCRIPTION
      ? `At most ${MAX_SECTION_DESCRIPTION} characters.`
      : undefined
  return { valid: !titleError && !descriptionError, titleError, descriptionError }
}
