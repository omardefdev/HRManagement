// PLAN-60 (G-SEP-11): a separation form offers the chosen employee's own active or pending contracts only.
// A <select data-hr-employee-contracts="<url>" data-hr-employee-source="<id of the employee select>"> is refilled from
// <url>?employeeId=<id> whenever the employee changes. The first option (no contract) is kept; the server checks the choice again.
(function () {
    'use strict';

    function refill(target, url, employeeId) {
        var keep = target.options.length > 0 && !target.options[0].value ? target.options[0] : null;
        var current = target.value;
        if (!employeeId) {
            target.innerHTML = '';
            if (keep) target.appendChild(keep);
            return;
        }
        fetch(url + '?employeeId=' + encodeURIComponent(employeeId), {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (items) {
                target.innerHTML = '';
                if (keep) target.appendChild(keep);
                (items || []).forEach(function (item) {
                    var option = document.createElement('option');
                    option.value = item.id;
                    option.textContent = item.label;
                    if (item.id === current || (items.length === 1 && !current)) option.selected = true;
                    target.appendChild(option);
                });
            })
            .catch(function () { /* the server checks the contract on submit */ });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('select[data-hr-employee-contracts]').forEach(function (target) {
            var source = document.getElementById(target.getAttribute('data-hr-employee-source') || 'EmployeeId');
            if (!source) return;
            var url = target.getAttribute('data-hr-employee-contracts');
            source.addEventListener('change', function () { refill(target, url, source.value); });
        });
    });
})();
