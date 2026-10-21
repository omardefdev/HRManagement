(function () {
    'use strict';

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + ' بايت';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' ك.ب';
        return (bytes / 1048576).toFixed(1) + ' م.ب';
    }

    function requireAttribute(root, name) {
        var value = root.getAttribute(name);
        if (!value) {
            console.error('[HrFileUpload] Missing required attribute: ' + name, root);
            throw new Error('إعداد رفع الملف غير مكتمل.');
        }
        return value;
    }

    function buildDownloadUrl(root, fileId) {
        var template = requireAttribute(root, 'data-download-url-template');
        return template.replace('{fileId}', fileId);
    }

    function getZone(pickerRoot) {
        return pickerRoot.querySelector('[data-picker-zone]') || pickerRoot;
    }

    function showEmptyState(pickerRoot) {
        var zone = getZone(pickerRoot);
        var empty = pickerRoot.querySelector('[data-picker-empty]');
        var file = pickerRoot.querySelector('[data-picker-file]');
        if (empty) empty.classList.remove('d-none');
        if (file) file.classList.add('d-none');
        zone.classList.remove('is-selected', 'is-uploading', 'is-error');
    }

    function showFileState(pickerRoot, fileName, meta) {
        var zone = getZone(pickerRoot);
        var empty = pickerRoot.querySelector('[data-picker-empty]');
        var file = pickerRoot.querySelector('[data-picker-file]');
        var nameEl = pickerRoot.querySelector('[data-file-picker-name]');
        var statusEl = pickerRoot.querySelector('[data-file-status]');

        if (empty) empty.classList.add('d-none');
        if (file) file.classList.remove('d-none');
        if (nameEl) nameEl.textContent = fileName || '';
        if (statusEl && meta !== undefined) statusEl.textContent = meta || '';
        zone.classList.add('is-selected');
        zone.classList.remove('is-uploading', 'is-error');
    }

    function setUploading(pickerRoot, message) {
        var zone = getZone(pickerRoot);
        zone.classList.add('is-uploading');
        zone.classList.remove('is-error');
        var statusEl = pickerRoot.querySelector('[data-file-status]');
        if (statusEl) statusEl.textContent = message || 'جاري الرفع...';
    }

    function findParentForm(element) {
        var node = element;
        while (node && node !== document.body) {
            if (node.tagName === 'FORM') return node;
            node = node.parentElement;
        }
        return null;
    }

    function getUploadProgressFlag(form) {
        if (!form) return null;
        return form.querySelector('[data-upload-in-progress-flag]');
    }

    function setFormUploading(form, uploading) {
        if (!form) return;
        var flag = getUploadProgressFlag(form);
        if (flag) flag.value = uploading ? '1' : '0';
        form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(function (btn) {
            btn.disabled = uploading;
        });
    }

    function setWidgetUploading(widgetRoot, uploading) {
        if (!widgetRoot) return;
        if (uploading) {
            widgetRoot.setAttribute('data-uploading', '1');
        } else {
            widgetRoot.removeAttribute('data-uploading');
        }
        var form = findParentForm(widgetRoot);
        var activeCount = form
            ? form.querySelectorAll('[data-file-upload-widget][data-uploading="1"]').length
            : 0;
        if (form) {
            if (uploading || activeCount > 0) {
                setFormUploading(form, true);
            } else {
                setFormUploading(form, false);
            }
        }
    }

    function setError(pickerRoot, message) {
        var zone = getZone(pickerRoot);
        zone.classList.add('is-error');
        zone.classList.remove('is-uploading');
        var statusEl = pickerRoot.querySelector('[data-file-status]');
        if (statusEl) statusEl.textContent = message || 'فشل الرفع';
    }

    async function uploadFile(file, uploadUrl) {
        var formData = new FormData();
        formData.append('file', file);
        var response = await fetch(uploadUrl, {
            method: 'POST',
            body: formData,
            credentials: 'include'
        });
        if (!response.ok) {
            var text = await response.text();
            var message = text || 'فشل رفع الملف';
            try {
                var body = JSON.parse(text);
                if (body && body.error) {
                    message = body.error;
                    if (window.hrSharedUi && window.hrSharedUi.showConflictAlert) {
                        window.hrSharedUi.showConflictAlert(message, 'تعذّر رفع الملف');
                    }
                }
            } catch (_) { /* plain text */ }
            throw new Error(message);
        }
        return response.json();
    }

    function wirePicker(pickerRoot, options) {
        if (pickerRoot.dataset.hrPickerInit === '1') return;
        pickerRoot.dataset.hrPickerInit = '1';

        options = options || {};
        var fileInput = pickerRoot.querySelector('[data-file-input]');
        var zone = getZone(pickerRoot);
        if (!fileInput) return;

        function openPicker() {
            fileInput.click();
        }

        pickerRoot.querySelectorAll('[data-file-picker-btn]').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openPicker();
            });
        });

        var clearBtn = pickerRoot.querySelector('[data-file-clear-btn]');
        if (clearBtn) {
            clearBtn.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                fileInput.value = '';
                if (options.hiddenInput) options.hiddenInput.value = '';
                if (options.previewEl) options.previewEl.textContent = '';
                showEmptyState(pickerRoot);
                if (options.onClear) options.onClear();
            });
        }

        zone.addEventListener('click', function (e) {
            if (e.target.closest('[data-file-clear-btn]') || e.target.closest('[data-file-picker-btn]')) return;
            if (zone.classList.contains('is-selected')) return;
            openPicker();
        });

        ['dragenter', 'dragover'].forEach(function (evt) {
            zone.addEventListener(evt, function (e) {
                e.preventDefault();
                e.stopPropagation();
                zone.classList.add('is-dragover');
            });
        });

        ['dragleave', 'drop'].forEach(function (evt) {
            zone.addEventListener(evt, function (e) {
                e.preventDefault();
                e.stopPropagation();
                zone.classList.remove('is-dragover');
            });
        });

        zone.addEventListener('drop', function (e) {
            var file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (file) handleFile(file);
        });

        fileInput.addEventListener('change', function () {
            var file = fileInput.files && fileInput.files[0];
            if (file) handleFile(file);
        });

        function handleFile(file) {
            if (options.onFileSelected) {
                options.onFileSelected(file);
            } else {
                showFileState(pickerRoot, file.name, formatSize(file.size));
            }
        }
    }

    function initStandalonePickers() {
        document.querySelectorAll('[data-hr-file-picker]:not([data-file-upload-widget])').forEach(function (picker) {
            if (picker.closest('[data-file-upload-widget]')) return;
            wirePicker(picker);
        });
    }

    function initWidget(root) {
        if (root.dataset.hrUploadInit === '1') return;
        root.dataset.hrUploadInit = '1';

        var fileInput = root.querySelector('[data-file-input]');
        var hiddenInput = root.querySelector('[data-file-id-input]');
        var previewEl = root.querySelector('[data-file-preview]');
        var pickerRoot = root.querySelector('[data-hr-file-picker]');
        var uploadUrl;
        var maxSize = parseInt(root.getAttribute('data-max-size') || '26214400', 10);
        var accept = root.getAttribute('data-accept') || '';

        if (!fileInput || !hiddenInput || !pickerRoot) return;

        try {
            uploadUrl = requireAttribute(root, 'data-upload-url');
            requireAttribute(root, 'data-download-url-template');
        } catch (err) {
            setError(pickerRoot, err.message);
            return;
        }

        fileInput.setAttribute('accept', accept);

        wirePicker(pickerRoot, {
            hiddenInput: hiddenInput,
            previewEl: previewEl,
            onClear: function () {
                hiddenInput.value = '';
            },
            onFileSelected: async function (file) {
                if (file.size > maxSize) {
                    fileInput.value = '';
                    showEmptyState(pickerRoot);
                    setError(pickerRoot, 'حجم الملف أكبر من المسموح.');
                    return;
                }

                showFileState(pickerRoot, file.name, 'جاري الرفع...');
                setUploading(pickerRoot, 'جاري الرفع...');
                setWidgetUploading(root, true);

                try {
                    var result = await uploadFile(file, uploadUrl);
                    hiddenInput.value = result.fileId;
                    showFileState(pickerRoot, result.originalName, formatSize(result.size));
                    if (previewEl && accept.indexOf('image') >= 0) {
                        previewEl.textContent = '';
                        var img = document.createElement('img');
                        img.src = buildDownloadUrl(root, result.fileId);
                        img.alt = '';
                        previewEl.appendChild(img);
                    }
                } catch (err) {
                    hiddenInput.value = '';
                    fileInput.value = '';
                    showEmptyState(pickerRoot);
                    setError(pickerRoot, err.message || 'فشل الرفع');
                } finally {
                    setWidgetUploading(root, false);
                }
            }
        });
    }

    function initAll() {
        document.querySelectorAll('[data-file-upload-widget]').forEach(initWidget);
        initStandalonePickers();
    }

    document.addEventListener('DOMContentLoaded', initAll);

    window.HrFileUpload = { uploadFile: uploadFile, initWidget: initWidget, initAll: initAll };
})();
