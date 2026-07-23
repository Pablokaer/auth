namespace CentralLogin.Build.Infrastructure;

/// <summary>Pulumi execution via the <c>pulumi</c> CLI.</summary>
public sealed class PulumiCliRunner : IPulumiRunner
{
    private readonly BuildContext _context;

    public PulumiCliRunner(BuildContext context) => _context = context;

    public void Preview(string stack, string workingDirectory)
        => _context.RunProcess("pulumi", $"preview --stack {stack} --non-interactive", workingDirectory);

    public void Up(string stack, string workingDirectory)
        // --yes for non-interactive apply; production is additionally gated by the
        // PulumiDeploy task and by the deployment workflow's manual approval.
        => _context.RunProcess("pulumi", $"up --yes --stack {stack} --non-interactive", workingDirectory);
}
