// Site-wide behaviour shared by every page.
(function () {
    'use strict';

    // Success / error banners (.alert) fade out after a few seconds. Bulk-result notes (.pm-note) stay until closed.
    window.setTimeout(function () {
        $('.alert').fadeTo(500, 0).slideUp(500, function () { $(this).remove(); });
    }, 4000);

    // Bootstrap tooltips for elements marked data-toggle="tooltip"
    document.querySelectorAll('[data-toggle="tooltip"]').forEach(function (el) {
        new bootstrap.Tooltip(el);
    });

    // Highlight the menu entry for the current page
    document.querySelectorAll('.navbar .dropdown-item').forEach(function (link) {
        if (link.href === window.location.href) link.classList.add('active');
    });
})();
