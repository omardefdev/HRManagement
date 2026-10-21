(function ($) {
    'use strict';

    if (!$.validator) {
        return;
    }

    $.extend($.validator.messages, {
        required: 'هذا الحقل مطلوب.',
        remote: 'يرجى تصحيح هذا الحقل.',
        email: 'يرجى إدخال بريد إلكتروني صالح.',
        url: 'يرجى إدخال رابط صالح.',
        date: 'يرجى إدخال تاريخ صالح.',
        dateISO: 'يرجى إدخال تاريخ صالح (ISO).',
        number: 'يرجى إدخال رقم صالح.',
        digits: 'يرجى إدخال أرقام فقط.',
        creditcard: 'يرجى إدخال رقم بطاقة صالح.',
        equalTo: 'يرجى إدخال نفس القيمة مرة أخرى.',
        maxlength: $.validator.format('يرجى عدم إدخال أكثر من {0} حرفاً.'),
        minlength: $.validator.format('يرجى إدخال {0} أحرف على الأقل.'),
        rangelength: $.validator.format('يرجى إدخال قيمة بين {0} و {1} حرفاً.'),
        range: $.validator.format('يرجى إدخال قيمة بين {0} و {1}.'),
        max: $.validator.format('يرجى إدخال قيمة أقل من أو تساوي {0}.'),
        min: $.validator.format('يرجى إدخال قيمة أكبر من أو تساوي {0}.'),
        step: $.validator.format('يرجى إدخال مضاعف {0}.')
    });
})(jQuery);
