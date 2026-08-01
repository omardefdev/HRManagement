# مسارات الاستثناء — التعيين (FUN-001-D)

| السيناريو | السلوك المتوقع |
|---|---|
| `ICandidateAccepted` مكرر لنفس الموظف | لا معاملة Appointment ثانية مفتوحة (`HasOpenAppointmentTransaction`) |
| رفض مرشّح قبل Publish | حالة Rejected في التوظيف؛ لا `StartHireSaga` |
| تصحيح هوية أثناء التعيين | EmployeeCorrection / DATA-002؛ لا يعلن Hire مكتمل |
| تكرار NationalId | Unique + طابور GOV-003 `MasterDataDuplicateCase` |
| طرف saga يتعثر | PartyFaulted → ProcessIntervention؛ لا IHireCaseCompleted |
| Cancel من التدخل | ICancelHireSaga → Compensate |

قناة التدخل: `GET/POST /api/process-interventions` (Resume / Cancel) — إشعار SignalR اختياري؛ الدليل الأدنى = صف التدخل المفتوح.
