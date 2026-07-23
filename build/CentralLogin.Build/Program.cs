using Cake.Frosting;
using CentralLogin.Build;

// Entry point for the Cake Frosting pipeline. The process exit code reflects
// task success/failure (non-zero on failure), which CI/CD relies on.
return new CakeHost()
    .UseContext<BuildContext>()
    .Run(args);
