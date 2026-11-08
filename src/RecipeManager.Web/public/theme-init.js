// Apply an explicit preference before Angular and CSS render the first frame.
// System preference is handled by the stylesheet until the theme service starts.
try {
  const preference = localStorage.getItem('recipe-manager.theme');
  if (preference === 'light' || preference === 'dark') {
    document.documentElement.dataset.theme = preference;
    document.documentElement.style.colorScheme = preference;
  }
} catch { /* Storage may be disabled. */ }
