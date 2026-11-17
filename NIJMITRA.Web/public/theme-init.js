// Applies the saved theme before React renders (external file so it complies with the strict CSP).
(function () {
  try {
    var t = localStorage.getItem('nm-theme') || 'system';
    var dark = t === 'dark' || (t === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.setAttribute('data-bs-theme', dark ? 'dark' : 'light');
  } catch (e) { /* storage unavailable */ }
})();
