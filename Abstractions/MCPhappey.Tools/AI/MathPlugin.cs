
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class MathPlugin
{
    [Description("Add two numbers.")]
    [McpServerTool(ReadOnly = true)]
    public static double MathPlugin_Add(
         [Description("The first number to add")] double number1,
         [Description("The second number to add")] double number2
     )
    {
        return number1 + number2;
    }

    [Description("Subtract two numbers.")]
    [McpServerTool(ReadOnly = true)]
    public static double MathPlugin_Subtract(
        [Description("The first number to subtract from")] double number1,
        [Description("The second number to subtract away")] double number2
    )
    {
        return number1 - number2;
    }

    [Description("Multiply two numbers.")]
    [McpServerTool(ReadOnly = true)]
    public static double MathPlugin_Multiply(
        [Description("The first number to multiply")] double number1,
        [Description("The second number to multiply")] double number2
    )
    {
        return number1 * number2;
    }

    [Description("Divide two numbers.")]
    [McpServerTool(ReadOnly = true)]
    public static double MathPlugin_Divide(
        [Description("The first number to divide from")] double number1,
        [Description("The second number to divide by")] double number2
    )
    {
        return number1 / number2;
    }

    [Description("Round a number to the target number of decimal places.")]
    [McpServerTool(ReadOnly = true)]
    public static double MathPlugin_Round(
        [Description("The number to round")] double number1,
        [Description("The number of decimal places to round to")] double number2
    )
    {
        return Math.Round(number1, (int)number2);
    }
}