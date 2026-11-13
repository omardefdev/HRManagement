(function (ui) {

    'use strict';



    var guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    var maxBatch = 50;



    function lookupBase() {

        return document.body.getAttribute('data-lookup-base') || '/ui/hr-core';

    }



    function isGuid(text) {

        return guidPattern.test((text || '').trim());

    }



    function applyLabel(item, elements) {

        if (!item || !item.label) return;

        var text = item.sublabel ? item.label + ' — ' + item.sublabel : item.label;

        elements.forEach(function (el) { el.textContent = text; });

    }



    function groupElements(selector, attr) {

        var groups = new Map();

        document.querySelectorAll(selector).forEach(function (el) {

            var id = el.getAttribute(attr);

            if (!id) return;

            var key = id.toLowerCase();

            if (!groups.has(key)) groups.set(key, []);

            groups.get(key).push(el);

        });

        return groups;

    }



    function chunkIds(ids, size) {

        var chunks = [];

        for (var i = 0; i < ids.length; i += size) {

            chunks.push(ids.slice(i, i + size));

        }

        return chunks;

    }



    // PLAN-67 (D-67-3): user names come from the gateway (its own origin), not from HR.Core's lookup base.
    function userNamesBase() {
        return document.body.getAttribute('data-user-names-base') || '';
    }

    function applyUserName(item, elements) {
        if (!item || !item.label) return;
        elements.forEach(function (el) {
            el.textContent = item.label;
            el.removeAttribute('title');
        });
    }

    function fetchBatch(path, ids, groups, base, apply) {

        return fetch((base === undefined ? lookupBase() : base) + path, {

            method: 'POST',

            credentials: 'same-origin',

            headers: {

                Accept: 'application/json',

                'Content-Type': 'application/json'

            },

            body: JSON.stringify({ ids: ids })

        })

            .then(function (r) { return r.ok ? r.json() : null; })

            .then(function (data) {

                if (!data || !data.items) return;

                data.items.forEach(function (item) {

                    var key = String(item.id).toLowerCase();

                    var els = groups.get(key) || groups.get(String(item.id));

                    if (els) (apply || applyLabel)(item, els);

                });

            })

            .catch(function () {
                groups.forEach(function (elements) {
                    elements.forEach(function (el) {
                        if (!el.textContent || isGuid(el.textContent) || el.textContent.trim() === 'جاري التحميل...')
                            el.textContent = 'غير متاح';
                    });
                });
            });

    }



    function hydrateBatch(path, groups, base, apply) {

        var ids = Array.from(groups.keys());

        if (ids.length === 0) return Promise.resolve();

        var chunks = chunkIds(ids, maxBatch);

        return Promise.all(chunks.map(function (chunk) {

            return fetchBatch(path, chunk, groups, base, apply);

        }));

    }



    document.addEventListener('DOMContentLoaded', function () {

        var employeeGroups = groupElements('[data-hr-employee-id]', 'data-hr-employee-id');

        var orgUnitGroups = groupElements('[data-hr-org-unit-id]', 'data-hr-org-unit-id');
        var userGroups = groupElements('[data-hr-user-id]', 'data-hr-user-id');

        Promise.all([

            hydrateBatch('/api/lookup/employees/by-ids', employeeGroups),

            hydrateBatch('/api/lookup/org-units/by-ids', orgUnitGroups),
            // An id the registry does not know keeps «مستخدم غير معروف» and its tooltip.
            hydrateBatch('/api/user-names/by-ids', userGroups, userNamesBase(), applyUserName)

        ]);

    });

})(window.hrSharedUi || {});

