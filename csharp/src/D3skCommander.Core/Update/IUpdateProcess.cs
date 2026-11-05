namespace D3skCommander.Core.Update;

/// <summary>Runs a program and returns its exit code. Tests record the arguments instead of starting it.</summary>
public interface IUpdateProcess
{
    int Run(string program, IReadOnlyList<string> arguments);
}
