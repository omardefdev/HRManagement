#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Safely add [Display(Name = "...")] to enum members missing the attribute."""

import re
import sys
from pathlib import Path

# --- translation tables (same as before, abbreviated in file for maintainability) ---
TRANSLATIONS = {
    "Active": "نشط", "Suspended": "موقوف", "Terminated": "منتهي", "Retired": "متقاعد",
    "OnLeave": "في إجازة", "Seconded": "معار", "Resigned": "مستقيل", "Deceased": "متوفى",
    "Merged": "مدمج", "Anonymized": "مجهول الهوية",
    "Male": "ذكر", "Female": "أنثى", "Other": "أخرى",
    "Single": "أعزب", "Married": "متزوج", "Divorced": "مطلق", "Widowed": "أرمل",
    "Permanent": "دائم", "Administrative": "إداري", "Temporary": "مؤقت",
    "Phone": "هاتف", "Email": "بريد إلكتروني", "Address": "عنوان",
    "Certificate": "شهادة", "Photo": "صورة", "Fingerprint": "بصمة",
    "NationalId": "هوية وطنية", "Passport": "جواز سفر",
    "Pending": "قيد الانتظار", "Approved": "معتمد", "Rejected": "مرفوض",
    "Cancelled": "ملغى", "Executed": "منفّذ",
    "Draft": "مسودة", "Calculated": "محسوب", "Posted": "مرحّل",
    "UnderReview": "قيد المراجعة", "Reversed": "معكوس",
    "Calculating": "جاري الحساب", "Validating": "جاري التحقق", "Processed": "معالج",
    "Running": "قيد التنفيذ", "Completed": "مكتمل", "Failed": "فشل", "Paused": "متوقف مؤقتاً",
    "Snapshot": "لقطة", "Calculate": "حساب", "Validate": "تحقق", "Post": "ترحيل", "Pay": "صرف",
    "Submitted": "مُرسَل", "Acked": "مُستَلَم", "Settled": "مسدّد", "Returned": "مُرتَجَع",
    "Disbursement": "صرف", "Reversal": "عكس",
    "InReview": "قيد المراجعة", "Calibrating": "معايرة", "Closed": "مغلق",
    "Planning": "تخطيط", "Review": "مراجعة", "Calibration": "معايرة",
    "Self": "ذاتي", "Manager": "مدير", "Matrix": "مصفوفة", "Peer": "زميل", "Committee": "لجنة",
    "NotStarted": "لم يبدأ", "InProgress": "قيد التنفيذ", "Acknowledged": "مُقرّ", "Appealed": "مستأنف",
    "Open": "مفتوح", "Investigating": "قيد التحقيق", "Resolved": "محلول", "Ongoing": "جاري",
    "Mitigated": "مُخفَّف", "Low": "منخفض", "Medium": "متوسط", "High": "عالي", "Critical": "حرج",
    "Warning": "إنذار", "Dismissal": "فصل", "None": "لا يوجد", "Decided": "مُبتَ فيه",
    "Ok": "ناجح", "Weekly": "أسبوعي", "Monthly": "شهري", "AdHoc": "استثنائي",
    "Enrolled": "مسجّل", "Attended": "حاضر", "Proposed": "مقترح",
    "Preventive": "وقائي", "Corrective": "تصحيحي",
    "InUse": "قيد الاستخدام", "InStorage": "في المخزن", "Maintenance": "صيانة",
    "Disposed": "مُستَبعَد", "Lost": "مفقود", "New": "جديد", "Replacement": "بديل", "Update": "تحديث",
    "Revoked": "ملغى", "Expired": "منتهي الصلاحية", "OnLoan": "معار", "Overdue": "متأخر",
    "Physical": "ورقي", "Digital": "رقمي",
    "Laptop": "حاسوب محمول", "Desktop": "حاسوب مكتبي", "Mobile": "هاتف",
    "InRepair": "قيد الإصلاح", "Decommissioned": "مُخرَج من الخدمة", "Assigned": "مُسند",
    "Fulfilled": "مُنفَّذ", "Daily": "يومي", "Seasonal": "موسمي", "PartTime": "دوام جزئي",
    "Production": "إنتاج", "Contractual": "تعاقدي",
    "Law50": "قانون 50", "Law53": "قانون 53", "Special": "خاص",
    "InApp": "داخل التطبيق", "Sms": "رسالة نصية", "Push": "إشعار فوري",
    "Delivered": "مُسلَّم", "Read": "مقروء",
    "Day": "نهارية", "Evening": "مسائية", "Night": "ليلية",
    "Annual": "سنوية", "Sick": "مرضية", "Unpaid": "بدون أجر", "Maternity": "أمومة",
    "Passed": "ناجح", "Extended": "ممدد",
    "Resignation": "استقالة", "EndOfContract": "انتهاء عقد", "Medical": "طبية",
    "Judicial": "قضائية", "Disciplinary": "تأديبية", "Termination": "إنهاء",
    "Appointment": "تعيين", "Confirmation": "تثبيت", "LeaveAdvance": "سلفة إجازة",
    "JobUpdate": "تحديث وظيفة", "Separation": "إنهاء خدمة",
    "NeedsInfo": "بحاجة لمعلومات", "InProcess": "قيد المعالجة",
    "PendingApproval": "بانتظار الاعتماد", "Executing": "قيد التنفيذ",
    "PostProcessing": "معالجة لاحقة", "Archived": "مؤرشف",
    "CandidateReceived": "استلام المرشح", "ContractClassified": "تصنيف العقد",
    "PersonalFileOpened": "فتح الملف الشخصي", "PayrollLinked": "ربط الرواتب",
    "CareerRecordApproved": "اعتماد السجل الوظيفي", "IdentityIssued": "إصدار الهوية",
    "HireArchived": "أرشفة التعيين",
    "ContractTerminated": "إنهاء العقد", "PayrollSettled": "تسوية الراتب",
    "CardRevoked": "إلغاء البطاقة", "AccountDisabled": "تعطيل الحساب",
    "AssetsReturned": "إرجاع العهدة",
    "Present": "حاضر", "Absent": "غائب", "Late": "متأخر", "EarlyLeave": "مغادرة مبكرة",
    "Holiday": "عطلة", "Weekend": "عطلة أسبوعية", "OnDuty": "في الخدمة", "OffDuty": "خارج الخدمة",
    "Skipped": "متخطى", "Waiting": "في الانتظار",
    "Queued": "في الطابور", "Scanning": "جاري الفحص", "Clean": "سليم",
    "Infected": "مصاب", "Quarantined": "في الحجر", "Purged": "محذوف",
    "Public": "عام", "Internal": "داخلي", "Restricted": "مقيد", "Confidential": "سري",
    "Regular": "اعتيادي", "Supplementary": "تكميلي", "Bonus": "مكافأة", "Arrears": "متأخرات",
    "EndOfService": "نهاية خدمة", "Basic": "أساسي", "Allowance": "علاوة",
    "Deduction": "اقتطاع", "Tax": "ضريبة", "SocialSecurity": "تأمينات", "Pension": "تقاعد",
    "Overtime": "إضافي", "RoundUp": "تقريب لأعلى", "RoundDown": "تقريب لأسفل",
    "RoundNearest": "تقريب للأقرب", "Truncate": "اقتطاع",
    "CalendarDays": "أيام تقويمية", "WorkingDays": "أيام عمل",
    "Published": "منشور", "Unpublished": "غير منشور", "Scheduled": "مجدول", "Bounced": "مرتد",
    "Transfer": "نقل", "Promotion": "ترقية", "Demotion": "تنزيل", "Distribution": "توزيع",
    "Fit": "لائق", "Unfit": "غير لائق", "PartiallyFit": "لائق جزئياً",
    "Referral": "إحالة", "Rest": "راحة", "LightDuty": "عمل خفيف",
    "Printed": "مطبوعة", "Issued": "صادرة",
    "Requested": "مطلوب", "Provisioned": "مُزوَّد", "Deprovisioned": "مُلغى التزويد",
    "Grade": "درجة", "Rank": "رتبة", "JobTitle": "مسمى وظيفي", "OrgUnit": "وحدة تنظيمية",
    "Location": "موقع",
    "Access": "وصول", "Rectification": "تصحيح", "Erasure": "محو",
    "Portability": "نقل", "Restriction": "تقييد", "Objection": "اعتراض",
    "Received": "مستلم", "Denied": "مرفوض", "Paid": "مدفوع",
    "Create": "إنشاء", "Modify": "تعديل", "Delete": "حذف",
    "Inquiry": "استفسار", "Request": "طلب", "Complaint": "شكوى", "Grievance": "تظلم",
    "Info": "معلومات",
    "Classroom": "حضوري", "Online": "إلكتروني", "Blended": "مختلط",
    "Workshop": "ورشة", "Seminar": "ندوة", "Conference": "مؤتمر", "Withdrawn": "منسحب",
    "Harassment": "تحرش", "Misconduct": "سوء سلوك", "Conflict": "نزاع",
    "Health": "صحي", "Life": "حياة", "Disability": "إعاقة",
    "Verified": "مُتحقَّق", "Duplicate": "مكرر", "Superseded": "مُستبدَل",
    "Valid": "ساري", "Invalid": "غير ساري",
    "Released": "مُرفَع",
    "Pre2011": "ما قبل 2011", "Post2011": "ما بعد 2011",
    "Task": "مهمة", "Approval": "اعتماد", "Notification": "إشعار",
    "Urgent": "عاجل", "Normal": "عادي", "Snoozed": "مؤجل", "Done": "منجز",
    "Admitted": "مُقبَل", "Throttled": "مُقيَّد",
    "Live": "مباشر", "Shadow": "ظل", "Replay": "إعادة تشغيل",
    "Direct": "مباشر", "Inherited": "موروث", "Explicit": "صريح",
    "ReadyNow": "جاهز الآن", "ReadyOneToTwoYears": "جاهز خلال 1-2 سنة",
    "ReadyThreePlusYears": "جاهز خلال 3+ سنوات", "NotReady": "غير جاهز",
    "Applied": "مقدَّم", "Screening": "فرز", "Shortlisted": "في القائمة المختصرة",
    "Interview": "مقابلة", "Offered": "عرض", "Hired": "معيّن",
    "Incoming": "وارد", "Outgoing": "صادر",
    "Fixed": "ثابت", "Rotating": "دوّار", "Flexible": "مرن",
    "Detected": "مُكتشَف", "Dismissed": "مرفوض",
    "Initiated": "مُبادر", "Signed": "موقّع", "Notified": "مُبلَّغ",
    "Created": "منشأ", "Sent": "مُرسَل", "Confirmed": "مؤكد",
    "Minor": "بسيط", "Moderate": "متوسط", "Severe": "شديد", "Fatal": "وفاة",
    "Vacant": "شاغر", "Occupied": "مشغول", "Frozen": "مجمد", "Abolished": "ملغى",
    "Assignment": "تكليف", "Secondment": "إعارة",
    "Indexed": "مفهرس", "Stored": "مخزّن",
    "Planned": "مخطط", "Evaluated": "مُقيَّم", "Certified": "مُصدَّق",
    "Opened": "مفتوح", "EvidenceCollected": "جمع أدلة",
    "HearingHeld": "جلسة استماع", "DecisionIssued": "صدر القرار",
    "ReturnToWork": "عودة للعمل", "LimitedDuties": "مهام محدودة",
    "FitnessForDuty": "لياقة للعمل", "Compensation": "تعويض",
    "Replaced": "مُستبدَلة", "CreateNew": "إنشاء جديد", "LinkExisting": "ربط موجود",
    "BaseSalary": "راتب أساسي", "Adjustment": "تسوية",
    "Disabled": "معطّل", "NotScoped": "غير محدد النطاق", "InvalidClaim": "مطالبة غير صالحة",
    "Make": "إجراء", "Check": "تحقق", "Reverse": "عكس",
    "Escalation": "تصعيد", "Reminder": "تذكير",
    "MedicalPlan": "طبية", "Dental": "أسنان", "Vision": "بصر",
    "External": "خارجي", "SelfPaced": "ذاتي", "Coaching": "تدريب", "Certification": "شهادة",
    "OnHold": "معلّق", "Filled": "مُشغَل",
    "Excused": "مبرر", "Abstain": "امتناع",
    "Processing": "قيد المعالجة",
    "Activate": "تفعيل", "Deactivate": "تعطيل", "Rename": "إعادة تسمية",
    "Move": "نقل", "Merge": "دمج", "Split": "تقسيم", "Close": "إغلاق",
    "Discrimination": "تمييز", "Bullying": "تنمر",
    "Feedback": "ملاحظات", "Suggestion": "اقتراح",
    "Escalated": "مُصعَّد", "Outstanding": "قائم",
    "HalfDay": "نصف يوم", "Remote": "عن بُعد",
    "Compliance": "امتثال", "Domain": "مجال", "Soft": "مهارات شخصية",
    "Leadership": "قيادي", "Technical": "تقني", "Language": "لغوي",
    "Start": "بداية", "End": "نهاية", "SubWorkflow": "سير فرعي",
    "Condition": "شرط", "Parallel": "متوازي", "Join": "دمج", "Timer": "مؤقت", "Script": "سكربت",
    "Private": "خاص", "Admit": "قبول", "Defer": "تأجيل", "Reject": "رفض",
    "Approve": "موافقة", "RequestInfo": "طلب معلومات", "Delegate": "تفويض",
    "Execute": "تنفيذ", "Export": "تصدير", "Import": "استيراد", "Assign": "إسناد",
    "WrittenOff": "شُطب", "PartiallyPaid": "مدفوع جزئياً",
    "Accepted": "مقبول", "Declined": "مرفوض",
    "Dual": "مزدوج", "RotatingShift": "دوّار", "Split": "مقسّمة",
    "Pdf": "PDF", "Docx": "Word", "Html": "HTML", "RetiredTemplate": "مُخرَج",
    "Shift": "ورديات", "Inactive": "غير نشط",
    "Suspension": "إيقاف", "Rectify": "تصحيح", "Deletion": "محو", "Correction": "تصحيح",
    "Appeal": "استئناف", "Decision": "قرار",
    "Inbound": "وارد", "Outbound": "صادر",
    "FullTime": "دوام كامل", "Loan": "إعارة",
    "RequestRecorded": "تسجيل الطلب", "FinancialReviewed": "مراجعة مالية",
    "OrgSynced": "مزامنة تنظيمية", "DirectoryUpdated": "تحديث الدليل",
    "AssetsNoted": "تسجيل العهدة", "Ended": "منتهي",
    "CertificateIssued": "إصدار الشهادة", "Designed": "تصميم",
    "Succeeded": "ناجح", "FullyPaid": "مدفوع بالكامل",
    "PartiallySettled": "مسدّد جزئياً", "AwayFromZero": "بعيداً عن الصفر",
    "ToEven": "تقريب زوجي", "FinalSettlement": "تسوية نهائية",
    "Generated": "مُولَّد", "Viewed": "مُشاهَد", "Calendar30": "30 يوماً تقويمياً",
    "Classified": "مصنّف", "Ingested": "مُدخَل", "Held": "محجوز",
    "DisposeRequested": "طلب إتلاف", "DisposeApproved": "إتلاف معتمد",
    "File": "ملف", "Stub": "وهمي", "Sandbox": "بيئة تجريبية",
    "Acknowledgement": "إقرار", "Information": "معلومات",
    "Delegated": "مُفوَّض", "SocialInsurance": "تأمينات اجتماعية",
    "Discipline": "تأديب", "Investigation": "تحقيق", "Whistleblower": "إبلاغ",
    "WorkplaceIncident": "حادث عمل", "UnderInvestigation": "قيد التحقيق",
    "PendingHearing": "بانتظار جلسة", "ActionTaken": "إجراء متخذ",
    "AppealFiled": "استئناف مقدّم",
    "OldLira": "ليرة قديمة", "NewLira": "ليرة جديدة",
    "PdfA": "PDF/A", "Deprecated": "مهمل",
    "ProfileCorrection": "تصحيح ملف", "LeaveRequest": "طلب إجازة",
    "AdvanceRequest": "طلب سلفة", "DocumentRequest": "طلب مستند",
    "PayslipQuery": "استفسار قسيمة", "BenefitEnquiry": "استفسار مزايا",
    "LearningEnrollment": "التحاق تعليمي", "CardReplacement": "بدل بطاقة",
    "GeneralInquiry": "استفسار عام",
    "Module": "وحدة", "Tenant": "جهة",
    "PendingDisposition": "بانتظار التصرف", "UnderLegalHold": "تحت حجز قانوني",
    "JobClassification": "تصنيف وظيفي", "Geography": "جغرافيا",
    "Agency": "جهة", "Qualification": "مؤهل", "Bank": "مصرف",
    "SalaryComponent": "مكوّن راتب", "ContractType": "نوع عقد",
    "MovementType": "نوع حركة", "ServiceType": "نوع معاملة",
    "PartiallySettledArrears": "مسدّد جزئياً",
    "IdentityVerification": "التحقق من الهوية", "PendingReview": "بانتظار المراجعة",
    "PartiallyFulfilled": "منفّذ جزئياً", "AppealOpen": "استئناف مفتوح",
    "UnderScreening": "قيد الفرز", "InterviewScheduled": "مقابلة مجدولة",
    "Assessed": "مُقيَّم", "Ranked": "مُصنَّف", "OfferExtended": "عرض مُقدَّم",
    "Training": "تدريب", "ELearning": "تعلّم إلكتروني",
    "OnTheJob": "أثناء العمل", "Mentoring": "إرشاد",
    "Nominated": "مرشّح", "Managerial": "إداري",
    "Behavioral": "سلوكي", "LegalSkill": "قانوني", "Specialized": "اختصاصي",
    "Leave": "إجازة", "Mission": "مهمة", "Continuous": "مستمر",
    "Legal": "قانوني",
}

