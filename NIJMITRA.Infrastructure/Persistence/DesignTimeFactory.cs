using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NIJMITRA.Application.Abstractions;

namespace NIJMITRA.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling to build the model; no connection is opened.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("NIJMITRA_DESIGN_CONNECTION") ?? "Host=localhost;Database=nijmitra_design")
            .UseSnakeCaseNamingConvention().Options;
        return new AppDbContext(options, new NoUser(), TimeProvider.System);
    }

    private sealed class NoUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? SessionId => null;
        public Guid? ImpersonatorId => null;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
