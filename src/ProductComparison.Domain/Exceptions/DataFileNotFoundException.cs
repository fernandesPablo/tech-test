namespace ProductComparison.Domain.Exceptions;

public class DataFileNotFoundException : Exception
{
    public DataFileNotFoundException(string fileName, string path) 
        : base($"Data file '{fileName}' not found at path: {path}. Please ensure the file exists and has correct permissions.")
    {
        FileName = fileName;
        FilePath = path;
    }

    public string FileName { get; }
    public string FilePath { get; }
}