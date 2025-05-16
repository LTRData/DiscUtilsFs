using System.Globalization;

namespace DiscUtilsFs;

/// <summary>
/// Log to the console.
/// </summary>
public class ConsoleLogger(string loggerName = "", DateTimeFormatInfo? dateTimeFormatInfo = null)
    : FuseDotNet.Logging.ConsoleLogger(loggerName, dateTimeFormatInfo), DokanNet.Logging.ILogger;
