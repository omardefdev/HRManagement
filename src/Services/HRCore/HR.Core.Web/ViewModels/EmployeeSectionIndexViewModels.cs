using HR.Core.Domain.Entities;

namespace HR.Core.Web.ViewModels;

public class QualificationsIndexViewModel
{
    public Employee Employee { get; set; } = null!;
    public IReadOnlyList<Qualification> Qualifications { get; set; } = [];
    public QualificationCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
}

public class ContactsIndexViewModel
{
    public Employee Employee { get; set; } = null!;
    public IReadOnlyList<ContactInfo> Contacts { get; set; } = [];
    public ContactCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
}

public class DocumentsIndexViewModel
{
    public Employee Employee { get; set; } = null!;
    public IReadOnlyList<EmployeeDocument> Documents { get; set; } = [];
    public DocumentCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
}
