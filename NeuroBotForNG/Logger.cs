using Telegram.Bot.Types;

namespace NeuroBotForNG;

public static class Logger
{
    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("[");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(DateTime.Now);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write(" : ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.Write("ERROR");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("]");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(": "+message+"\n");
    }
    
    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("[");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(DateTime.Now);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write(" : ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.Write("WARN");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("]");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(": "+message+"\n");
    }
    
    public static void Log(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("[");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(DateTime.Now);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write(" : ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Error.Write("LOG");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("]");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(": "+message+"\n");
    }
    
    public static void Message(Message message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("[");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(DateTime.Now);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write(" : ");
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Error.Write(message.From!.FirstName);
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Error.Write("]");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(": " + message.Text + "\n");
    }
}