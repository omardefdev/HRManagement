1. AC-30c: an apply with a CV named malware.exe (MZ bytes) → the form is shown again, nothing is saved.
2. AC-30c: an apply with a 5 MB + 1 byte PDF → refused, nothing is saved.
   (Steps 1–2 are refused before any upload, so they run without File.Storage.)
3. AC-30c: a citizen applies with a CV and a certificate (PDF) → both listed on HR Details (CandidateDocuments Cv, Certificate)
   and «سليم» after the ClamAV scan; the CV downloads through the HR proxy (%PDF). Skipped when the apply page reports
   «تعذّر رفع المستندات حالياً…» (File.Storage not in the acceptance stack).
4. AC-30c: an apply with eicar.pdf (minimal PDF + EICAR string, the AC-28 pattern) is accepted; File.Storage marks the file
   Infected/Quarantined; HR Details shows «محجور — غير متاح» without a download link and the HR download proxy refuses it.