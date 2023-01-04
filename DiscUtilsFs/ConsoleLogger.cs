using FuseDotNet.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscUtilsFs;

public class ConsoleLogger : DokanNet.Logging.ILogger, ILogger
{
    private readonly string loggerName;

    private static readonly object Lock = new();

    public bool DebugEnabled => true;

    public ConsoleLogger(string loggerName = "")
    {
        this.loggerName = loggerName;
    }

    public void Debug(FormattableString message)
        => WriteMessage(ConsoleColor.DarkCyan, message);

    public void Info(FormattableString message) => WriteMessage(ConsoleColor.Cyan, message);

    public void Warn(FormattableString message)
        => WriteMessage(ConsoleColor.DarkYellow, message);

    public void Error(FormattableString message)
        => WriteMessage(ConsoleColor.Red, message);

    public void Fatal(FormattableString message)
        => WriteMessage(ConsoleColor.Red, message);

    private void WriteMessage(ConsoleColor newColor, FormattableString message)
        => WriteMessage(message.FormatMessageForLogging(addDateTime: true, Environment.CurrentManagedThreadId, null, loggerName), newColor);

    private static void WriteMessage(string message, ConsoleColor newColor)
    {
        lock (Lock)
        {
            var foregroundColor = Console.ForegroundColor;
            Console.ForegroundColor = newColor;
            Console.WriteLine(message);
            Console.ForegroundColor = foregroundColor;
        }
    }
}
