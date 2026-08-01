-- MDM-001: sample Syrian national reference data seed
-- Uses a placeholder TenantId; replace with the actual tenant GUID for each deployment.

DO $$
DECLARE
    _tenant uuid := '00000000-0000-0000-0000-000000000001';
    _now    timestamptz := now();
BEGIN

-- Grades (الدرجات الوظيفية)
INSERT INTO "ReferenceCatalogEntries" ("Id","TenantId","CatalogKind","Code","NameAr","NameEn","IsActive","Version","CreatedAt","UpdatedAt")
VALUES
  (gen_random_uuid(), _tenant, 'Grade', 'GR-01', 'الدرجة الأولى',        'Grade 1',  true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Grade', 'GR-02', 'الدرجة الثانية',       'Grade 2',  true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Grade', 'GR-03', 'الدرجة الثالثة',       'Grade 3',  true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Grade', 'GR-04', 'الدرجة الرابعة',       'Grade 4',  true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Grade', 'GR-05', 'الدرجة الخامسة',       'Grade 5',  true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Grade', 'GR-EX', 'الدرجة الاستثنائية',   'Exceptional Grade', true, 1, _now, _now)
ON CONFLICT DO NOTHING;

-- Job Classifications (التصنيفات الوظيفية)
INSERT INTO "ReferenceCatalogEntries" ("Id","TenantId","CatalogKind","Code","NameAr","NameEn","IsActive","Version","CreatedAt","UpdatedAt")
VALUES
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-ADM', 'إداري',         'Administrative', true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-TEC', 'فني',           'Technical',      true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-ENG', 'هندسي',         'Engineering',    true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-MED', 'طبي',           'Medical',        true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-EDU', 'تعليمي',        'Educational',    true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-LEG', 'قانوني',        'Legal',          true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'JobClassification', 'JC-FIN', 'مالي',          'Financial',      true, 1, _now, _now)
ON CONFLICT DO NOTHING;

-- Geography (المحافظات السورية)
INSERT INTO "ReferenceCatalogEntries" ("Id","TenantId","CatalogKind","Code","NameAr","NameEn","IsActive","Version","CreatedAt","UpdatedAt")
VALUES
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-DMS', 'دمشق',           'Damascus',       true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-RDM', 'ريف دمشق',       'Rural Damascus', true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-ALP', 'حلب',            'Aleppo',         true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-HMS', 'حمص',            'Homs',           true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-HMA', 'حماة',           'Hama',           true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-LTK', 'اللاذقية',       'Latakia',        true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-TRT', 'طرطوس',          'Tartus',         true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-IDL', 'إدلب',           'Idlib',          true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-DRZ', 'درعا',           'Daraa',          true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-SWD', 'السويداء',       'As-Suwayda',     true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-QNT', 'القنيطرة',       'Quneitra',       true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-DEZ', 'دير الزور',      'Deir ez-Zor',    true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-RAQ', 'الرقة',          'Raqqa',          true, 1, _now, _now),
  (gen_random_uuid(), _tenant, 'Geography', 'GEO-HSK', 'الحسكة',         'Al-Hasakah',     true, 1, _now, _now)
ON CONFLICT DO NOTHING;

END $$;
