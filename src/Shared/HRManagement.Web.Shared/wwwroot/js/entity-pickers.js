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

    function getSelectedOptionText(selectEl) {
        if (!selectEl || selectEl.selectedIndex < 0) return '';
        var opt = selectEl.options[selectEl.selectedIndex];
        if (!opt) return '';
        return (opt.textContent || opt.text || opt.value || '').trim();
    }

    function ensureReferenceOption(select, code, text) {
        if (!select || !select.tomselect || !code) return;
        var display = text && text !== code ? text : code;
        if (!select.tomselect.options[code]) {
            select.tomselect.addOption({ value: code, text: display });
        }
        select.tomselect.setValue(code, true);
    }

    function referenceRenderers(placeholder) {
        return {
            option: function (data, escape) {
                return '<div><span>' + escape(data.label || data.text) + '</span>' +
                    (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                    '</div>';
            },
            item: function (data, escape) {
                return '<div>' + escape(data.label || data.text || data.value) + '</div>';
            },
            no_results: function () {
                return '<div class="no-results px-2 py-1">' + escapeHtml(placeholder) + '</div>';
            }
        };
    }

    function normalizeEmptyOption(selectEl) {
        var emptyOpt = selectEl.querySelector('option[value=""]');
        if (emptyOpt) emptyOpt.textContent = '';
    }

    ui.initEmployeePicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        normalizeEmptyOption(selectEl);
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث عن موظف بالاسم...';
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            plugins: ['dropdown_input'],
            dir: isRtl ? 'rtl' : 'ltr',
            load: function (query, callback) {
                remoteLoad('/api/lookup/employees', query, callback);
            },
            shouldLoad: function (query) {
                return query.length >= 1;
            },
            render: {
                option: function (data, escape) {
                    return '<div><span>' + escape(data.label || data.text) + '</span>' +
                        (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                        '</div>';
                },
                no_results: function () {
                    return '<div class="no-results px-2 py-1">اكتب للبحث عن موظف...</div>';
                }
            },
            onDropdownOpen: function () {
                var input = this.dropdown.querySelector('.dropdown-input');
                if (input) {
                    input.setAttribute('placeholder', placeholder);
                    input.focus();
                }
            }
        });
        loadSelected('/api/lookup/employees', selectEl.value, selectEl);
        return ts;
    };

    function remoteLoadReference(kind, query, callback) {
        var url = lookupBase() + '/api/lookup/reference-data?kind=' + encodeURIComponent(kind) +
            '&q=' + encodeURIComponent(query || '') + '&page=1&pageSize=30';
        fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) {
                if (!r.ok) throw new Error('lookup failed');
                return r.json();
            })
            .then(function (data) {
                callback((data.items || []).map(function (x) {
                    return {
                        value: x.code,
                        text: formatOption({ label: x.label, sublabel: x.sublabel }),
                        label: x.label,
                        sublabel: x.sublabel
                    };
                }));
            })
            .catch(function () {
                ui.handleAjaxError({ status: 0 });
                callback();
            });
    }

    function loadReferenceSelected(kind, code, select) {
        if (!code) return;
        var fallbackText = getSelectedOptionText(select) || code;
        ensureReferenceOption(select, code, fallbackText);
        fetch(lookupBase() + '/api/lookup/reference-data/' + encodeURIComponent(kind) + '/' + encodeURIComponent(code), {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (item) {
                if (!item || !select.tomselect) return;
                ensureReferenceOption(
                    select,
                    item.code,
                    formatOption({ label: item.label, sublabel: item.sublabel }));
            })
            .catch(function () {
                ensureReferenceOption(select, code, fallbackText);
            });
    }

    function setGradeCodeValue(gradeInput, gradeCode) {
        if (!gradeInput || !gradeCode) return;
        if (gradeInput.tomselect) {
            ensureReferenceOption(gradeInput, gradeCode, gradeCode);
            loadReferenceSelected('Grade', gradeCode, gradeInput);
        } else {
            gradeInput.value = gradeCode;
        }
    }

    ui.initReferenceDataPicker = function (selectEl, kind) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        normalizeEmptyOption(selectEl);
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث في المرجع الوطني...';
        var excludeCode = (selectEl.getAttribute('data-exclude-code') || '').trim().toLowerCase();
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        var initialCode = (selectEl.value || '').trim();
        var initialText = getSelectedOptionText(selectEl) || initialCode;
        var config = {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text', 'label', 'sublabel'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            plugins: ['dropdown_input'],
            dir: isRtl ? 'rtl' : 'ltr',
            preload: true,
            load: function (query, callback) {
                remoteLoadReference(kind, query, function (items) {
                    if (!excludeCode) {
                        callback(items);
                        return;
                    }
                    callback((items || []).filter(function (item) {
                        return String(item.value || '').toLowerCase() !== excludeCode;
                    }));
                });
            },
            shouldLoad: function (query) {
                return query.length === 0 || query.length >= 2;
            },
            render: referenceRenderers('اكتب حرفين على الأقل للبحث...'),
            onDropdownOpen: function () {
                var input = this.dropdown.querySelector('.dropdown-input');
                if (input) {
                    input.setAttribute('placeholder', placeholder);
                    input.focus();
                }
            }
        };
        if (initialCode) {
            config.options = [{ value: initialCode, text: initialText, label: initialText, sublabel: initialCode }];
            config.items = [initialCode];
        }
        var ts = new TomSelect(selectEl, config);
        if (initialCode) {
            loadReferenceSelected(kind, initialCode, selectEl);
        }
        return ts;
    };

    ui.initJobCatalogPicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        normalizeEmptyOption(selectEl);
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث في دليل الوظائف...';
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        var gradeInput = selectEl.form ? selectEl.form.querySelector('[name="GradeCode"]') : null;
        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            plugins: ['dropdown_input'],
            dir: isRtl ? 'rtl' : 'ltr',
            preload: true,
            load: function (query, callback) {
                remoteLoad('/api/lookup/job-catalog', query, callback);
            },
            shouldLoad: function (query) {
                return query.length === 0 || query.length >= 2;
            },
            render: {
                option: function (data, escape) {
                    return '<div><span>' + escape(data.label || data.text) + '</span>' +
                        (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                        '</div>';
                },
                no_results: function () {
                    return '<div class="no-results px-2 py-1">اكتب حرفين على الأقل للبحث في دليل الوظائف...</div>';
                }
            },
            onChange: function (value) {
                if (!gradeInput) return;
                if (!value) {
                    if (gradeInput.tomselect) {
                        gradeInput.tomselect.clear(true);
                    } else {
                        gradeInput.value = '';
                    }
                    return;
                }
                fetch(lookupBase() + '/api/lookup/job-catalog/' + encodeURIComponent(value), {
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (item) {
                        if (item && item.gradeCode) setGradeCodeValue(gradeInput, item.gradeCode);
                    })
                    .catch(function () { /* ignore */ });
            },
            onDropdownOpen: function () {
                var input = this.dropdown.querySelector('.dropdown-input');
                if (input) {
                    input.setAttribute('placeholder', placeholder);
                    input.focus();
                }
            }
        });
        loadSelected('/api/lookup/job-catalog', selectEl.value, selectEl);
        if (selectEl.value && gradeInput) {
            fetch(lookupBase() + '/api/lookup/job-catalog/' + encodeURIComponent(selectEl.value), {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (item) {
                    if (item && item.gradeCode && !gradeInput.value) setGradeCodeValue(gradeInput, item.gradeCode);
                })
                .catch(function () { /* ignore */ });
        }
        return ts;
    };

    ui.initSelectPicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        var placeholder = selectEl.getAttribute('data-placeholder') || '';
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        return new TomSelect(selectEl, {
            allowEmptyOption: true,
            create: false,
            maxOptions: null,
            placeholder: placeholder,
            plugins: ['dropdown_input'],
            sortField: { field: 'text', direction: 'asc' },
            dir: isRtl ? 'rtl' : 'ltr'
        });
    };

    function defaultJobTitle(option) {
        if (!option) return '';
        if (option.jobTitle) return option.jobTitle;
        return option.gradeCode || '';
    }

    function formatVacantPosition(p) {
        var title = p.jobTitle || p.gradeCode || '';
        return p.positionCode + ' — ' + title + ' (' + (p.orgUnitName || '—') + ')';
    }

    function vacantPositionsUrl(budgetYear, query, includeId, orgUnitId) {
        var url = lookupBase() + '/api/recruitment/vacant-positions?budgetYear=' + encodeURIComponent(budgetYear || new Date().getFullYear());
        url += '&orgUnitId=' + encodeURIComponent(orgUnitId || '');
        if (query) url += '&q=' + encodeURIComponent(query);
        if (includeId) url += '&includeId=' + encodeURIComponent(includeId);
        return url;
    }

    ui.initVacantPositionPicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        normalizeEmptyOption(selectEl);
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث عن منصب شاغر...';
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        var budgetYearInput = selectEl.form ? selectEl.form.querySelector('[name="BudgetYear"]') : null;
        var scopeSelect = selectEl.form ? selectEl.form.querySelector('[name="ScopeOrgUnitId"]') : null;
        var jobTitleInput = selectEl.form ? selectEl.form.querySelector('[name="JobTitle"]') : null;
        var includeId = selectEl.getAttribute('data-include-id') || selectEl.value || '';

        function readBudgetYear() {
            return budgetYearInput ? budgetYearInput.value : String(new Date().getFullYear());
        }

        function readScopeOrgUnitId() {
            return scopeSelect ? scopeSelect.value : '';
        }

        function mapVacantOption(p) {
            return {
                value: p.id,
                text: formatVacantPosition(p),
                jobTitle: p.jobTitle || '',
                gradeCode: p.gradeCode || ''
            };
        }

        function loadVacantPositions(query, callback) {
            var orgUnitId = readScopeOrgUnitId();
            if (!orgUnitId) {
                callback([]);
                return;
            }

            fetch(vacantPositionsUrl(readBudgetYear(), query, includeId, orgUnitId), {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('vacant-positions failed');
                    return r.json();
                })
                .then(function (data) {
                    callback((data || []).map(mapVacantOption));
                })
                .catch(function () {
                    ui.handleAjaxError({ status: 0 });
                    callback();
                });
        }

        function resetPositionPicker() {
            includeId = '';
            ts.clearOptions();
            ts.clear(true);
            if (jobTitleInput) jobTitleInput.value = '';
        }

        function syncPositionPickerState() {
            if (readScopeOrgUnitId()) {
                ts.enable();
            } else {
                ts.disable();
                resetPositionPicker();
            }
        }

        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            plugins: ['dropdown_input'],
            preload: true,
            dir: isRtl ? 'rtl' : 'ltr',
            load: function (query, callback) {
                loadVacantPositions(query, callback);
            },
            shouldLoad: function () {
                return !!readScopeOrgUnitId();
            },
            render: {
                option: function (data, escape) {
                    return '<div>' + escape(data.text) + '</div>';
                },
                no_results: function () {
                    if (!readScopeOrgUnitId()) {
                        return '<div class="no-results px-2 py-1">حدّد الفرع أو المركزية أولاً</div>';
                    }
                    return '<div class="no-results px-2 py-1">لا توجد مناصب شاغرة مطابقة</div>';
                }
            },
            onChange: function (value) {
                if (!jobTitleInput) return;
                if (!value) {
                    jobTitleInput.value = '';
                    return;
                }
                var option = this.options[value];
                jobTitleInput.value = defaultJobTitle(option);
            },
            onDropdownOpen: function () {
                if (readScopeOrgUnitId()) {
                    this.load('');
                }
                var input = this.dropdown.querySelector('.dropdown-input');
                if (input) {
                    input.setAttribute('placeholder', readScopeOrgUnitId() ? placeholder : 'حدّد الفرع أو المركزية أولاً');
                    input.focus();
                }
            }
        });

        syncPositionPickerState();

        if (selectEl.value && readScopeOrgUnitId()) {
            loadVacantPositions('', function (options) {
                options.forEach(function (opt) {
                    ts.addOption(opt);
                });
                ts.setValue(selectEl.value, true);
            });
        }

        if (scopeSelect && !scopeSelect.disabled) {
            scopeSelect.addEventListener('change', function () {
                resetPositionPicker();
                syncPositionPickerState();
                if (readScopeOrgUnitId()) {
                    ts.load('');
                }
            });
        }

        if (budgetYearInput && !budgetYearInput.readOnly) {
            budgetYearInput.addEventListener('change', function () {
                resetPositionPicker();
                if (readScopeOrgUnitId()) {
                    ts.load('');
                }
            });
        }

        return ts;
    };

    ui.initOrgUnitPicker = function (selectEl) {
        if (!selectEl || selectEl.tomselect || typeof TomSelect === 'undefined') return;
        normalizeEmptyOption(selectEl);
        var placeholder = selectEl.getAttribute('data-placeholder') || 'ابحث عن وحدة تنظيمية...';
        var isRtl = document.documentElement.getAttribute('dir') === 'rtl';
        var ts = new TomSelect(selectEl, {
            valueField: 'value',
            labelField: 'text',
            searchField: ['text'],
            placeholder: placeholder,
            allowEmptyOption: true,
            maxOptions: 50,
            plugins: ['dropdown_input'],
            dir: isRtl ? 'rtl' : 'ltr',
            preload: true,
            load: function (query, callback) {
                remoteLoad('/api/lookup/org-units', query, callback);
            },
            shouldLoad: function (query) {
                return query.length === 0 || query.length >= 2;
            },
            render: {
                option: function (data, escape) {
                    return '<div><span>' + escape(data.label || data.text) + '</span>' +
                        (data.sublabel ? ' <small class="text-muted">' + escape(data.sublabel) + '</small>' : '') +
                        '</div>';
                },
                no_results: function () {
                    return '<div class="no-results px-2 py-1">اكتب حرفين على الأقل للبحث عن وحدة...</div>';
                }
            },
            onDropdownOpen: function () {
                var input = this.dropdown.querySelector('.dropdown-input');
                if (input) {
                    input.setAttribute('placeholder', placeholder);
                    input.focus();
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
        document.querySelectorAll('[data-hr-picker="job-catalog"]').forEach(ui.initJobCatalogPicker);
        document.querySelectorAll('[data-hr-picker="reference-grade"]').forEach(function (el) {
            ui.initReferenceDataPicker(el, 'Grade');
        });
        document.querySelectorAll('[data-hr-picker="reference-job-classification"]').forEach(function (el) {
            ui.initReferenceDataPicker(el, 'JobClassification');
        });
        document.querySelectorAll('[data-hr-picker="vacant-position"]').forEach(ui.initVacantPositionPicker);
        document.querySelectorAll('[data-hr-picker="select"]').forEach(ui.initSelectPicker);
        ui.initOrgUnitTreeModal();
    });

})(window.hrSharedUi);
