using System.ComponentModel.DataAnnotations;

namespace Api.Models;

/// <summary>
/// Represents a job description submission request from the API client
/// </summary>
public class JobDescriptionSubmission
{
    /// <summary>
    /// The job title
    /// </summary>
    [Required(ErrorMessage = "Title is required")]
    [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The company name
    /// </summary>
    [Required(ErrorMessage = "Company is required")]
    [StringLength(100, ErrorMessage = "Company name cannot exceed 100 characters")]
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// The full job description text
    /// </summary>
    [Required(ErrorMessage = "Description is required")]
    [StringLength(10000, ErrorMessage = "Description cannot exceed 10,000 characters")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The job location (optional)
    /// </summary>
    [StringLength(100, ErrorMessage = "Location cannot exceed 100 characters")]
    public string? Location { get; set; }

    /// <summary>
    /// When the job was originally posted (optional)
    /// </summary>
    public DateTimeOffset? PostingDate { get; set; }

    /// <summary>
    /// The original ID from Workday system (optional)
    /// </summary>
    [StringLength(50, ErrorMessage = "WorkdayId cannot exceed 50 characters")]
    public string? WorkdayId { get; set; }
}