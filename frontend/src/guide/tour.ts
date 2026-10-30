// The tour's steps, in order: find the player on IBBA -> type their name ->
// add them -> it syncs by itself.
export type TourStep = 'find' | 'search' | 'confirm' | 'synced'
export const TOUR: TourStep[] = ['find', 'search', 'confirm', 'synced']
