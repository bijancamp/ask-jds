namespace Api.Models;

/// <summary>
/// Represents a job description message for Azure Service Bus queue processing
/// </summary>
public class JobDescriptionMessage
{
    /// <summary>
    /// Unique identifier for the message
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The job description payload
    /// </summary>
    public JobDescriptionSubmission Payload { get; set; } = new();

    /// <summary>
    /// Creates a new message with generated ID and current timestamp
    /// </summary>
    /// <param name="payload">The job description submission to wrap</param>
    /// <returns>A new JobDescriptionMessage instance</returns>
    public static JobDescriptionMessage Create(JobDescriptionSubmission payload)
    {
        return new JobDescriptionMessage
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
    }
}