CONTEXT = {
    "ContractStatus.Expired": "منتهي",
    "ApprovalStatus.Pending": "معلق",
    "PayrollRunStatus.Pending": "مسودة",
    "PayrollRunStatus.Processed": "محسوب",
    "PaymentBatchStatus.Processed": "مسدّد",
    "SignatureType.Digital": "توقيع رقمي",
    "SignatureType.Physical": "توقيع يدوي",
    "ProbationResult.Failed": "راسب",
    "AppealStatus.None": "لا يوجد",
    "AppealStatus.Pending": "معلق",
    "EmployeeCadre.Supervisory": "كادر إشرافي",
    "EmployeeCadre.Executive": "موظف تنفيذي",
    "ShiftType.Day": "نهارية",
    "ShiftType.Evening": "مسائية",
    "ShiftType.Night": "ليلية",
    "JobStatus.Ok": "ناجح",
    "BackupStatus.Ok": "ناجح",
    "CardStatus.Active": "نشطة",
    "InsuranceCardStatus.Active": "نشطة",
    "ServerStatus.Active": "نشط",
    "FundedPositionStatus.Active": "نشط",
    "GoldenPersonStatus.Active": "نشط",
    "PensionEnrollmentStatus.Active": "نشط",
    "LegalHoldStatus.Active": "نشط",
    "FileRecordStatus.Active": "نشط",
    "CardRequestStatus.Processed": "معالج",
    "ActionType.Other": "أخرى",
    "SalaryComponentType.Other": "أخرى",
    "LeaveType.Other": "أخرى",
    "DocumentType.Other": "أخرى",
    "Gender.Other": "أخرى",
    "ServiceCaseType.Other": "أخرى",
    "RelationCaseType.Other": "أخرى",
    "SkillCategory.Other": "أخرى",
    "SodAction.Approve": "اعتماد",
    "SodAction.Post": "ترحيل",
    "SodAction.Pay": "صرف",
    "RegimeType.Special": "نظام خاص",
    "WorkItemPriority.Low": "منخفضة",
    "WorkItemPriority.Normal": "عادية",
    "WorkItemPriority.High": "عالية",
    "WorkItemPriority.Urgent": "عاجلة",
    "MedicalCommitteeRecommendation.Retirement": "تقاعد",
    "MedicalRecommendationType.Retirement": "تقاعد",
    "MedicalRecommendationType.LimitedDuties": "مهام محدودة",
    "RecruitmentHireMode.CreateNew": "إنشاء جديد",
    "RecruitmentHireMode.LinkExisting": "ربط موجود",
    "PayrollRoundingMode.RoundNearest": "تقريب للأقرب",
    "TemplateStatus.Retired": "مُخرَج",
    "BenefitPlanType.Health": "صحية",
    "BenefitPlanType.Medical": "طبية",
    "AdmissionDecision.Admit": "قبول",
    "AdmissionDecision.Reject": "رفض",
    "AdmissionDecision.Defer": "تأجيل",
    "AdmissionDecision.Throttle": "تقييد",
    "OrgScopeClaimKind.NotScoped": "غير محدد النطاق",
    "OrgScopeClaimKind.InvalidClaim": "مطالبة غير صالحة",
    "IntegrationConnectorMode.Disabled": "معطّل",
    "DsarRequestStatus.InProgress": "قيد المعالجة",
    "DsarRequestStatus.Fulfilled": "مُنفَّذ",
    "LearningEnrollmentStatus.Failed": "راسب",
    "CorrespondenceDirection.Internal": "داخلي",
    "ArchiveFormat.Physical": "ورقي",
    "ArchiveFormat.Digital": "رقمي",
    "MaintenanceType.Preventive": "وقائي",
    "MaintenanceType.Corrective": "تصحيحي",
    "CardRequestPurpose.New": "جديد",
    "CardRequestPurpose.Replacement": "بديل",
    "CardRequestPurpose.Update": "تحديث",
    "LegalRegime.EmploymentType.Permanent": "دائم",
    "LegalRegime.EmploymentType.Temporary": "مؤقت",
    "LegalRegime.EmploymentType.Daily": "يومي",
    "LegalRegime.EmploymentType.Seasonal": "موسمي",
    "LegalRegime.EmploymentType.PartTime": "دوام جزئي",
    "LegalRegime.EmploymentType.Production": "إنتاج",
    "LegalRegime.EmploymentType.Contractual": "تعاقدي",
    "SkillCategory.Legal": "قانوني",
    "SkillCategory.Managerial": "إداري",
    "SkillCategory.Behavioral": "سلوكي",
    "SkillCategory.Specialized": "اختصاصي",
    "ActionType.Suspension": "إيقاف",
    "DsarRequestType.Rectify": "تصحيح",
    "DsarRequestType.Deletion": "محو",
    "DsarRequestType.Correction": "تصحيح",
    "DsarRequestType.Appeal": "استئناف",
    "CorrespondenceDirection.Inbound": "وارد",
    "CorrespondenceDirection.Outbound": "صادر",
    "WorkScheduleMode.FullTime": "دوام كامل",
    "MovementKind.Loan": "إعارة",
    "ProvisioningStatus.Succeeded": "ناجح",
    "ArrearsLedgerStatus.FullyPaid": "مدفوع بالكامل",
    "PaymentOrderStatus.PartiallySettled": "مسدّد جزئياً",
    "PayrollRoundingMode.AwayFromZero": "بعيداً عن الصفر",
    "PayrollRoundingMode.ToEven": "تقريب زوجي",
    "PayrollRunType.Correction": "تصحيح",
    "PayrollRunType.FinalSettlement": "تسوية نهائية",
    "PayslipDeliveryStatus.Generated": "مُولَّد",
    "PayslipDeliveryStatus.Viewed": "مُشاهَد",
    "ProrationDaysBasis.Calendar30": "30 يوماً تقويمياً",
    "IntegrationConnectorMode.File": "ملف",
    "IntegrationConnectorMode.Stub": "وهمي",
    "IntegrationConnectorMode.Sandbox": "بيئة تجريبية",
    "WorkItemType.Acknowledgement": "إقرار",
    "WorkItemType.Information": "معلومات",
    "WorkItemStatus.Delegated": "مُفوَّض",
    "BenefitPlanType.SocialInsurance": "تأمينات اجتماعية",
    "CurrencyEra.OldLira": "ليرة قديمة",
    "CurrencyEra.NewLira": "ليرة جديدة",
    "OfficialDocumentFormat.PdfA": "PDF/A",
    "TemplateStatus.Deprecated": "مهمل",
    "FileVisibility.Module": "وحدة",
    "FileVisibility.Tenant": "جهة",
    "ArrearsStatus.PartiallySettled": "مسدّد جزئياً",
    "AttendanceStatus.Leave": "إجازة",
    "AttendanceStatus.Mission": "مهمة",
    "ShiftType.Continuous": "مستمر",
    "LearningType.Training": "تدريب",
    "LearningType.ELearning": "تعلّم إلكتروني",
    "LearningType.OnTheJob": "أثناء العمل",
    "LearningType.Mentoring": "إرشاد",
    "EnrollmentStatus.Nominated": "مرشّح",
    "CatalogKind.JobClassification": "تصنيف وظيفي",
    "CatalogKind.Geography": "جغرافيا",
    "CatalogKind.Agency": "جهة",
    "CatalogKind.Qualification": "مؤهل",
    "CatalogKind.Bank": "مصرف",
    "CatalogKind.Currency": "عملة",
    "CatalogKind.SalaryComponent": "مكوّن راتب",
    "CatalogKind.DocumentType": "نوع مستند",
    "CatalogKind.ContractType": "نوع عقد",
    "CatalogKind.MovementType": "نوع حركة",
    "CatalogKind.ServiceType": "نوع معاملة",
    "InvestigationChecklistStep.Decision": "قرار",
    "MovementChecklistStep.RequestRecorded": "تسجيل الطلب",
    "MovementChecklistStep.FinancialReviewed": "مراجعة مالية",
    "MovementChecklistStep.OrgSynced": "مزامنة تنظيمية",
    "MovementChecklistStep.DirectoryUpdated": "تحديث الدليل",
    "MovementChecklistStep.AssetsNoted": "تسجيل العهدة",
    "MovementChecklistStep.Ended": "منتهي",
    "TrainingChecklistStep.CertificateIssued": "إصدار الشهادة",
    "CardChecklistStep.Designed": "تصميم",
    "ArchiveChecklistStep.Classified": "مصنّف",
    "ArchiveChecklistStep.Ingested": "مُدخَل",
    "ArchiveChecklistStep.Held": "محجوز",
    "ArchiveChecklistStep.DisposeRequested": "طلب إتلاف",
    "ArchiveChecklistStep.DisposeApproved": "إتلاف معتمد",
    "RelationCaseType.Discipline": "تأديب",
    "RelationCaseType.Investigation": "تحقيق",
    "RelationCaseType.Whistleblower": "إبلاغ",
    "RelationCaseType.WorkplaceIncident": "حادث عمل",
    "RelationCaseType.Appeal": "استئناف",
    "RelationCaseStatus.UnderInvestigation": "قيد التحقيق",
    "RelationCaseStatus.PendingHearing": "بانتظار جلسة",
    "RelationCaseStatus.ActionTaken": "إجراء متخذ",
    "RelationCaseStatus.AppealFiled": "استئناف مقدّم",
    "OfficialRecordStatus.PendingDisposition": "بانتظار التصرف",
    "OfficialRecordStatus.UnderLegalHold": "تحت حجز قانوني",
    "ServiceCaseType.ProfileCorrection": "تصحيح ملف",
    "ServiceCaseType.LeaveRequest": "طلب إجازة",
    "ServiceCaseType.AdvanceRequest": "طلب سلفة",
    "ServiceCaseType.DocumentRequest": "طلب مستند",
    "ServiceCaseType.PayslipQuery": "استفسار قسيمة",
    "ServiceCaseType.BenefitEnquiry": "استفسار مزايا",
    "ServiceCaseType.LearningEnrollment": "التحاق تعليمي",
    "ServiceCaseType.CardReplacement": "بدل بطاقة",
    "ServiceCaseType.GeneralInquiry": "استفسار عام",
    "DsarRequestStatus.IdentityVerification": "التحقق من الهوية",
    "DsarRequestStatus.PendingReview": "بانتظار المراجعة",
    "DsarRequestStatus.PartiallyFulfilled": "منفّذ جزئياً",
    "DsarRequestStatus.AppealOpen": "استئناف مفتوح",
    "ApplicationStatus.UnderScreening": "قيد الفرز",
    "ApplicationStatus.InterviewScheduled": "مقابلة مجدولة",
    "ApplicationStatus.Assessed": "مُقيَّم",
    "ApplicationStatus.Ranked": "مُصنَّف",
    "ApplicationStatus.OfferExtended": "عرض مُقدَّم",
}

