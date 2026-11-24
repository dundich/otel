// Small, local-only UI adjustments for the embedded Aspire Dashboard.
// Loaded from nginx on the same origin as the dashboard, so CSP script-src 'self' allows it.
(() => {
    const isEditable = target =>
        target instanceof Element &&
        (target.tagName === 'INPUT' ||
         target.tagName === 'TEXTAREA' ||
         target.tagName === 'SELECT' ||
         target.isContentEditable);

    // Aspire opens its Help dialog on "?". The button is hidden with CSS;
    // this also prevents the shortcut from opening the dialog.
    document.addEventListener('keydown', event => {
        if (event.key !== '?' || event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }
        if (isEditable(event.target)) {
            return;
        }
        event.preventDefault();
        event.stopImmediatePropagation();
    }, true);
})();
