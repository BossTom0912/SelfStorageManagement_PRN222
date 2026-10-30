namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class CustomerNotificationResponse
{
    public long Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
