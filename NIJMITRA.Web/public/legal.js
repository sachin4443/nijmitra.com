// Fills in details the administrator manages in the Admin Panel (support email, deletion grace period, copyright name).
(function () {
  fetch('/api/system/info', { headers: { 'X-Requested-With': 'NIJMITRA' } }).then(function (r) { return r.ok ? r.json() : null }).then(function (info) {
    if (!info) return
    var web = info.website || {}
    var email = web.contactEmail || info.supportEmail
    document.querySelectorAll('[data-email]').forEach(function (el) {
      if (!email) return
      el.textContent = email
      if (el.tagName === 'A') el.href = 'mailto:' + email + (el.getAttribute('data-subject') ? '?subject=' + encodeURIComponent(el.getAttribute('data-subject')) : '')
    })
    document.querySelectorAll('[data-if-email]').forEach(function (el) { el.hidden = !email })
    document.querySelectorAll('[data-if-no-email]').forEach(function (el) { el.hidden = !!email })
    if (info.accountDeletionGraceDays) document.querySelectorAll('[data-grace]').forEach(function (el) { el.textContent = info.accountDeletionGraceDays })
    if (web.copyrightName) document.querySelectorAll('[data-copyright]').forEach(function (el) { el.textContent = web.copyrightName })
  }).catch(function () {})
  document.querySelectorAll('[data-year]').forEach(function (el) { el.textContent = new Date().getFullYear() })
})()
