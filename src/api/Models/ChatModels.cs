using System.ComponentModel.DataAnnotations;

namespace Api.Models;

/// <summary>
/// Request model for chat functionality
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The user's question or message
    /// </summary>
    [Required(ErrorMessage = "Message is required")]
    [StringLength(1000, ErrorMessage = "Message cannot exceed 1000 characters")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional conversation history for context
    /// </summary>
    public List<ChatMessage> History { get; set; } = new();
}

/// <summary>
/// Response model for chat functionality
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// The AI assistant's response
    /// </summary>
    public string Response { get; set; } = string.Empty;

    /// <summary>
    /// Sources from the search index that were used to generate the response
    /// </summary>
    public List<DocumentSource> Sources { get; set; } = new();

    /// <summary>
    /// Unique identifier for this conversation turn
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a single message in the chat conversation
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// The role of the message sender (user or assistant)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The content of the message
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a document source used in generating the chat response
/// </summary>
public class DocumentSource
{
    /// <summary>
    /// The document ID from the search index
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The job title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The company name
    /// </summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// The location
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Relevant excerpt from the job description
    /// </summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>
    /// Search relevance score
    /// </summary>
    public double Score { get; set; }
}