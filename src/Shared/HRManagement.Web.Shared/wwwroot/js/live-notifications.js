(function () {
    'use strict';
    var pathBase = (window.location.pathname.match(/^\/ui\/[^/]+/) || [''])[0];
    var hubUrl = pathBase + '/hubs/notifications';
    if (typeof signalR === 'undefined') return;

    var connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl)
        .withAutomaticReconnect()
        .build();

    connection.on('ReceiveNotification', function (data) {
        showToast(data && data.title, data && data.message, (data && data.type) || 'info');
    });

    connection.start().catch(function () { /* hub may not be available */ });

    function showToast(title, message, type) {
        var container = document.getElementById('hr-notification-container');
        if (!container) return;

        var iconMap = {
            info: 'fas fa-info-circle text-info',
            success: 'fas fa-check-circle text-success',
            warning: 'fas fa-exclamation-triangle text-warning',
            error: 'fas fa-times-circle text-danger'
        };
        var iconClass = iconMap[type] || iconMap.info;

        var toast = document.createElement('div');
        toast.className = 'toast show shadow-lg';
        toast.setAttribute('role', 'alert');

        var header = document.createElement('div');
        header.className = 'toast-header';

        var icon = document.createElement('i');
        icon.className = iconClass + ' me-2';
        header.appendChild(icon);

        var strong = document.createElement('strong');
        strong.className = 'me-auto';
        strong.textContent = title == null ? '' : String(title);
        header.appendChild(strong);

        var small = document.createElement('small');
        small.textContent = new Date().toLocaleTimeString('ar');
        header.appendChild(small);

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'btn-close';
        closeBtn.setAttribute('data-bs-dismiss', 'toast');
        closeBtn.setAttribute('aria-label', 'إغلاق');
        header.appendChild(closeBtn);

        var body = document.createElement('div');
        body.className = 'toast-body';
        body.textContent = message == null ? '' : String(message);

        toast.appendChild(header);
        toast.appendChild(body);
        container.prepend(toast);
        setTimeout(function () { toast.classList.remove('show'); toast.remove(); }, 8000);
    }
})();
