window.hrSharedUi = window.hrSharedUi || {};

(function (ui) {
    'use strict';

    var VARIANTS = {
        danger: {
            headerClass: 'hr-confirm--danger',
            icon: 'fas fa-trash',
            btnClass: 'btn hr-confirm-btn-danger'
        },
        warning: {
            headerClass: 'hr-confirm--warning',
            icon: 'fas fa-exclamation-triangle',
            btnClass: 'btn btn-primary'
        },
        info: {
            headerClass: 'hr-confirm--info',
            icon: 'fas fa-info-circle',
            btnClass: 'btn btn-primary'
        }
    };

    var confirmModalInstance = null;
    var confirmResolve = null;
    var confirmReject = null;
    var confirmMode = 'confirm';

    /** UX-001: HTML-escape untrusted strings before any HTML context. */
    ui.escapeHtml = function (value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    };

    function getDefaults() {
        var el = document.getElementById('hrConfirmDefaults');
        if (!el) {
            return {
                cancel: 'إلغاء',
                confirm: 'تأكيد',
                delete: 'حذف',
                confirmDelete: 'هل أنت متأكد من الحذف؟'
            };
        }
        return {
            cancel: el.dataset.hrDefaultCancel || 'إلغاء',
            confirm: el.dataset.hrDefaultConfirm || 'تأكيد',
            delete: el.dataset.hrDefaultDelete || 'حذف',
            confirmDelete: el.dataset.hrDefaultConfirmDelete || 'هل أنت متأكد من الحذف؟'
        };
    }

    function getConfirmModal() {
        var modalEl = document.getElementById('hrConfirmModal');
        if (!modalEl || typeof bootstrap === 'undefined') return null;
        if (!confirmModalInstance) {
            confirmModalInstance = new bootstrap.Modal(modalEl, { focus: true });
            modalEl.addEventListener('hidden.bs.modal', function () {
                if (confirmResolve) {
                    var resolve = confirmResolve;
                    confirmResolve = null;
                    confirmReject = null;
                    resolve(confirmMode === 'alert' ? undefined : false);
                }
            });
        }
        return { el: modalEl, instance: confirmModalInstance };
    }

    function applyVariant(modalEl, variantKey) {
        var cfg = VARIANTS[variantKey] || VARIANTS.warning;
        var header = modalEl.querySelector('#hrConfirmHeader');
        var icon = modalEl.querySelector('#hrConfirmIcon');
        var okBtn = modalEl.querySelector('#hrConfirmOk');
        if (header) {
            header.classList.remove('hr-confirm--danger', 'hr-confirm--warning', 'hr-confirm--info');
            header.classList.add(cfg.headerClass);
        }
        if (icon) {
            icon.className = cfg.icon + ' hr-confirm-icon';
        }
        if (okBtn) {
            okBtn.className = cfg.btnClass;
        }
        return cfg;
    }

    function openConfirmDialog(options) {
        var modal = getConfirmModal();
        if (!modal) {
            return Promise.resolve(false);
        }

        var defaults = getDefaults();
        var variantKey = options.variant || 'warning';
        var isAlert = options.alert === true;
        confirmMode = isAlert ? 'alert' : 'confirm';

        applyVariant(modal.el, variantKey);

        var titleEl = modal.el.querySelector('#hrConfirmTitle');
        var messageEl = modal.el.querySelector('#hrConfirmMessage');
        var cancelBtn = modal.el.querySelector('#hrConfirmCancel');
        var okBtn = modal.el.querySelector('#hrConfirmOk');

        if (titleEl) titleEl.textContent = options.title || defaults.confirm;
        if (messageEl) messageEl.textContent = options.message || '';

        var confirmLabel = options.confirmLabel
            || (variantKey === 'danger' ? defaults.delete : defaults.confirm);
        if (okBtn) okBtn.textContent = confirmLabel;
        if (cancelBtn) {
            cancelBtn.textContent = options.cancelLabel || defaults.cancel;
            cancelBtn.style.display = isAlert ? 'none' : '';
        }

        if (options.icon) {
            var iconEl = modal.el.querySelector('#hrConfirmIcon');
            if (iconEl) iconEl.className = options.icon + ' hr-confirm-icon';
        }

        return new Promise(function (resolve, reject) {
            confirmResolve = resolve;
            confirmReject = reject;

            var onOk = function () {
                okBtn.removeEventListener('click', onOk);
                var resolve = confirmResolve;
                confirmResolve = null;
                confirmReject = null;
                modal.instance.hide();
                if (resolve) resolve(isAlert ? undefined : true);
            };

            okBtn.addEventListener('click', onOk, { once: true });
            modal.instance.show();

            modal.el.addEventListener('shown.bs.modal', function focusCancel() {
                modal.el.removeEventListener('shown.bs.modal', focusCancel);
                var focusTarget = isAlert ? okBtn : (cancelBtn || okBtn);
                if (focusTarget) focusTarget.focus();
            }, { once: true });
        });
    }

    ui.confirm = function (options) {
        options = options || {};
        if (typeof options === 'string') {
            options = { message: options };
        }
        return openConfirmDialog(options).then(function (result) { return result === true; });
    };

    ui.alert = function (options) {
        options = options || {};
        if (typeof options === 'string') {
            options = { message: options };
        }
        options.alert = true;
        options.variant = options.variant || 'info';
        options.title = options.title || 'تنبيه';
        options.confirmLabel = options.confirmLabel || 'حسناً';
        return openConfirmDialog(options).then(function () { });
    };

    function extractJsonErrorMessage(body) {
        if (!body || typeof body !== 'object') return null;
        if (body.error) return String(body.error);
        if (body.message) return String(body.message);
        return null;
    }

    ui.showConflictAlert = function (message, title) {
        return ui.alert({
            title: title || 'تعذّر الحفظ',
            message: message || 'تعذّر إتمام العملية.',
            variant: 'danger',
            confirmLabel: 'حسناً'
        });
    };

    function showDialogFromElement(el) {
        if (!el || el.dataset.show !== 'true') return;
        var message = el.dataset.message || '';
        if (!message) return;
        ui.showConflictAlert(message, el.dataset.title || 'تعذّر الحفظ');
    }

    function showFlashAndValidationDialogs() {
        showDialogFromElement(document.getElementById('hrFlashDialog'));
        showDialogFromElement(document.getElementById('hrValidationDialog'));
    }

    ui.installFetchConflictHandler = function () {
        if (window.__hrFetchConflictPatched) return;
        window.__hrFetchConflictPatched = true;
        var nativeFetch = window.fetch.bind(window);
        window.fetch = function (input, init) {
            return nativeFetch(input, init).then(function (response) {
                if (response.status !== 409 && response.status !== 400)
                    return response;

                var contentType = response.headers.get('content-type') || '';
                if (contentType.indexOf('json') < 0)
                    return response;

                return response.clone().json().then(function (body) {
                    var msg = extractJsonErrorMessage(body);
                    if (msg)
                        ui.showConflictAlert(msg);
                    return response;
                }).catch(function () {
                    return response;
                });
            });
        };
    };

    function setAlertContent(alert, message) {
        alert.textContent = '';
        var text = document.createTextNode(message == null ? '' : String(message));
        alert.appendChild(text);
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'btn-close';
        btn.setAttribute('data-bs-dismiss', 'alert');
        btn.setAttribute('aria-label', 'إغلاق');
        alert.appendChild(btn);
    }

    ui.confirmDelete = function (formOrUrl, message) {
        var defaults = getDefaults();
        return ui.confirm({
            title: defaults.delete,
            message: message || defaults.confirmDelete + ' لا يمكن التراجع عن هذا الإجراء.',
            variant: 'danger',
            confirmLabel: defaults.delete
        }).then(function (ok) {
            if (!ok) return false;
            if (typeof formOrUrl === 'string') {
                window.location.href = formOrUrl;
            } else if (formOrUrl && formOrUrl.submit) {
                formOrUrl.submit();
            }
            return true;
        });
    };

    ui.showLoader = function () {
        var overlay = document.getElementById('hr-loader-overlay');
        if (!overlay) {
            overlay = document.createElement('div');
            overlay.id = 'hr-loader-overlay';
            overlay.style.cssText =
                'position:fixed;inset:0;background:rgba(0,0,0,0.35);z-index:9999;' +
                'display:flex;align-items:center;justify-content:center;';
            var spinner = document.createElement('div');
            spinner.className = 'spinner-border text-light';
            spinner.setAttribute('role', 'status');
            var sr = document.createElement('span');
            sr.className = 'visually-hidden';
            sr.textContent = 'جاري التحميل...';
            spinner.appendChild(sr);
            overlay.appendChild(spinner);
            document.body.appendChild(overlay);
        }
        overlay.style.display = 'flex';
    };

    ui.hideLoader = function () {
        var overlay = document.getElementById('hr-loader-overlay');
        if (overlay) overlay.style.display = 'none';
    };

    ui.formatDateAr = function (dateInput) {
        if (!dateInput) return '';
        var d = dateInput instanceof Date ? dateInput : new Date(dateInput);
        if (isNaN(d.getTime())) return '';
        try {
            return d.toLocaleDateString('ar-SY', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
        } catch (_) {
            return d.toLocaleDateString('ar', {
                year: 'numeric', month: '2-digit', day: '2-digit'
            });
        }
    };

    ui.handleAjaxError = function (xhr) {
        var msg = 'حدث خطأ غير متوقع.';
        if (xhr) {
            if (xhr.status === 0) {
                msg = 'لا يمكن الاتصال بالخادم. تحقق من اتصال الشبكة.';
            } else if (xhr.status === 403) {
                msg = 'ليس لديك صلاحية لتنفيذ هذا الإجراء.';
            } else if (xhr.status === 404) {
                msg = 'المورد المطلوب غير موجود.';
            } else if (xhr.status === 409) {
                msg = extractJsonErrorMessage(xhr.responseJSON) || 'يتعارض الطلب مع سجل موجود مسبقاً.';
                ui.showConflictAlert(msg);
                return;
            } else if (xhr.status >= 500) {
                msg = 'خطأ في الخادم. يرجى المحاولة لاحقاً.';
            } else if (xhr.responseJSON) {
                msg = extractJsonErrorMessage(xhr.responseJSON) || msg;
            }
        }

        var container = document.querySelector('.content > .container-fluid')
            || document.querySelector('.app-content > .container-fluid');
        if (container) {
            var alert = document.createElement('div');
            alert.className = 'alert alert-danger alert-dismissible fade show';
            alert.setAttribute('role', 'alert');
            setAlertContent(alert, msg);
            container.insertBefore(alert, container.firstChild);
        } else {
            ui.showConflictAlert(msg, 'خطأ');
        }
    };

    ui.postAjax = function (url, data, onSuccess) {
        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        var headers = { 'Content-Type': 'application/json', 'Accept': 'application/json' };
        if (token) headers['RequestVerificationToken'] = token.value;

        ui.showLoader();
        fetch(url, {
            method: 'POST',
            headers: headers,
            body: data ? JSON.stringify(data) : null
        })
        .then(function (response) {
            ui.hideLoader();
            if (!response.ok) {
                return response.json().catch(function () { return null; }).then(function (body) {
                    ui.handleAjaxError({
                        status: response.status,
                        responseJSON: body
                    });
                });
            }
            return response.json().catch(function () { return {}; });
        })
        .then(function (result) {
            if (result && onSuccess) onSuccess(result);
        })
        .catch(function () {
            ui.hideLoader();
            ui.handleAjaxError({ status: 0 });
        });
    };

    ui.showSuccess = function (message) {
        var container = document.querySelector('.content > .container-fluid')
            || document.querySelector('.app-content > .container-fluid');
        if (!container) return;
        var alert = document.createElement('div');
        alert.className = 'alert alert-success alert-dismissible fade show';
        alert.setAttribute('role', 'alert');
        setAlertContent(alert, message);
        container.insertBefore(alert, container.firstChild);
    };

    function bindConfirmForms() {
        document.querySelectorAll('form[data-hr-confirm]').forEach(function (form) {
            if (form.dataset.hrConfirmBound === 'true') return;
            form.dataset.hrConfirmBound = 'true';
            form.addEventListener('submit', function (e) {
                if (form.dataset.hrConfirmSkip === 'true') {
                    form.dataset.hrConfirmSkip = '';
                    return;
                }
                e.preventDefault();
                ui.confirm({
                    title: form.dataset.hrConfirmTitle || getDefaults().confirm,
                    message: form.dataset.hrConfirm || '',
                    variant: form.dataset.hrConfirmVariant || 'warning',
                    confirmLabel: form.dataset.hrConfirmLabel || getDefaults().confirm,
                    cancelLabel: form.dataset.hrConfirmCancel || getDefaults().cancel
                }).then(function (ok) {
                    if (ok) {
                        form.dataset.hrConfirmSkip = 'true';
                        form.submit();
                    }
                });
            });
        });
    }

    function bindConfirmDeleteElements() {
        var defaults = getDefaults();
        document.querySelectorAll('[data-hr-confirm-delete]').forEach(function (el) {
            if (el.dataset.hrConfirmDeleteBound === 'true') return;
            el.dataset.hrConfirmDeleteBound = 'true';
            el.addEventListener('click', function (e) {
                e.preventDefault();
                ui.confirm({
                    title: defaults.delete,
                    message: el.dataset.hrConfirmDelete || defaults.confirmDelete,
                    variant: 'danger',
                    confirmLabel: defaults.delete
                }).then(function (ok) {
                    if (!ok) return;
                    if (el.tagName === 'A' && el.href) {
                        window.location.href = el.href;
                    } else if (el.form) {
                        el.form.submit();
                    }
                });
            });
        });
    }

    function bindLegacyDeleteModal() {
        var defaults = getDefaults();
        document.querySelectorAll('[data-delete-url]').forEach(function (button) {
            if (button.dataset.hrLegacyDeleteBound === 'true') return;
            button.dataset.hrLegacyDeleteBound = 'true';

            button.addEventListener('click', function (e) {
                var target = button.getAttribute('data-bs-target');
                if (target && target !== '#deleteModal' && target !== '#hrConfirmModal') return;

                e.preventDefault();
                e.stopPropagation();

                var deleteUrl = button.getAttribute('data-delete-url');
                var message = button.getAttribute('data-delete-message')
                    || defaults.confirmDelete + ' لا يمكن التراجع عن هذا الإجراء.';

                ui.confirm({
                    title: defaults.delete,
                    message: message,
                    variant: 'danger',
                    confirmLabel: defaults.delete
                }).then(function (ok) {
                    if (!ok || !deleteUrl) return;
                    var form = document.getElementById('hrConfirmDeleteForm');
                    if (form) {
                        form.action = deleteUrl;
                        form.submit();
                    } else {
                        window.location.href = deleteUrl;
                    }
                });
            });
        });
    }

    function bindAjaxConfirm() {
        document.querySelectorAll('[data-hr-ajax-post]').forEach(function (el) {
            if (el.dataset.hrAjaxBound === 'true') return;
            el.dataset.hrAjaxBound = 'true';
            el.addEventListener('click', function (e) {
                e.preventDefault();
                var url = el.dataset.hrAjaxPost;
                var confirmMsg = el.dataset.hrAjaxConfirm;

                function runPost() {
                    ui.postAjax(url, null, function () {
                        var successMsg = el.dataset.hrAjaxSuccess || 'تمت العملية بنجاح';
                        ui.showSuccess(successMsg);
                        if (el.dataset.hrAjaxReload === 'true') {
                            setTimeout(function () { location.reload(); }, 500);
                        }
                    });
                }

                if (confirmMsg) {
                    ui.confirm({
                        message: confirmMsg,
                        variant: el.dataset.hrAjaxConfirmVariant || 'warning',
                        title: el.dataset.hrAjaxConfirmTitle || getDefaults().confirm
                    }).then(function (ok) {
                        if (ok) runPost();
                    });
                } else {
                    runPost();
                }
            });
        });
    }

    function normalizeNavToken(value) {
        return (value || '').trim().toLowerCase();
    }

    function parseMvcSidebarLink(link) {
        var href = link.getAttribute('href');
        if (!href || href === '#' || href.indexOf('javascript:') === 0) return null;

        try {
            var url = new URL(link.href, window.location.origin);
            var parts = url.pathname.split('/').filter(Boolean);
            if (parts.length >= 4 && parts[0] === 'ui') {
                return {
                    controller: parts[parts.length - 2],
                    action: parts[parts.length - 1]
                };
            }
            if (parts.length >= 2) {
                return {
                    controller: parts[parts.length - 2],
                    action: parts[parts.length - 1]
                };
            }
        } catch (_) {
            return null;
        }

        return null;
    }

    function pickActiveSidebarLink(links, controller, action) {
        if (!controller) return null;

        var normalizedController = normalizeNavToken(controller);
        var normalizedAction = normalizeNavToken(action);
        var byController = {};

        links.forEach(function (link) {
            var parsed = parseMvcSidebarLink(link);
            if (!parsed) return;

            var key = normalizeNavToken(parsed.controller);
            if (!byController[key]) byController[key] = [];
            byController[key].push({
                link: link,
                action: normalizeNavToken(parsed.action)
            });
        });

        var candidates = byController[normalizedController] || [];
        if (!candidates.length) return null;

        var exact = candidates.find(function (candidate) {
            return candidate.action === normalizedAction;
        });
        if (exact) return exact.link;

        var sidebarActions = {};
        candidates.forEach(function (candidate) {
            sidebarActions[candidate.action] = true;
        });

        if (!sidebarActions[normalizedAction]) {
            var indexLink = candidates.find(function (candidate) {
                return candidate.action === 'index';
            });
            if (indexLink) return indexLink.link;
        }

        return null;
    }

    function scrollSidebarToActive(activeLink) {
        var wrapper = document.querySelector('.sidebar-wrapper');
        if (!wrapper || !activeLink) return;

        var item = activeLink.closest('.nav-item');
        if (!item) return;

        var wrapperRect = wrapper.getBoundingClientRect();
        var itemRect = item.getBoundingClientRect();
        var relativeTop = itemRect.top - wrapperRect.top + wrapper.scrollTop;
        var targetScroll = relativeTop - (wrapper.clientHeight / 2) + (item.offsetHeight / 2);
        var maxScroll = Math.max(0, wrapper.scrollHeight - wrapper.clientHeight);
        wrapper.scrollTop = Math.max(0, Math.min(targetScroll, maxScroll));
    }

    function bindSidebarScrollPersistence(wrapper, storageKey) {
        if (!wrapper || !storageKey || wrapper.dataset.hrSidebarScrollBound === 'true') return;
        wrapper.dataset.hrSidebarScrollBound = 'true';

        var saveTimer = null;
        wrapper.addEventListener('scroll', function () {
            if (saveTimer) window.clearTimeout(saveTimer);
            saveTimer = window.setTimeout(function () {
                try {
                    sessionStorage.setItem(storageKey, String(wrapper.scrollTop));
                } catch (_) { }
            }, 150);
        });
    }

    function initSidebarNavigation() {
        var menu = document.querySelector('.sidebar-menu');
        var wrapper = document.querySelector('.sidebar-wrapper');
        if (!menu || !wrapper) return;

        var controller = document.body.dataset.navController || '';
        var action = document.body.dataset.navAction || '';
        var moduleSlug = document.body.dataset.moduleSlug || 'default';
        var storageKey = 'hr:sidebar:scroll:' + moduleSlug;
        var links = Array.prototype.slice.call(menu.querySelectorAll('.nav-link[href]'));

        bindSidebarScrollPersistence(wrapper, storageKey);

        var activeLink = pickActiveSidebarLink(links, controller, action);
        if (activeLink) {
            activeLink.classList.add('active');
            var activeItem = activeLink.closest('.nav-item');
            if (activeItem) activeItem.classList.add('active');
            scrollSidebarToActive(activeLink);
            try {
                sessionStorage.setItem(storageKey, String(wrapper.scrollTop));
            } catch (_) { }
            return;
        }

        try {
            var savedScroll = sessionStorage.getItem(storageKey);
            if (savedScroll !== null) {
                wrapper.scrollTop = parseInt(savedScroll, 10) || 0;
            }
        } catch (_) { }
    }

    function scrollGlobalAlertsIntoView() {
        var alertsHost = document.querySelector('[data-hr-alerts]')
            || document.querySelector('.content > .container-fluid > .alert')
            || document.querySelector('.app-content .alert-success, .app-content .alert-danger');
        if (!alertsHost) {
            var danger = document.querySelector('.alert-danger, .alert-success');
            if (danger) alertsHost = danger;
        }
        if (alertsHost && typeof alertsHost.scrollIntoView === 'function') {
            alertsHost.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    function showAssetFormError(form, message) {
        ui.alert({
            title: 'تعذّر الحفظ',
            message: message,
            variant: 'danger',
            confirmLabel: 'حسناً'
        }).then(function () {
            if (form) {
                form.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    }

    function showInlineFormError(form, message) {
        var existing = form.querySelector('[data-hr-asset-form-error]');
        if (existing) {
            existing.textContent = message;
            existing.classList.remove('d-none');
            existing.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            return;
        }
        var wrap = document.createElement('div');
        wrap.className = 'col-12';
        var alert = document.createElement('div');
        alert.className = 'alert alert-danger py-2 mb-0';
        alert.setAttribute('role', 'alert');
        alert.setAttribute('data-hr-asset-form-error', '1');
        alert.textContent = message;
        wrap.appendChild(alert);
        form.insertBefore(wrap, form.firstChild);
        alert.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function getTomSelectValue(selectEl) {
        if (!selectEl) return '';
        if (selectEl.tomselect) return selectEl.tomselect.getValue() || '';
        return selectEl.value || '';
    }

    function hasAssetProof(form) {
        var evidenceInput = form.querySelector('input[name="evidenceRef"]');
        var evidenceRef = evidenceInput ? (evidenceInput.value || '').trim() : '';
        if (evidenceRef.length >= 2 &&
            evidenceRef !== 'موقّع' &&
            evidenceRef.toLowerCase() !== 'موقع') {
            return true;
        }
        var fileIdInput = form.querySelector('[data-file-id-input]');
        if (fileIdInput && (fileIdInput.value || '').trim()) return true;
        return false;
    }

    function validateAssetDetailForm(form) {
        var formKind = form.getAttribute('data-hr-asset-form');
        var requiresProof = form.getAttribute('data-hr-requires-proof') === 'true';
        var uploadFlag = form.querySelector('[data-upload-in-progress-flag]');
        if (uploadFlag && uploadFlag.value === '1') {
            showAssetFormError(form, 'انتظر اكتمال رفع الملف قبل الإرسال');
            return false;
        }
        if (form.querySelector('[data-file-upload-widget][data-uploading="1"]')) {
            showAssetFormError(form, 'انتظر اكتمال رفع الملف قبل الإرسال');
            return false;
        }
        if (formKind === 'assign') {
            var employeeSelect = form.querySelector('select[name="employeeId"]');
            var employeeId = getTomSelectValue(employeeSelect);
            if (!employeeId || employeeId === '00000000-0000-0000-0000-000000000000') {
                showAssetFormError(form, 'اختر موظفاً قبل التسليم');
                return false;
            }
        }
        if (requiresProof && !hasAssetProof(form)) {
            var proofMsg = formKind === 'return'
                ? 'أصل مميز: أرفق ملف الاسترداد أو اكتب مرجع دليل — يكفي أحدهما'
                : formKind === 'loss'
                    ? 'أصل مميز: أرفق ملف دليل الفقدان أو اكتب مرجع دليل — يكفي أحدهما'
                    : 'أصل مميز: أرفق ملف المحضر أو اكتب مرجع دليل (رقم محضر / EVD-...) — يكفي أحدهما';
            showAssetFormError(form, proofMsg);
            return false;
        }
        return true;
    }

    function bindAssetDetailForms() {
        document.querySelectorAll('form[data-hr-asset-form]').forEach(function (form) {
            if (form.dataset.hrAssetFormBound === 'true') return;
            form.dataset.hrAssetFormBound = 'true';
            form.addEventListener('submit', function (e) {
                if (form.dataset.hrAssetValidationSkip === 'true') {
                    form.dataset.hrAssetValidationSkip = '';
                    return;
                }
                if (!validateAssetDetailForm(form)) {
                    e.preventDefault();
                    e.stopPropagation();
                }
            }, true);
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        ui.installFetchConflictHandler();
        bindConfirmForms();
        bindConfirmDeleteElements();
        bindLegacyDeleteModal();
        bindAjaxConfirm();
        bindAssetDetailForms();
        showFlashAndValidationDialogs();
        scrollGlobalAlertsIntoView();
        initSidebarNavigation();
    });

})(window.hrSharedUi);
