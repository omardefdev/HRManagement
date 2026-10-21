// HCM-026: assessment criteria editor — dynamic rows and live weight total (CSP-safe, no inline handlers).
(function () {
    'use strict';

    function parseNumber(value) {
        var n = parseFloat(String(value || '').replace(',', '.'));
        return isNaN(n) ? 0 : n;
    }

    function nextKey(form) {
        var max = 0;
        form.querySelectorAll('[data-criteria-key]').forEach(function (input) {
            var m = /^item-(\d+)$/.exec(input.value || '');
            if (m) max = Math.max(max, parseInt(m[1], 10));
        });
        return 'item-' + (max + 1);
    }

    function updateTotal(form) {
        var total = 0;
        form.querySelectorAll('[data-criteria-weight]').forEach(function (input) {
            total += parseNumber(input.value);
        });
        total = Math.round(total * 100) / 100;

        var ok = Math.abs(total - 100) <= 0.01;
        var totalEl = form.querySelector('[data-criteria-total]');
        var hintEl = form.querySelector('[data-criteria-total-hint]');
        var submit = form.querySelector('[data-criteria-submit]');
        var rows = form.querySelectorAll('[data-criteria-row]').length;

        if (totalEl) {
            totalEl.textContent = String(total);
            totalEl.classList.toggle('text-success', ok);
            totalEl.classList.toggle('text-danger', !ok);
        }
        if (hintEl) {
            hintEl.textContent = ok ? 'المجموع صحيح' : 'يجب أن يساوي مجموع الأوزان 100';
            hintEl.classList.toggle('text-success', ok);
            hintEl.classList.toggle('text-danger', !ok);
        }
        if (submit) submit.disabled = !ok || rows === 0;
    }

    function addRow(form) {
        var template = form.querySelector('[data-criteria-row-template]');
        var body = form.querySelector('[data-criteria-rows]');
        if (!template || !body) return;

        var token = 'n' + Date.now().toString(36) + Math.floor(Math.random() * 1000);
        var html = template.innerHTML.split('__T__').join(token);
        var holder = document.createElement('tbody');
        holder.innerHTML = html.trim();
        var row = holder.firstElementChild;
        var key = row.querySelector('[data-criteria-key]');
        if (key) key.value = nextKey(form);
        body.appendChild(row);
        updateTotal(form);
    }

    function init(form) {
        form.addEventListener('input', function (e) {
            if (e.target && e.target.hasAttribute('data-criteria-weight')) updateTotal(form);
        });
        form.addEventListener('click', function (e) {
            var target = e.target instanceof Element ? e.target : null;
            if (!target) return;
            if (target.closest('[data-criteria-add]')) {
                addRow(form);
                return;
            }
            var remove = target.closest('[data-criteria-remove]');
            if (remove) {
                var row = remove.closest('[data-criteria-row]');
                if (row) row.remove();
                updateTotal(form);
            }
        });
        updateTotal(form);
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('form[data-criteria-editor]').forEach(init);
    });
})();
