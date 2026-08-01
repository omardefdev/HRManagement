namespace HRManagement.MessageContracts.Events;



public interface ITrainingCompleted

{

    Guid EmployeeId { get; }

    Guid CourseId { get; }

    decimal Score { get; }

    DateTime CompletedAt { get; }



    string? CertificateUrl => null;

    Guid? CertificateFileId => null;



    string? CourseTitleAr => null;

    string? CourseCode => null;

    Guid? ProviderId => null;

    int? DurationDays => null;

    Guid? CorrelationId => null;

    string? SourceSystem => null;



    Guid? TenantId => null;




    int SchemaVersion => 3;

}

