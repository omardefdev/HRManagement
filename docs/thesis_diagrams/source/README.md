# مصدر مخططات DFD

- `dfd-hr-system_original.drawio`: النسخة الأصلية التي قدمها الفريق.
- `dfd-hr-system.drawio`: نسخة Draw.io قابلة للتحرير، صُحح فيها الترميز المزدوج لقيم الخلايا العربية فقط.
- `../drawio_rendered/`: صور الصفحات العشر المدرجة في الأطروحة.
- `usecase-diagrams_original.drawio`: النسخة الأصلية لحزمة حالات الاستخدام ذات الصفحات الإحدى والعشرين.
- `usecase-diagrams.drawio`: نسخة قابلة للتحرير أُعدت للتصيير والإدراج في الأطروحة.
- `../usecase_drawio_rendered/`: الصور المعتمدة لمخططات حالات الاستخدام القطاعية.

تتضمن الحزمة مخطط السياق، ومخطط المستوى الأول، وسبعة مخططات للمستوى الثاني، وقاموس البيانات.

## مخططات ERD لقواعد البيانات

- `database-erdds.drawio`: 16 صفحة (00 فهرس + 01–15 جداول وعلاقات FK؛ اسم إنجليزي مع ترجمة عربية تحته).
- `erd-sample-one-to-many.drawio`: عينة أسلوب Crow's Foot (Asset → AssetAssignment).
- إعادة التوليد: من جذر المستودع `python scripts/generate_database_erdd_drawio.py`.
- المحلّل المشترك: `scripts/database_schema_parser.py` (يُستخدم أيضاً مع `scripts/generate_complete_database_erdds.py`).
