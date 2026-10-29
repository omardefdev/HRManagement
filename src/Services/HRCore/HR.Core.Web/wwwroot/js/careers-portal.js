// HCM-030 (PLAN-30 H, K4): /careers/my, the apply form and the track lookup — moves focus to the error summary (or the
// status notice) after a post, so screen readers announce it (CSP-safe, no inline handlers).
(function () {
    'use strict';

    function focusFirst() {
        var target = document.querySelector('[data-hr-error-summary]') || document.querySelector('[data-hr-focus-notice]');
        if (target) {
            target.focus();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', focusFirst);
    } else {
        focusFirst();
    }
})();
