1. Two citizens (L and P) apply on REQ-2026-A-CX01.
2. AC-30f: L requests a code; five wrong codes → «رمز التحقق غير صحيح…» then locked (PortalLockedUntil, audit
   CandidatePortalLocked); the real code (FileDrop) is then refused with «تم إيقاف الدخول مؤقتاً…»; no careers_session.
3. AC-30f: L's reference with P's national id → the same neutral message, no code is created for either, no session;
   /careers/my redirects to the sign-in page.