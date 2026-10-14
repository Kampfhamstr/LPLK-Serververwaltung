(() => {
  const root = document.documentElement;
  const allowed = new Set(['light','dark','system']);
  let preference = String(root.dataset.themePreference || 'system').toLowerCase();
  const authenticated = root.dataset.authenticated === '1';

  if (!allowed.has(preference)) preference = 'system';

  try {
    const local = String(localStorage.getItem('serververwaltung.uiTheme') || '').toLowerCase();
    if (!authenticated && allowed.has(local)) preference = local;
  } catch (_) {}

  const resolved = preference === 'system'
    ? (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark')
    : preference;

  root.dataset.themePreference = preference;
  root.dataset.themeResolved = resolved;
  root.style.colorScheme = resolved;
})();
