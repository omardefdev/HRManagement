(function () {
    'use strict';

    function fieldId(el) {
        return el.id || el.getAttribute('name') || '';
    }

    function ensureErrorId(span, input) {
        var id = span.id;
        if (!id) {
            var base = fieldId(input) || 'field';
            id = base + '-error';
            span.id = id;
        }
        return id;
    }

    function linkField(input) {
        if (!input || input.disabled) return;
        var name = input.getAttribute('name');
        if (!name) return;
        var span = null;
        try {
            span = document.querySelector('[data-valmsg-for="' + CSS.escape(name) + '"]');
        } catch (_) {
            span = document.querySelector('[data-valmsg-for="' + name.replace(/"/g, '') + '"]');
        }
        if (!span) {
            // fallback: asp-validation-for renders span next to field often with field-validation-error
            var sibling = input.parentElement && input.parentElement.querySelector('.field-validation-error, .field-validation-valid');
            span = sibling;
        }
        if (!span) return;

        var errId = ensureErrorId(span, input);
        var described = (input.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
        if (described.indexOf(errId) < 0) described.push(errId);
        input.setAttribute('aria-describedby', described.join(' '));

        var hasError = span.classList.contains('field-validation-error')
            && (span.textContent || '').trim().length > 0;
        if (hasError) {
            input.setAttribute('aria-invalid', 'true');
        } else {
            input.removeAttribute('aria-invalid');
        }
    }

    function refresh() {
        document.querySelectorAll('form input, form select, form textarea').forEach(linkField);
    }

    document.addEventListener('DOMContentLoaded', function () {
        refresh();
        document.addEventListener('focusout', function (e) {
            if (e.target && (e.target.matches('input,select,textarea'))) linkField(e.target);
        }, true);
        if (window.jQuery) {
            window.jQuery(document).on('invalid-form.validate', 'form', function () {
                setTimeout(refresh, 0);
            });
        }
    });
})();
