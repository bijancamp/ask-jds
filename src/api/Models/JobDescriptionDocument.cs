using Azure.Search.Documents.Indexes;

namespace Api.Models;

/// <summary>
/// Represents a job description document for Azure AI Search indexing
/// </summary>
public class JobDescriptionDocument
{
    /// <summary>
    /// Unique identifier for the document (search key)
    /// </summary>
    [SimpleField(IsKey = true)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The job title (searchable and filterable)
    /// </summary>
    [SearchableField(IsFilterable = true)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The company name (searchable and filterable)
    /// </summary>
    [SearchableField(IsFilterable = true)]
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// The full job description text (searchable)
    /// </summary>
    [SearchableField]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The job location (searchable and filterable, optional)
    /// </summary>
    [SearchableField(IsFilterable = true)]
    public string? Location { get; set; }

    /// <summary>
    /// When the job was originally posted (filterable and sortable, optional)
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true)]
    public DateTimeOffset? PostingDate { get; set; }

    /// <summary>
    /// The original ID from Workday system (retrievable, optional)
    /// </summary>
    [SimpleField]
    public string? WorkdayId { get; set; }

    /// <summary>
    /// When the job description was ingested into the system (filterable and sortable)
    /// </summary>
    [SimpleField(IsFilterable = true, IsSortable = true)]
    public DateTimeOffset IngestionTime { get; set; }

    /// <summary>
    /// Creates a search document from a job description message
    /// </summary>
    /// <param name="message">The job description message to convert</param>
    /// <returns>A new JobDescriptionDocument instance</returns>
    public static JobDescriptionDocument FromMessage(JobDescriptionMessage message)
    {
        return new JobDescriptionDocument
        {
            Id = message.Id.ToString(),
            Title = message.Payload.Title,
            Company = message.Payload.Company,
            Description = message.Payload.Description,
            Location = message.Payload.Location,
            PostingDate = message.Payload.PostingDate,
            WorkdayId = message.Payload.WorkdayId,
            IngestionTime = DateTimeOffset.UtcNow
        };
    }
}