using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RuneReign.Api;

// Used only by "dotnet ef migrations add". Migrations are written for PostgreSQL, the hosted database.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RuneDbContext>
{
    public RuneDbContext CreateDbContext(string[] args)
    {
        DatabaseSetup.UsePlainTimestamps();
        var options = new DbContextOptionsBuilder<RuneDbContext>()
            .UseNpgsql("Host=localhost;Database=runereign;Username=postgres;Password=development")
            .Options;
        return new RuneDbContext(options);
    }
}
