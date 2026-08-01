# ورقة اعتماد الإنتاج الوطني Go/No-Go

هذه الورقة لا تُملأ قبل إغلاق البطاقات المنطبقة. كل نتيجة مرتبطة بمرشح إصدار واحد؛ تغيير digest أو config جوهري أو بنية الاعتماد يبطل النتائج المتأثرة.

## هوية المرشح

| الحقل | القيمة |
|---|---|
| Release version | |
| Source commit/tag | |
| Image digests لكل خدمة | |
| SBOM/provenance bundle | |
| Database schema/migration bundle | |
| Configuration/policy/reference-data versions | |
| Keycloak realm/federation version | |
| IaC environment revision | |
| بيئة وتواريخ الاختبار | |
| نطاق الجهات والقدرات | |

## بوابات الأدلة

| المجال | شرط النجاح الأدنى | الدليل | المقيّم/الموقّع | النتيجة |
|---|---|---|---|---|
| P0 | جميع موانع الإطلاق مغلقة | تقارير البطاقات وCI | CISO + QA | |
| عزل الجهات | صفر قراءة/تعديل/بحث ناجح cross-tenant في اختبار مستقل | نتائج TEN/IAM/SEC | Independent Security | |
| الأمن التطبيقي | صفر Critical/High قابل للاستغلال؛ ASVS profile مكتمل | NAT-002/SOV-006 | Independent Assessor | |
| الهوية | assurance/federation/MFA/revocation/rollover ناجحة | EXT-001/IAM-005 | Identity Authority | |
| الخصوصية | ROPA/DPIA/sharing/rights/breach drills معتمدة | PRV-001..003 | DPO + Legal | |
| الاعتماد السوري | جميع `SYD-001..018` مغلقة أو خارج نطاق الموجة بقرار قانوني | SYR-001/007/017 + الملف 14 | Syrian Authorization Board | |
| العملة السورية | كل مبلغ موسوم بالعصر؛ تحويل 100:1 متصالح؛ لا خلط أو تحويل مزدوج | SYR-004 | Finance + Central Bank Liaison | |
| أنظمة الخدمة السورية | كل عامل له نظام قانوني ومصدر نافذ ولا جمع متعارض | SYR-003/005 | Administrative Development + Finance | |
| التأمين والمعاشات | نسب/أسس مصدقة ومصالحة مستقلة مع المؤسستين | SYR-006 | Social Insurance + Pensions | |
| السيادة | residency/egress/support/key custody مطابقة | SOV-002..004 | Sovereignty Authority | |
| الأدلة القانونية | إعادة بناء قرار وتحقق توقيع وسجل طويل الأجل | EVD-001/SIG-001/LEG-001 | Legal + Audit | |
| الرواتب | ≥3 دورات متتالية؛ صفر فرق غير مفسر؛ replay مطابق | NAT-003 | Finance + Legal + Audit | |
| الهجرة | 100% accounted؛ صفر فجوة حرجة؛ rollback مجرب | NAT-004 | Data Owners + Audit | |
| التكامل | كل connector معتمد؛ outage/replay/reconciliation ناجحة | EXT/INT-004 | Integration Authority | |
| التكاملات السورية | الأحوال المدنية والناقل والمالية والمصارف والبريد بعقود ومصالحة | SYR-009..011 | Syrian Source Authorities | |
| الوظائف | UAT لكل capability داخل النطاق؛ لا Sev1/2 | UAT-001 | Agency Business Owners | |
| الوصولية | WCAG 2.2 AA للرحلات الحرجة؛ zero blocker | NAT-005 | Independent Accessibility | |
| تجربة الفروع | low-bandwidth/device/language targets ناجحة | PROD-004/MOB-001 | User Research + Agencies | |
| الحجم | 72h@100%، 4h@150%، 15m@200% مع التدهور الآمن | CAP-001/002 | Independent Performance | |
| الرواتب بالحجم | المغلف العام 5M/6h؛ وللموجة السورية 1.25M/4h وضغط 2M أو قيمة `SYR-002` الأعلى | PERF-001/CAP-001/SYR-017 | Payroll + Performance | |
| التوافر | Production-like SLO ≥30 يوماً دون استثناء خفي | OBS-002/NAT-006 | SRE | |
| HA | node/zone/dependency failover ضمن الأهداف | OPS-001/002 | SRE + DBA | |
| الاستعادة | full+PITR ≤RTO/RPO؛ آخر تمرين ≤90 يوماً | OPS-003/004 | DBA + Audit | |
| Cyber recovery | clean-room rebuild وتدوير الثقة ناجح | OPS-006 | CISO + SRE | |
| التشغيل | 24x7 paging وORR وincident drill ناجحة | SRE-001..003 | Service Owner | |
| الدعم | L1–L3 والموردون والتدريب جاهزون | SUP-002..004/TRN-001 | Service Management | |
| الإصدار | signed artifact، canary، abort، rollback ≤15m | REL-004..006 | Release Authority | |
| المورد والخروج | alternate-operator rebuild/export ناجح | SOV-007/VEN-001 | Procurement + Owner | |
| التمويل | TCO خمس سنوات وتمويل التشغيل معتمدان | FIN-001/002 | Finance | |
| سلسلة التوريد والقيود القانونية | بناء وترقيع ناجحان من مرآة داخلية مقطوعة؛ لا مكون حرج بمصدر واحد بلا بديل | SAN-001 | Procurement + Legal + CISO | |
| البنية المادية | قطع تيار فعلي مقاس؛ مسارا اتصال؛ وقود بمدة معتمدة وعقد أولوية | PHY-001 | Facilities + NAITS + SRE | |
| مكافحة الاحتيال | صفر مستفيد غير مثبت؛ صفر ازدواج غير مبرر؛ تقرير شذوذ مستقل لثلاث دورات | FRD-001 | Internal Audit + Payroll | |
| السيولة والصرف الجزئي | «المصروف + المتأخر = المستحق» لثلاث دورات؛ لا دفع مزدوج عند اللحاق | PAY-004 | Treasury + Finance + Legal | |
| إدارة البرنامج | خط أساس زمني معتمد؛ صفر بطاقة `XL` غير مقسّمة؛ شريحة الموجة موقعة | PGM-001 | PMO + Sponsor | |
| مستودع الأدلة | إعادة إنتاج مستقلة ≥10% من الأدلة؛ استحالة التعديل أو الحذف | EVD-003 | Assurance Office + Audit | |
| النص العربي والمطابقة | corpus أسماء مرجعي ناجح؛ القيود الفريدة والمطابقة على المطبّع | L10N-002 | Data Governance | |
| التمثيل النقابي | دليل إشراك لكل جهة في الموجة أو قرار مختص بعدم اللزوم | TRN-002 | Sponsor + HR Policy | |
| الأنظمة القديمة | القديم read-only؛ استرجاع أرشيف ناجح؛ صفر عملية ظل | DEC-001 | Records + Migration Authority | |
| قابلية الصيانة | خط أساس الدين التقني مفروض في CI وغير متجاوز | QA-005 | Engineering Leads + Architecture | |
| Pilot | شروط NAT-007 ونتائج الموجة مستوفاة | NAT-007 | Pilot Board | |

