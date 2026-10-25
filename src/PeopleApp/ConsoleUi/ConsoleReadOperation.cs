using PeopleApp.Api;
using Spectre.Console;

namespace PeopleApp.ConsoleUi;

internal static class ConsoleReadOperation
{
    public static async Task<bool> TryRunAsync(
        IAnsiConsole console,
        string message,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await console.Status().StartAsync(message, _ => operation());
            return true;
        }
        catch (PeopleApiException exception)
        {
            console.MarkupLine($"[red]The service returned HTTP {(int)exception.StatusCode} ({exception.StatusCode}).[/]");
            console.WriteLine(exception.ResponseBody);
        }
        catch (HttpRequestException exception)
        {
            console.WriteLine($"The service request failed: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            console.MarkupLine("[red]The service request timed out.[/]");
        }
        catch (InvalidDataException exception)
        {
            console.WriteLine($"The service returned an unusable response: {exception.Message}");
        }
        console.MarkupLine("[yellow]The read did not complete. No automatic retry was made.[/]");
        return false;
    }
}
