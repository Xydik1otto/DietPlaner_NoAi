using System.IO;

namespace DietPlanner.Infrastructure;

public interface IAppPaths
{
    string RootDirectory { get; }
    string DatabasePath { get; }
    string ReportsDirectory { get; }
    string LogsDirectory { get; }
    string ReportPointerPath { get; }
}

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DietPlanner");

        DatabasePath = Path.Combine(RootDirectory, "dietplanner.db");
        ReportsDirectory = Path.Combine(RootDirectory, "reports");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        ReportPointerPath = Path.Combine(RootDirectory, "report.txt");
    }

    public string RootDirectory { get; }
    public string DatabasePath { get; }
    public string ReportsDirectory { get; }
    public string LogsDirectory { get; }
    public string ReportPointerPath { get; }
}