## اختبارات No-Go الإجبارية

يجب أن تُجرّب حالات الفشل الآتية، ولا يكفي القول إن النظام “مصمم” لتحملها:

- اختراق أو تعطيل IdP جهة واحدة وتدوير مفاتيحه.
- محاولة موظف ومدير ومشغل خدمة الوصول إلى tenant آخر.
- RabbitMQ down أثناء حفظ نقل/فصل/راتب ثم عودته.
- قتل عامل الرواتب في كل checkpoint وإعادة تشغيله مرتين.
- انقطاع PostgreSQL primary وMinIO node وKeycloak node وDNS path.
- امتلاء disk/queue، انتهاء certificate، وتأخر الوقت أو فشل مصدره.
- استعادة backup قبل/بعد تدوير مفتاح، واستعادة clean room بعد افتراض فدية.
- provider خارجي يكرر ويرتب الرسائل عكسياً ويتوقف 24 ساعة.
- انقطاع شبكة فرع أثناء submit ورفع ملف واعتماد.
- migration row/file تالف، identity duplicate، وrollback بعد delta.
- نشر release معيب وmigration جزئية ثم automated abort/rollback.
- إساءة موظف داخلي لصلاحية رواتب أو bulk export ومحاولة حذف أثره.
- خروج المورد وتسليم التشغيل لمشغل بديل.
- حجب سجل الحزم والصور العام، ثم بناء وترقيع أمني كاملين من مرآة داخلية مقطوعة عن الإنترنت.
- قطع التيار الكهربائي عن الموقع فعلياً والاعتماد على UPS ثم المولد والوقود لمدة الاستقلال كاملة.
- فشل التبريد وفقد مزود اتصال، مع محاولة إرسال طلب من فرع بلا كهرباء.
- دورة راتب بتمويل جزئي ثم لحاق لاحق، مع إعادة إرسال ملف الدفع نفسه.
- حقن موظف وهمي ومتقاعد متوفى وIBAN مشترك بين مستفيدين، وقياس نسبة الكشف.
- محاولة مشغّل داخلي رفع حجز صرف أو محو أثر تحقيق احتيال.
- بحث ومطابقة اسم عربي بهجاءات مختلفة عبر البحث والقيد الفريد والتقرير والتصدير.
- محاولة كتابة على النظام القديم بعد تاريخ القطع، وتدقيق ميداني بحثاً عن مسار مواز.
- محاولة تعديل أو حذف دليل في مستودع الأدلة بصلاحية admin التطبيق.

