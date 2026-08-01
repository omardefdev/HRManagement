namespace HRManagement.MessageContracts.Events;



public interface IMedicalRecommendationIssued

{

    Guid EmployeeId { get; }

    string RecommendationType { get; }

    string Details { get; }

    DateTime IssuedAt { get; }



    Guid? ReportId => null;

    DateOnly? CommitteeDate => null;

    string? RecommendationEnum => null;

    Guid? CorrelationId => null;

    string? SourceSystem => null;

    Guid RecommendationId { get; }



    Guid? TenantId => null;




    int SchemaVersion => 2;

}

