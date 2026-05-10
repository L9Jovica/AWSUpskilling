using System;

namespace LambdaHandlers.Models
{
    /// <summary>
    /// Represents the status of a media processing job
    /// These values will be stored in DynamoDB
    /// </summary>
    public enum ProcessingStatus
    {
        /// <summary>
        /// Job has been created, waiting for processing to start
        /// </summary>
        Pending,
        
        /// <summary>
        /// Job is currently being processed
        /// </summary>
        Processing,
        
        /// <summary>
        /// Job has been successfully completed
        /// </summary>
        Completed,
        
        /// <summary>
        /// Job failed during processing
        /// </summary>
        Failed
    }
    
    /// <summary>
    /// Represents a media processing job metadata stored in DynamoDB
    /// This class maps to items in the MediaProcessingJobs table
    /// </summary>
    public class ProcessingMetadata
    {
        /// <summary>
        /// Unique identifier for the job (UUID/GUID)
        /// This is the Partition Key in DynamoDB
        /// </summary>
        public string JobId { get; set; } = string.Empty;
        
        /// <summary>
        /// Current status of the processing job
        /// </summary>
        public string Status { get; set; } = ProcessingStatus.Pending.ToString();
        
        /// <summary>
        /// ISO 8601 timestamp when the job was created
        /// Example: "2026-05-06T19:30:00Z"
        /// </summary>
        public string UploadedAt { get; set; } = string.Empty;
        
        /// <summary>
        /// ISO 8601 timestamp when processing started (optional)
        /// </summary>
        public string? ProcessingStartedAt { get; set; }
        
        /// <summary>
        /// ISO 8601 timestamp when processing completed (optional)
        /// </summary>
        public string? CompletedAt { get; set; }
        
        /// <summary>
        /// Original filename provided by the user
        /// </summary>
        public string OriginalFileName { get; set; } = string.Empty;
        
        /// <summary>
        /// Size of the uploaded file in bytes
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// MIME type of the file (e.g., "image/jpeg", "image/png")
        /// </summary>
        public string FileType { get; set; } = string.Empty;
        
        /// <summary>
        /// S3 key (path) for the input file
        /// Example: "jobs/12345678-1234-1234-1234-123456789abc/input.jpg"
        /// </summary>
        public string InputS3Key { get; set; } = string.Empty;
        
        /// <summary>
        /// S3 key (path) for the processed output file (optional)
        /// </summary>
        public string? OutputS3Key { get; set; }
        
        /// <summary>
        /// Width of the processed image in pixels (optional)
        /// </summary>
        public int? ProcessedWidth { get; set; }
        
        /// <summary>
        /// Height of the processed image in pixels (optional)
        /// </summary>
        public int? ProcessedHeight { get; set; }
        
        /// <summary>
        /// Error message if processing failed (optional)
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
