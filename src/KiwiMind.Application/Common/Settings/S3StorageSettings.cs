namespace KiwiMind.Application.Common.Settings;

public class S3StorageSettings
{
    public const string SectionName = "S3Storage";

    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "kiwimind-documents";
}
