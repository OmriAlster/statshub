// The basketball season: August to July, as the server counts it
// (LeagueSeason.StartOf) - "2026-27".
export const LeagueSeason = {
  startOf(now: Date) {
    const year = now.getMonth() >= 7 ? now.getFullYear() : now.getFullYear() - 1
    return new Date(year, 7, 1)
  },
  label(now: Date) {
    const year = LeagueSeason.startOf(now).getFullYear()
    return `${year}-${String((year + 1) % 100).padStart(2, '0')}`
  },
}
