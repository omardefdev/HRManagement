(function (ui) {
    'use strict';

    function lookupBase() {
        return document.body.getAttribute('data-lookup-base') || '/ui/hr-core';
    }

    function fetchName(path, id, el) {
        if (!id || id === '00000000-0000-0000-0000-000000000000') return;
        fetch(lookupBase() + path + '/' + id, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (item) {
                // Sublabel is non-PII (org unit name / org code) — never NationalId.
                if (item && item.label) el.textContent = item.sublabel ? item.label + ' — ' + item.sublabel : item.label;
            })
            .catch(function () { /* keep guid fallback */ });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-hr-employee-id]').forEach(function (el) {
            fetchName('/api/lookup/employees', el.getAttribute('data-hr-employee-id'), el);
        });
        document.querySelectorAll('[data-hr-org-unit-id]').forEach(function (el) {
            fetchName('/api/lookup/org-units', el.getAttribute('data-hr-org-unit-id'), el);
        });
    });
})(window.hrSharedUi || {});
