namespace CentralLogin.Build.Infrastructure;

/// <summary>
/// Abstraction over Pulumi execution. The initial implementation shells out to
/// the Pulumi CLI; it is isolated behind this interface so it can later be
/// replaced with the Pulumi Automation API without touching the tasks.
/// </summary>
public interface IPulumiRunner
{
    void Preview(string stack, string workingDirectory);

    void Up(string stack, string workingDirectory);
}
