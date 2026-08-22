// Mounted once at the app root. Every icon elsewhere is rendered as
// <svg className="icon"><use href="#i-name" /></svg> against this sprite,
// so no icon set/library needs to be installed.
export default function IconSprite() {
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true">
      <symbol id="i-ball" viewBox="0 0 24 24"><circle cx="12" cy="12" r="9" /><path d="M12 3v18M3 12h18M5.6 5.6c3 3 9.8 3 12.8 0M5.6 18.4c3-3 9.8-3 12.8 0" /></symbol>
      <symbol id="i-check" viewBox="0 0 24 24"><polyline points="4 13 9 18 20 6" /></symbol>
      <symbol id="i-x" viewBox="0 0 24 24"><line x1="5" y1="5" x2="19" y2="19" /><line x1="19" y1="5" x2="5" y2="19" /></symbol>
      <symbol id="i-reb" viewBox="0 0 24 24"><rect x="4" y="4" width="16" height="16" rx="2" /><path d="M12 8v8M8 12h8" /></symbol>
      <symbol id="i-ast" viewBox="0 0 24 24"><path d="M4 17c3-6 7-9 8-9s5 3 8 9" /><path d="M9 9l-1-4M15 9l1-4" /></symbol>
      <symbol id="i-stl" viewBox="0 0 24 24"><path d="M4 12c4-6 12-6 16 0" /><path d="M14 8l4 1-1 4" /></symbol>
      <symbol id="i-blk" viewBox="0 0 24 24"><path d="M12 3l7 3v6c0 5-3 7.5-7 9-4-1.5-7-4-7-9V6z" /></symbol>
      <symbol id="i-to" viewBox="0 0 24 24"><path d="M4 8h13M13 4l4 4-4 4" /><path d="M20 16H7M11 20l-4-4 4-4" /></symbol>
      <symbol id="i-foul" viewBox="0 0 24 24"><path d="M5 3v18" /><path d="M5 4h11l-3 4 3 4H5" /></symbol>
      <symbol id="i-ft" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.5" /><circle cx="12" cy="12" r="3" /></symbol>
      <symbol id="i-home" viewBox="0 0 24 24"><path d="M4 11l8-7 8 7" /><path d="M6 10v9h12v-9" /></symbol>
      <symbol id="i-live" viewBox="0 0 24 24"><path d="M3 12h4l2 7 4-14 2 7h6" /></symbol>
      <symbol id="i-chart" viewBox="0 0 24 24"><path d="M4 20V10M12 20V4M20 20v-7" /></symbol>
      <symbol id="i-users" viewBox="0 0 24 24"><circle cx="9" cy="8" r="3" /><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6" /><circle cx="17" cy="9" r="2.4" /><path d="M15.5 14c2.6.3 4.5 2.3 4.5 6" /></symbol>
      <symbol id="i-user" viewBox="0 0 24 24"><circle cx="12" cy="8" r="4" /><path d="M4 20c0-4.4 3.6-8 8-8s8 3.6 8 8" /></symbol>
      <symbol id="i-chevron" viewBox="0 0 24 24"><polyline points="9 5 16 12 9 19" /></symbol>
      <symbol id="i-target" viewBox="0 0 24 24"><circle cx="12" cy="12" r="8.5" /><circle cx="12" cy="12" r="4.5" /><circle cx="12" cy="12" r=".8" fill="currentColor" stroke="none" /></symbol>
      <symbol id="i-lock" viewBox="0 0 24 24"><rect x="5" y="10" width="14" height="10" rx="2" /><path d="M8 10V7a4 4 0 018 0v3" /></symbol>
      <symbol id="i-logout" viewBox="0 0 24 24"><path d="M9 4H5a1 1 0 00-1 1v14a1 1 0 001 1h4" /><path d="M15 16l4-4-4-4" /><path d="M19 12H9" /></symbol>
      <symbol id="i-plus" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14" /></symbol>
      <symbol id="i-share" viewBox="0 0 24 24"><circle cx="18" cy="5" r="2.5" /><circle cx="6" cy="12" r="2.5" /><circle cx="18" cy="19" r="2.5" /><path d="M8.3 10.7l7.4-4.4M8.3 13.3l7.4 4.4" /></symbol>
      <symbol id="i-copy" viewBox="0 0 24 24"><rect x="9" y="9" width="11" height="11" rx="2" /><path d="M5 15V5a2 2 0 012-2h10" /></symbol>
      <symbol id="i-undo" viewBox="0 0 24 24"><path d="M4 10h9a5 5 0 010 10h-2" /><path d="M4 10l4-4M4 10l4 4" /></symbol>
      <symbol id="i-trophy" viewBox="0 0 24 24"><path d="M7 4h10v3a5 5 0 01-10 0V4z" /><path d="M7 5H4v2a3 3 0 003 3M17 5h3v2a3 3 0 01-3 3" /><path d="M12 12v4M9 20h6M9 20v-2a3 3 0 013-3 3 3 0 013 3v2" /></symbol>
    </svg>
  )
}
