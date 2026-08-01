window.hrSharedUi = window.hrSharedUi || {};

(function (ui) {
    'use strict';

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
        var msg = message || 'هل أنت متأكد من الحذف؟ لا يمكن التراجع عن هذا الإجراء.';
        if (!confirm(msg)) return false;

        if (typeof formOrUrl === 'string') {
            window.location.href = formOrUrl;
        } else if (formOrUrl && formOrUrl.submit) {
            formOrUrl.submit();
        }
        return true;
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
            } else if (xhr.status >= 500) {
                msg = 'خطأ في الخادم. يرجى المحاولة لاحقاً.';
            } else if (xhr.responseJSON && xhr.responseJSON.message) {
                msg = xhr.responseJSON.message;
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
            window.alert(msg);
        }
    };

    ui.postAjax = function (url, data, onSuccess) {
        var token = document.querySelector('input[name="__RequestVerificationToken"]');
        var headers = { 'Content-Type': 'application/json' };
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
                ui.handleAjaxError({ status: response.status });
                return;
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

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-hr-confirm-delete]').forEach(function (el) {
            el.addEventListener('click', function (e) {
                if (!confirm(el.dataset.hrConfirmDelete || 'هل أنت متأكد من الحذف؟')) {
                    e.preventDefault();
                }
            });
        });

        document.querySelectorAll('[data-hr-ajax-post]').forEach(function (el) {
            el.addEventListener('click', function (e) {
                e.preventDefault();
                var url = el.dataset.hrAjaxPost;
                var confirmMsg = el.dataset.hrAjaxConfirm;
                if (confirmMsg && !confirm(confirmMsg)) return;

                ui.postAjax(url, null, function () {
                    var successMsg = el.dataset.hrAjaxSuccess || 'تمت العملية بنجاح';
                    ui.showSuccess(successMsg);
                    if (el.dataset.hrAjaxReload === 'true') {
                        setTimeout(function () { location.reload(); }, 500);
                    }
                });
            });
        });
    });

})(window.hrSharedUi);
