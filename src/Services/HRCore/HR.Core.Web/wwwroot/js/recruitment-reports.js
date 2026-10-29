// HCM-032 (PLAN-32): recruitment report charts. Data comes from the JSON block #recruitment-report-data (CSP-safe: no
// inline script, no inline handlers); every chart has an accessible data table next to it.
(function () {
    'use strict';

    if (typeof Chart === 'undefined') return;
    var block = document.getElementById('recruitment-report-data');
    if (!block) return;

    var data;
    try {
        data = JSON.parse(block.textContent || '{}');
    } catch (e) {
        return;
    }

    var funnelCanvas = document.querySelector('canvas[data-chart="funnel"]');
    if (funnelCanvas && Array.isArray(data.funnel) && data.funnel.length) {
        new Chart(funnelCanvas, {
            type: 'bar',
            data: {
                labels: data.funnel.map(function (s) { return s.label; }),
                datasets: [{ label: 'عدد الطلبات', data: data.funnel.map(function (s) { return s.count; }), backgroundColor: '#007bff' }]
            },
            options: {
                indexAxis: 'y',
                responsive: true,
                plugins: { legend: { display: false } },
                scales: { x: { beginAtZero: true, ticks: { precision: 0 } } }
            }
        });
    }

    var channelCanvas = document.querySelector('canvas[data-chart="channels"]');
    if (channelCanvas && Array.isArray(data.channels) && data.channels.length) {
        new Chart(channelCanvas, {
            type: 'bar',
            data: {
                labels: data.channels.map(function (c) { return c.label; }),
                datasets: [
                    { label: 'الطلبات', data: data.channels.map(function (c) { return c.applied; }), backgroundColor: '#17a2b8' },
                    { label: 'المعيّنون', data: data.channels.map(function (c) { return c.hired; }), backgroundColor: '#28a745' }
                ]
            },
            options: { responsive: true, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } }
        });
    }
})();