DISPLAY_LINE = re.compile(r'^\s*\[Display\(')
MEMBER_LINE = re.compile(r'^(\s*)([A-Za-z_]\w*)\s*(=\s*.+?)?,?\s*$')
ALIAS_LINE = re.compile(r'^(\s*)([A-Za-z_]\w*)\s*=\s*([A-Za-z_]\w*)\s*,?\s*$')
SINGLE_LINE_ENUM = re.compile(
    r'^(\s*(?:///[^\n]*\n\s*)*)public enum ([A-Za-z_]\w+) \{ ([^}\n]+) \}\s*$',
    re.MULTILINE,
)


def label_for(enum_name: str, member: str, member_labels: dict) -> str:
    key = f"{enum_name}.{member}"
    if key in CONTEXT:
        return CONTEXT[key]
    if member in member_labels:
        return member_labels[member]
    if member in TRANSLATIONS:
        return TRANSLATIONS[member]
    print(f"WARNING: No translation for {key}", file=sys.stderr)
    return member


def has_display_above(lines: list[str], index: int) -> bool:
    j = index - 1
    while j >= 0 and lines[j].strip().startswith('///'):
        j -= 1
    return j >= 0 and DISPLAY_LINE.match(lines[j])


def process_enum_block(enum_name: str, body_lines: list[str]) -> tuple[list[str], bool]:
    labels: dict[str, str] = {}
    for line in body_lines:
        dm = DISPLAY_LINE.match(line)
        if dm:
            continue
        am = ALIAS_LINE.match(line)
        if am:
            alias, target = am.group(2), am.group(3)
            labels.setdefault(alias, label_for(enum_name, target, labels))

    # collect existing display labels
    for i, line in enumerate(body_lines):
        if MEMBER_LINE.match(line) and not line.strip().startswith('///'):
            j = i - 1
            while j >= 0 and body_lines[j].strip().startswith('///'):
                j -= 1
            if j >= 0:
                m = re.search(r'\[Display\(Name\s*=\s*"([^"]+)"\)\]', body_lines[j])
                if m:
                    labels[MEMBER_LINE.match(line).group(2)] = m.group(1)

    out: list[str] = []
    modified = False
    for i, line in enumerate(body_lines):
        mm = MEMBER_LINE.match(line)
        if mm and mm.group(2) not in ('public', 'private', 'protected', 'internal'):
            if not has_display_above(body_lines, i):
                member = mm.group(2)
                arabic = labels.get(member) or label_for(enum_name, member, labels)
                indent = mm.group(1) or '    '
                out.append(f'{indent}[Display(Name = "{arabic}")]')
                modified = True
        out.append(line)
    return out, modified


