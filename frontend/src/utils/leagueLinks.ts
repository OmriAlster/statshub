// An address rather than a name: "https://…", or anything with a /player/ path.
export const looksLikeLink = (text: string) => /^https?:\/\//i.test(text.trim()) || text.includes('/player/')