## قاعدة الاستثناء

لا يوجد “قبول شفهي”. أي استثناء يتضمن: الضابط، سبب عدم الإغلاق، الأثر، البيانات والجهات المتأثرة، الضوابط التعويضية، مالك المخاطر المخول قانوناً، تاريخ الانتهاء، trigger للإلغاء، وخطة الإغلاق. لا يقبل استثناء لأي شرط No-Go مطلق في نموذج الجاهزية.

## التوقيعات

| السلطة | الاسم/الصفة | القرار | الشروط/الاستثناءات | التاريخ/التوقيع |
|---|---|---|---|---|
| مالك الخدمة الحكومية | | | | |
| HR/الخدمة المدنية | | | | |
| وزارة التنمية الإدارية/سياسة الموارد البشرية | | | | |
| Payroll/Finance/Treasury | | | | |
| وزارة الشؤون الاجتماعية والعمل | | | | |
| التأمينات الاجتماعية | | | | |
| التأمين والمعاشات | | | | |
| Legal/Records | | | | |
| DPO/Privacy | | | | |
| هيئة حماية البيانات الشخصية | | | | |
| وزارة الاتصالات وNAITS | | | | |
| الأحوال المدنية | | | | |
| مركز التصديق الإلكتروني | | | | |
| الجهاز المركزي للرقابة المالية | | | | |
| الهيئة المركزية للرقابة والتفتيش | | | | |
| CISO/SOC | | | | |
| SRE/Operations | | | | |
| Data Governance/Migration | | | | |
| Internal Audit | | | | |
| المشتريات والتوريد | | | | |
| إدارة المرافق والاستضافة | | | | |
| مكتب إدارة البرنامج | | | | |
| ممثل العاملين/الاتحاد النقابي | | | | |
| ممثل الجهات | | | | |
| Independent Security Assessor | | | | |
| Independent Performance/DR Assessor | | | | |
| Authorizing Official | | | | |

## القرار

- [ ] **No-Go:** شرط إلزامي غير ناجح أو دليل غير صالح.
- [ ] **Pilot/limited continuation only:** نطاق محدود وشروط مكتوبة؛ لا توسع.
- [ ] **National wave authorized:** موجة محددة، release محدد، مدة اعتماد وشروط مراقبة.
- [ ] **Renewal required:** التاريخ أو التغيير الجوهري الذي يطلق إعادة الاعتماد: __________.
