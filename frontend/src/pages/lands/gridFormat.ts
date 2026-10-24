import type { GridDto } from '@/types/lands';

/**
 * The display text of a grid.
 *
 * The land endpoints expose the grid as an id, while the reference list carries the
 * grid name. The name is therefore resolved from the reference list; when the list has
 * no entry for the id (or could not be loaded) the id is shown as the only available
 * identifier, marked with a leading `#` so it is never mistaken for a name.
 */
export function gridDisplayText(
  grids: GridDto[] | undefined,
  gridId: number | null | undefined,
): string | null {
  if (gridId === null || gridId === undefined) {
    return null;
  }
  const name = grids?.find((grid) => grid.id === gridId)?.name;
  return name ?? `#${gridId}`;
}
