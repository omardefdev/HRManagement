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



    function fetchBatch(path, ids, groups) {

        return fetch(lookupBase() + path, {

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

                    if (els) applyLabel(item, els);

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



    function hydrateBatch(path, groups) {

        var ids = Array.from(groups.keys());

        if (ids.length === 0) return Promise.resolve();

        var chunks = chunkIds(ids, maxBatch);

        return Promise.all(chunks.map(function (chunk) {

            return fetchBatch(path, chunk, groups);

        }));

    }



    document.addEventListener('DOMContentLoaded', function () {

        var employeeGroups = groupElements('[data-hr-employee-id]', 'data-hr-employee-id');

        var orgUnitGroups = groupElements('[data-hr-org-unit-id]', 'data-hr-org-unit-id');

        Promise.all([

            hydrateBatch('/api/lookup/employees/by-ids', employeeGroups),

            hydrateBatch('/api/lookup/org-units/by-ids', orgUnitGroups)

        ]);

    });

})(window.hrSharedUi || {});

