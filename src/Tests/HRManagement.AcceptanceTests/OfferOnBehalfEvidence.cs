using System.Net.Http.Headers;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// HCM-030 (PLAN-30 J): HR on-behalf acceptance (<c>OfferLetters/AcceptOffer</c>) needs a reason of at least 20 characters
/// and the signed paper acceptance as evidence (pdf/jpg/png ≤ 5 MB, multipart). AC-HCM-27/28/29 accept offers this way.
/// </summary>
internal static class OfferOnBehalfEvidence
{
    public const string Reason = "قبول ورقي موقّع سلّمه المرشح إلى الموارد البشرية (اختبار قبول آلي)";
    public const string FileName = "signed-acceptance.pdf";

    /// <summary>Minimal clean PDF (magic bytes <c>%PDF</c>), as in AC-28 without the EICAR signature.</summary>
    public static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    public static List<KeyValuePair<string, string>> AcceptFields(string offerId) =>
        [new("id", offerId), new("Reason", Reason)];

    /// <summary>URL-encoded form, or multipart with the evidence PDF as <c>EvidenceFile</c>.</summary>
    public static HttpContent Content(IEnumerable<KeyValuePair<string, string>> fields, bool withEvidence)
    {
        if (!withEvidence)
            return new FormUrlEncodedContent(fields);

        var multipart = new MultipartFormDataContent();
        foreach (var field in fields)
            multipart.Add(new StringContent(field.Value), field.Key);
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "EvidenceFile", FileName);
        return multipart;
    }
}
