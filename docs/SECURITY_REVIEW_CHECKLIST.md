# قائمة مراجعة الأمان — ما قبل الإنتاج

- [ ] تفعيل `Keycloak:DirectorySync:Enabled` مع أسرار في متغيرات البيئة
- [ ] تعطيل Swagger في الإنتاج لجميع الخدمات
- [ ] تفعيل HTTPS وشهادات صالحة على البوابة
- [ ] مراجعة أدوار Keycloak: فصل `hr-employee` عن `hr-core`
- [ ] التحقق من عزل الفروع (`branch-manager` + `scope_org_unit_id`)
- [ ] تفعيل النسخ الاحتياطي المجدول (`scripts/backup-all-databases.ps1`)
- [ ] ضبط `Deployment:LicenseKey` و`Deployment:LicenseExpiresUtc`
- [ ] تدقيق وصول البيانات الصحية والشكاوى (صلاحيات الوحدة)
- [ ] تشغيل `dotnet test` واختبارات التكامل في CI