def process_file(path: Path) -> bool:
    text = path.read_text(encoding='utf-8')
    if 'public enum ' not in text:
        return False

    modified_any = False

    def expand_single_line(m: re.Match) -> str:
        nonlocal modified_any
        prefix, enum_name, members_blob = m.group(1), m.group(2), m.group(3)
        parts = [p.strip() for p in members_blob.split(',') if p.strip()]
        body_lines = []
        labels = {}
        for part in parts:
            pm = re.match(r'^([A-Za-z_]\w*)(\s*=.*)?$', part)
            if not pm:
                continue
            member, rest = pm.group(1), pm.group(2) or ''
            arabic = label_for(enum_name, member, labels)
            body_lines.append(f'    [Display(Name = "{arabic}")]')
            body_lines.append(f'    {member}{rest},')
        modified_any = True
        body = '\n'.join(body_lines)
        return f'{prefix}public enum {enum_name}\n{{\n{body}\n}}'

    new_text = SINGLE_LINE_ENUM.sub(expand_single_line, text)

    result_parts = []
    pos = 0
    while True:
        idx = new_text.find('public enum ', pos)
        if idx == -1:
            result_parts.append(new_text[pos:])
            break
        result_parts.append(new_text[pos:idx])
        brace_open = new_text.find('{', idx)
        if brace_open == -1:
            result_parts.append(new_text[idx:])
            break
        depth = 0
        brace_close = brace_open
        for i in range(brace_open, len(new_text)):
            ch = new_text[i]
            if ch == '{':
                depth += 1
            elif ch == '}':
                depth -= 1
                if depth == 0:
                    brace_close = i
                    break
        enum_header = new_text[idx:brace_open + 1]
        enum_name = re.search(r'public enum ([A-Za-z_]\w+)', enum_header).group(1)
        inner = new_text[brace_open + 1:brace_close]
        body_lines = inner.split('\n')
        new_body_lines, mod = process_enum_block(enum_name, body_lines)
        if mod:
            modified_any = True
            result_parts.append(enum_header)
            result_parts.append('\n'.join(new_body_lines))
            result_parts.append('}')
        else:
            result_parts.append(new_text[idx:brace_close + 1])
        pos = brace_close + 1

    if not modified_any:
        return False

    result = ''.join(result_parts)
    if 'using System.ComponentModel.DataAnnotations;' not in result:
        if re.search(r'^using ', result, re.MULTILINE):
            result = re.sub(
                r'^using ',
                'using System.ComponentModel.DataAnnotations;\nusing ',
                result,
                count=1,
                flags=re.MULTILINE,
            )
        else:
            result = 'using System.ComponentModel.DataAnnotations;\n\n' + result
    path.write_text(result, encoding='utf-8')
    return True


