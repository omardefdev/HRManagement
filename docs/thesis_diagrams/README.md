# مخططات الأطروحة

هذه الملفات مصادر PlantUML قابلة للتحرير للمخططات المدرجة في الأطروحة. تشغل بواسطة `scripts/generate_thesis_diagrams.py` لإنتاج نسخ PNG داخل `rendered/`. تمثل الملفات 26 إلى 30 الخرائط المفاهيمية الحالية لملكية البيانات والتكامل، وتمثل الملفات 43 إلى 57 الحزمة المرجعية الحالية لحالات الاستخدام وDFD والرحلات الحديثة، أما مخططات ERD المختصرة والكاملة المستخرجة من Migration Snapshots فتولد في حزمة قاعدة البيانات المستقلة، بينما تحفظ حزم Draw.io السابقة خارج هذا المجلد بوصفها أثراً تحليلياً تاريخياً.

## مخططات ERD بصيغة Draw.io (من الشيفرة)

- المصدر القابل للتحرير: `source/database-erdds.drawio` (16 صفحة: فهرس + 15 ERD للجداول وعلاقات FK).
- التوليد: `python scripts/generate_database_erdd_drawio.py` (يقرأ `*DbContext.cs` و`*ModelSnapshot.cs` عبر `scripts/database_schema_parser.py`).
- التحقق: `tmp/database_erdd_drawio_validation.json`.
