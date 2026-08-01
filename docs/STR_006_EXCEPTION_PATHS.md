# مسارات الاستثناء — STR-006

| مسار | السلوك |
|---|---|
| مكوّن حرج busFactor &lt; 2 أو بلا backup | فشل validate |
| ميزانية dev فقط (بلا ops/security/backup) | فشل validate |
| مورد بلا بند Exit أو RTO أو IncidentDisclosure | فشل validate |
| ادعاء national24x7Claim = true | فشل validate — SRE-001 |
| ادعاء إغلاق FIN-001 / SRE-001 / drill خروج حي داخل الحزمة | مرفوض — soft blockers (SOV-007 binding Done ≠ VEN-001) |
| استخدام SuccessionPlan/Vendor في Domain كبديل للحزمة | مرفوض — خارج النطاق |
| حذف STR-005 من blockers أو جعله غير Satisfied | فشل validate |

راجع [ADR-STR-006](ADR-STR-006-permanent-ownership-funding-skills-vendors.md).
