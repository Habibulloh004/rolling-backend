using Microsoft.AspNetCore.Mvc;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class UploadsController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<UploadsController> _logger;
    private readonly IConfiguration _configuration;
    private readonly long _maxFileSize;
    private readonly string[] _allowedImageExtensions;
    private readonly string[] _allowedVideoExtensions;

    public UploadsController(
        IWebHostEnvironment environment,
        ILogger<UploadsController> logger,
        IConfiguration configuration)
    {
        _environment = environment;
        _logger = logger;
        _configuration = configuration;

        // Read from configuration with defaults
        var maxFileSizeMB = _configuration.GetValue("FileUpload:MaxFileSizeMB", 100);
        _maxFileSize = maxFileSizeMB * 1024 * 1024; // Convert MB to bytes

        _allowedImageExtensions = _configuration.GetSection("FileUpload:AllowedImageExtensions").Get<string[]>()
            ?? new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        _allowedVideoExtensions = _configuration.GetSection("FileUpload:AllowedVideoExtensions").Get<string[]>()
            ?? new[] { ".mp4", ".mov", ".avi", ".webm" };
    }

    /// <summary>
    /// Upload single or multiple image/video files
    /// Accepts both "file" (single) and "files" (multiple) form fields
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            var files = Request.Form.Files;

            if (files == null || files.Count == 0)
            {
                return BadRequest(new { error = "No files uploaded" });
            }

            // Create uploads directory if it doesn't exist
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsPath))
            {
                Directory.CreateDirectory(uploadsPath);
            }

            var uploadedFiles = new List<object>();
            var errors = new List<string>();

            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    errors.Add($"{file.FileName}: File is empty");
                    continue;
                }

                if (file.Length > _maxFileSize)
                {
                    errors.Add($"{file.FileName}: File size exceeds {_maxFileSize / 1024 / 1024} MB limit");
                    continue;
                }

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

                var isImage = _allowedImageExtensions.Contains(extension);
                var isVideo = _allowedVideoExtensions.Contains(extension);

                if (!isImage && !isVideo)
                {
                    errors.Add($"{file.FileName}: Invalid file type. Only images and videos are allowed");
                    continue;
                }

                try
                {
                    // Generate unique filename
                    var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                    var filePath = Path.Combine(uploadsPath, uniqueFileName);

                    // Save file
                    await using var stream = new FileStream(filePath, FileMode.Create);
                    await file.CopyToAsync(stream, cancellationToken);

                    // Generate relative URL path
                    var fileUrl = $"/uploads/{uniqueFileName}";

                    _logger.LogInformation("File uploaded: {FileName} -> {Url}", file.FileName, fileUrl);

                    uploadedFiles.Add(new
                    {
                        url = fileUrl,
                        fileName = uniqueFileName,
                        originalFileName = file.FileName,
                        size = file.Length,
                        type = isImage ? "image" : "video"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file {FileName}", file.FileName);
                    errors.Add($"{file.FileName}: Failed to upload - {ex.Message}");
                }
            }

            // Return response based on single or multiple files
            if (files.Count == 1 && uploadedFiles.Count == 1)
            {
                // Single file upload - return single object
                return Ok(new
                {
                    success = true,
                    url = ((dynamic)uploadedFiles[0]).url,
                    fileName = ((dynamic)uploadedFiles[0]).fileName,
                    originalFileName = ((dynamic)uploadedFiles[0]).originalFileName,
                    size = ((dynamic)uploadedFiles[0]).size,
                    type = ((dynamic)uploadedFiles[0]).type
                });
            }
            else
            {
                // Multiple files upload - return array
                return Ok(new
                {
                    success = uploadedFiles.Count > 0,
                    count = uploadedFiles.Count,
                    files = uploadedFiles,
                    errors = errors.Count > 0 ? errors : null
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading files");
            return StatusCode(500, new { error = "Failed to upload files" });
        }
    }

    /// <summary>
    /// Delete an uploaded file
    /// </summary>
    [HttpDelete("{fileName}")]
    public IActionResult DeleteFile(string fileName)
    {
        try
        {
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
            var filePath = Path.Combine(uploadsPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { error = "File not found" });
            }

            System.IO.File.Delete(filePath);

            _logger.LogInformation("File deleted: {FileName}", fileName);

            return Ok(new { success = true, message = "File deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName}", fileName);
            return StatusCode(500, new { error = "Failed to delete file" });
        }
    }
}