def find_enum_files(roots):
    for root in roots:
        root = Path(root)
        if not root.exists():
            continue
        for path in root.rglob('*.cs'):
            try:
                if 'public enum ' in path.read_text(encoding='utf-8'):
                    yield path
            except OSError:
                pass


def fix_english_fallback_labels(path: Path) -> bool:
    text = path.read_text(encoding='utf-8')
    if '[Display(' not in text:
        return False

    def repl(m):
        enum_name, member, label = m.group(1), m.group(2), m.group(3)
        if label != member:
            return m.group(0)
        arabic = label_for(enum_name, member, {})
        if arabic == member:
            return m.group(0)
        return f'[Display(Name = "{arabic}")]'

    # Fix display lines where we can infer enum from nearby member line
    lines = text.split('\n')
    changed = False
    for i, line in enumerate(lines):
        dm = re.match(r'(\s*)\[Display\(Name = "([A-Za-z_]\w*)"\)\]', line)
        if not dm or not re.match(r'^[A-Za-z]', dm.group(2)):
            continue
        # find member on next non-empty, non-comment line
        j = i + 1
        while j < len(lines) and (not lines[j].strip() or lines[j].strip().startswith('///')):
            j += 1
        if j >= len(lines):
            continue
        mm = MEMBER_LINE.match(lines[j])
        if not mm or mm.group(2) != dm.group(2):
            continue
        # find enum name above
        enum_name = None
        for k in range(i, -1, -1):
            em = re.search(r'public enum ([A-Za-z_]\w+)', lines[k])
            if em:
                enum_name = em.group(1)
                break
        if not enum_name:
            continue
        arabic = label_for(enum_name, dm.group(2), {})
        if arabic != dm.group(2):
            lines[i] = f'{dm.group(1)}[Display(Name = "{arabic}")]'
            changed = True
    if changed:
        path.write_text('\n'.join(lines), encoding='utf-8')
    return changed


def main():
    repo = Path(__file__).resolve().parents[1]
    roots = [repo / 'src/Shared/HRManagement.SharedKernel', repo / 'src/Services', repo / 'src/Shared/HRManagement.ServiceDefaults']
    count = 0
    fix_count = 0
    for p in sorted(set(find_enum_files(roots))):
        if process_file(p):
            print(f'Modified: {p.relative_to(repo)}')
            count += 1
        if fix_english_fallback_labels(p):
            print(f'Fixed English label: {p.relative_to(repo)}')
            fix_count += 1
    print(f'Total files modified: {count}')
    print(f'Total English labels fixed: {fix_count}')


if __name__ == '__main__':
    main()
