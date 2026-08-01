window.hrSharedUi = window.hrSharedUi || {};

(function (ui) {
    'use strict';

    function lookupBase() {
        return document.body.getAttribute('data-lookup-base') || '/ui/hr-core';
    }

    function formatOption(item) {
        if (!item.sublabel) return item.label;
        return item.label + ' — ' + item.sublabel;
    }

    function escapeHtml(value) {
        return ui.escapeHtml ? ui.escapeHtml(value) : String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function remoteLoad(path, query, callback) {
        var url = lookupBase() + path + '?q=' + encodeURIComponent(query || '') + '&page=1&pageSize=30';
        fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error('lookup failed');
                return r.json();
            })
            .then(function (data) {
                callback(data.items.map(function (x) {
                    return { value: x.id, text: formatOption(x), label: x.label, sublabel: x.sublabel };
                }));
            })
            .catch(function () {
                ui.handleAjaxError({ status: 0 });
                callback();
            });
    }

    function loadSelected(path, id, select) {
        if (!id) return;
        fetch(lookupBase() + path + '/' + id, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (item) {
                if (!item || !select.tomselect) return;
                select.tomselect.addOption({ value: item.id, text: formatOption(item) });
                select.tomselect.setValue(item.id, true);
            })
            .catch(function () { /* ignore */ });
    }

    ui.initEmployeePicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث عن موظف بالاسم...';
        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            load: function (query, callback) {
                remoteLoad('/api/lookup/employees', query, callback);
            },
            render: {
                option: function (data, escape) {
                    return '<div><span>' + escape(data.label || data.text) + '</span>' +
                        (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                        '</div>';
                }
            }
        });
        loadSelected('/api/lookup/employees', selectEl.value, selectEl);
        return ts;
    };

    ui.initOrgUnitPicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث عن وحدة تنظيمية...';
        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            load: function (query, callback) {
                remoteLoad('/api/lookup/org-units', query, callback);
            },
            render: {
                option: function (data, escape) {
                    return '<div><span>' + escape(data.label || data.text) + '</span>' +
                        (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                        '</div>';
                }
            }
        });
        loadSelected('/api/lookup/org-units', selectEl.value, selectEl);
        return ts;
    };

    ui.initOrgUnitTreeModal = function () {
        var modal = document.getElementById('orgUnitTreeModal');
        if (!modal) return;

        var targetInput = null;
        var treeContainer = document.getElementById('orgUnitTreePickerBody');

        modal.addEventListener('show.bs.modal', function (event) {
            var button = event.relatedTarget;
            if (!button) return;
            targetInput = document.getElementById(button.getAttribute('data-target-input'));
            var labelEl = document.getElementById(button.getAttribute('data-target-label'));
            if (labelEl) labelEl.textContent = button.getAttribute('data-picker-label') || 'الوحدة التنظيمية';

            if (treeContainer && !treeContainer.dataset.loaded) {
                fetch(lookupBase() + '/api/lookup/org-units/tree', { credentials: 'same-origin' })
                    .then(function (r) { return r.json(); })
                    .then(function (nodes) {
                        mountTree(treeContainer, nodes);
                        treeContainer.dataset.loaded = '1';
                        bindTreeSelection(treeContainer, targetInput, modal);
                    })
                    .catch(function () { ui.handleAjaxError({ status: 0 }); });
            } else if (treeContainer) {
                bindTreeSelection(treeContainer, targetInput, modal);
            }
        });
    };

    /** UX-001: build tree with DOM APIs — no unescaped innerHTML. */
    function mountTree(container, nodes) {
        container.textContent = '';
        if (!nodes || !nodes.length) {
            var empty = document.createElement('p');
            empty.className = 'text-muted text-center';
            empty.textContent = 'لا توجد وحدات تنظيمية';
            container.appendChild(empty);
            return;
        }
        container.appendChild(buildTreeUl(nodes));
    }

    function buildTreeUl(nodes) {
        var ul = document.createElement('ul');
        ul.className = 'list-unstyled hr-org-tree';
        nodes.forEach(function (n) {
            ul.appendChild(buildTreeLi(n));
        });
        return ul;
    }

    function buildTreeLi(node) {
        var li = document.createElement('li');
        li.className = 'hr-org-tree-item';

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn btn-link tree-select-btn';
        btn.setAttribute('data-id', String(node.id == null ? '' : node.id));
        var label = String(node.code == null ? '' : node.code) + ' — ' + String(node.nameAr == null ? '' : node.nameAr);
        btn.setAttribute('data-label', label);

        var icon = document.createElement('i');
        icon.className = 'fas fa-folder-open text-warning';
        btn.appendChild(icon);
        btn.appendChild(document.createTextNode(' '));

        var strong = document.createElement('strong');
        strong.textContent = node.code == null ? '' : String(node.code);
        btn.appendChild(strong);
        btn.appendChild(document.createTextNode(' — ' + (node.nameAr == null ? '' : String(node.nameAr))));
        li.appendChild(btn);

        if (node.children && node.children.length) {
            var childUl = document.createElement('ul');
            childUl.className = 'list-unstyled ps-3';
            childUl.style.marginInlineStart = '0.75rem';
            node.children.forEach(function (c) {
                childUl.appendChild(buildTreeLi(c));
            });
            li.appendChild(childUl);
        }
        return li;
    }

    // Exported for XSS corpus / unit checks
    ui._mountOrgUnitTreeForTest = mountTree;
    ui._escapeHtml = escapeHtml;

    function bindTreeSelection(container, targetInput, modal) {
        container.querySelectorAll('.tree-select-btn').forEach(function (btn) {
            btn.onclick = function () {
                if (!targetInput) return;
                var id = btn.getAttribute('data-id');
                var label = btn.getAttribute('data-label');
                targetInput.value = id;
                var display = document.getElementById(targetInput.getAttribute('data-display-for'));
                if (display) display.textContent = label;
                var select = document.querySelector('select[name="' + targetInput.name + '"]');
                if (select && select.tomselect) {
                    select.tomselect.addOption({ value: id, text: label });
                    select.tomselect.setValue(id, true);
                }
                bootstrap.Modal.getInstance(modal).hide();
            };
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-hr-picker="employee"]').forEach(ui.initEmployeePicker);
        document.querySelectorAll('[data-hr-picker="org-unit"]').forEach(ui.initOrgUnitPicker);
        ui.initOrgUnitTreeModal();
    });

})(window.hrSharedUi);
